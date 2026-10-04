// Deterministic tests of the staging acceptance's admin-write recovery (support/override-recovery.mjs): which target a
// restore reads, navigates and writes, when the pending-restore journal is cleared, that cleanup failures never skip the
// restore, and that a restore after an interrupt never runs beside another. No browser, stack or staging: the flag rows,
// the users list and the admin context are fakes, and the journal is the real session store in a temporary folder outside
// the repository.
//
//   dotnet run --project developer-cli -- blazor-harness staging-recovery
//
// The harness passes --browser, which these tests ignore. Exit code 1 when a test fails.

import assert from "node:assert/strict";
import { mkdtempSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { sessionStore } from "./support/deployed.mjs";
import { createOverrideRecovery } from "./support/override-recovery.mjs";

const host = "back-office.staging.test";
const recordedFlag = "compact-view";
const otherFlag = "beta-dashboard";
const owner = { userId: "usr_owner", tenantId: "1553434734103793664", email: "jasper@etara.dk" };
const colleague = { userId: "usr_colleague", tenantId: "1553434734103793664", email: "colleague@etara.dk" };
const disabled = { isEnabled: false, source: "default" };
const enabledByOverride = { isEnabled: true, source: "manual_override" };
const disabledByOverride = { isEnabled: false, source: "manual_override" };

const keyOf = (flagKey, { userId, tenantId }) => `${flagKey}|${userId}|${tenantId}`;

// The account API and the flag detail's users list as check 8 and its restore see them: one state per flag, user and account
function createWorld(states) {
  const users = { [owner.userId]: owner.email, [colleague.userId]: colleague.email };
  const world = { states: { ...states }, mutations: [], active: 0, maxActive: 0, toggleFailures: [], closeFailures: [], held: undefined, onMutationStart: undefined, inert: false, shownRow: undefined };

  async function mutate(key, action) {
    world.active++;
    world.maxActive = Math.max(world.maxActive, world.active);
    world.mutations.push({ key, action });
    try {
      world.onMutationStart?.();
      while (world.held !== undefined) await world.held.promise;
      const failure = world.toggleFailures.shift();
      if (failure !== undefined) throw new Error(failure);
      if (world.inert) return;
      world.states[key] = action === "remove" ? { ...disabled } : { isEnabled: !world.states[key].isEnabled, source: "manual_override" };
    } finally {
      world.active--;
    }
  }

  world.fetchUserFlags = async (userId) =>
    Object.entries(world.states)
      .map(([key, state]) => [key.split("|"), state])
      .filter(([[, user]]) => user === String(userId))
      .map(([[flagKey, , tenantId], state]) => ({ flagKey, tenantId, ...state }));

  // Like openOverrideRow: the list of the named flag searched for the named email, then the row of the user and account
  world.openRow = async (page, { flagKey, email, userId, tenantId }) => {
    const visible = Object.keys(world.states)
      .map((key) => key.split("|"))
      .filter(([flag, user]) => flag === flagKey && users[user]?.includes(email));
    if (!visible.some(([, user, tenant]) => user === String(userId) && tenant === String(tenantId))) {
      throw new Error(`Timeout: no row ${userId}-${tenantId} on the ${flagKey} list searched for ${email}.`);
    }
    const key = `${flagKey}|${userId}|${tenantId}`;
    const shown = world.shownRow ?? { userId, tenantId };
    return {
      testId: `feature-flag-user-override-${shown.userId}-${shown.tenantId}`,
      text: `${users[shown.userId]} ${shown.tenantId}`,
      toggle: () => mutate(key, "toggle"),
      removeOverride: () => mutate(key, "remove")
    };
  };

  world.openSession = async () => ({
    newPage: async () => ({}),
    close: async () => {
      const failure = world.closeFailures.shift();
      if (failure !== undefined) throw new Error(failure);
    }
  });

  world.hold = () => {
    let release;
    const promise = new Promise((resolve) => {
      release = resolve;
    });
    world.held = { promise };
    return () => {
      world.held = undefined;
      release();
    };
  };

  return world;
}

// The real session store in a folder outside the repository; every clear records the recorded target's state at that moment
function createStore(t, world) {
  const folder = mkdtempSync(path.join(os.tmpdir(), "staging-recovery-"));
  t.after(() => rmSync(folder, { recursive: true, force: true }));
  const store = sessionStore(folder, host);
  const clears = [];
  return {
    store: {
      ...store,
      clearPendingRestore: () => {
        clears.push({ ...world.states[keyOf(recordedFlag, owner)] });
        store.clearPendingRestore();
      }
    },
    clears
  };
}

function createRecovery(t, world, current = { flagKey: recordedFlag, email: owner.email }) {
  const { store, clears } = createStore(t, world);
  const log = [];
  let clock = 0;
  const recovery = createOverrideRecovery({
    store,
    backOfficeHost: host,
    current,
    fetchUserFlags: world.fetchUserFlags,
    openSession: world.openSession,
    openRow: world.openRow,
    log: (message) => log.push(message),
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
      await new Promise((resolve) => setImmediate(resolve));
    }
  });
  return { recovery, store, clears, log };
}

// A journal as check 8 writes it today, with or without the host
const journalOf = (before, extra = {}) => ({ flagKey: recordedFlag, email: owner.email, userId: owner.userId, tenantId: owner.tenantId, before, recordedAt: "2026-10-04T12:00:00.000Z", ...extra });

const yes = async () => "y";
const settle = async (rounds = 20) => {
  for (let round = 0; round < rounds; round++) await new Promise((resolve) => setImmediate(resolve));
};
const resume = async (recovery) => {
  try {
    return await recovery.resumeLeftover(yes);
  } catch (error) {
    return `threw: ${error.message}`;
  }
};
const recordedTargetMutations = (world) => world.mutations.filter((mutation) => mutation.key === keyOf(recordedFlag, owner));

// Check 8's write: record the target, press its switch once through the same users list, then optionally fail the check
const writeOnce =
  (world, { failWith } = {}) =>
  async (session, { readFlag, record, mutate, waitForFlagChange }) => {
    const target = { flagKey: recordedFlag, ...owner };
    const before = await readFlag(target);
    record(target, before);
    const row = await world.openRow(await session.newPage(), target);
    await mutate(() => row.toggle());
    const after = await waitForFlagChange(target, before);
    if (failWith !== undefined) throw new Error(failWith);
    return { before, after };
  };

test("a leftover resumed with another --flag restores only the recorded flag", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: enabledByOverride, [keyOf(otherFlag, owner)]: disabledByOverride });
  const { recovery, store } = createRecovery(t, world, { flagKey: otherFlag, email: owner.email });
  store.writePendingRestore(journalOf(disabled));

  const result = await resume(recovery);

  assert.equal(result, "restored");
  assert.deepEqual(world.states[keyOf(recordedFlag, owner)], disabled);
  assert.deepEqual(world.states[keyOf(otherFlag, owner)], disabledByOverride);
  assert.deepEqual(
    world.mutations.filter((mutation) => mutation.key !== keyOf(recordedFlag, owner)),
    []
  );
  assert.equal(store.readPendingRestore(), undefined);
});

test("a leftover resumed with another --app-user restores the recorded user", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: enabledByOverride, [keyOf(recordedFlag, colleague)]: disabled });
  const { recovery, store } = createRecovery(t, world, { flagKey: recordedFlag, email: colleague.email });
  store.writePendingRestore(journalOf(disabled));

  const result = await resume(recovery);

  assert.equal(result, "restored");
  assert.deepEqual(world.states[keyOf(recordedFlag, owner)], disabled);
  assert.deepEqual(world.states[keyOf(recordedFlag, colleague)], disabled);
  assert.equal(store.readPendingRestore(), undefined);
});

test("another flag already in the recorded state never clears the journal; the verified recorded flag does", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: enabledByOverride, [keyOf(otherFlag, owner)]: disabled });
  const { recovery, store, clears } = createRecovery(t, world, { flagKey: otherFlag, email: owner.email });
  store.writePendingRestore(journalOf(disabled));

  await resume(recovery);

  assert.ok(clears.length > 0, "the journal was never cleared");
  for (const stateAtClear of clears) assert.deepEqual(stateAtClear, disabled, "the journal was cleared while the recorded flag was still changed");
  assert.deepEqual(world.states[keyOf(recordedFlag, owner)], disabled);
  assert.deepEqual(world.states[keyOf(otherFlag, owner)], disabled);
  assert.deepEqual(recordedTargetMutations(world).length, 1);
});

test("a journal missing a field changes nothing, is kept, and the recovery reports it", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: enabledByOverride });
  const { recovery, store, log } = createRecovery(t, world);
  const journal = journalOf(disabled);
  delete journal.userId;
  store.writePendingRestore(journal);

  const result = await resume(recovery);

  assert.equal(result, "failed");
  assert.deepEqual(world.mutations, []);
  assert.deepEqual(store.readPendingRestore(), journal);
  assert.ok(log.some((line) => line.includes(store.pendingRestoreFile)), "the journal's path is not reported");
});

test("a recorded user without a row for the recorded flag changes nothing and keeps the journal", async (t) => {
  const world = createWorld({ [keyOf(otherFlag, owner)]: enabledByOverride });
  const { recovery, store } = createRecovery(t, world);
  store.writePendingRestore(journalOf(disabled));

  const result = await resume(recovery);

  assert.equal(result, "failed");
  assert.deepEqual(world.mutations, []);
  assert.notEqual(store.readPendingRestore(), undefined);
});

test("a journal written before the host was recorded is recovered under the host of its file", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: enabledByOverride });
  const { recovery, store } = createRecovery(t, world);
  store.writePendingRestore(journalOf(disabled));

  const result = await resume(recovery);

  assert.equal(result, "restored");
  assert.deepEqual(world.states[keyOf(recordedFlag, owner)], disabled);
  assert.equal(store.readPendingRestore(), undefined);
});

test("a journal recorded for another back-office host changes nothing and is kept", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: enabledByOverride });
  const { recovery, store } = createRecovery(t, world);
  store.writePendingRestore(journalOf(disabled, { backOfficeHost: "back-office.other.test" }));

  const result = await resume(recovery);

  assert.equal(result, "failed");
  assert.deepEqual(world.mutations, []);
  assert.notEqual(store.readPendingRestore(), undefined);
});

test("the approval prompt names the saved host, flag, user and account, and says when the arguments differ", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: enabledByOverride, [keyOf(otherFlag, owner)]: disabled });
  const { recovery, store, log } = createRecovery(t, world, { flagKey: otherFlag, email: colleague.email });
  store.writePendingRestore(journalOf(disabled));
  let prompt;

  await recovery.resumeLeftover(async (question) => {
    prompt = [...log, question].join("\n");
    return "n";
  });

  for (const expected of [host, recordedFlag, owner.email, owner.userId, owner.tenantId, otherFlag, colleague.email]) {
    assert.ok(prompt.includes(expected), `the prompt does not name ${expected}:\n${prompt}`);
  }
  assert.deepEqual(world.mutations, []);
  assert.notEqual(store.readPendingRestore(), undefined);
});

test("a row that is not the recorded user's is never pressed, and the journal stays", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: enabledByOverride, [keyOf(recordedFlag, colleague)]: enabledByOverride });
  const { recovery, store } = createRecovery(t, world);
  store.writePendingRestore(journalOf(disabled));
  world.shownRow = colleague;

  const result = await resume(recovery);

  assert.equal(result, "failed");
  assert.deepEqual(world.mutations, []);
  assert.notEqual(store.readPendingRestore(), undefined);
});

test("a failed close of check 8's context still restores the recorded state and reports the close failure", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: disabled });
  const { recovery, store } = createRecovery(t, world);
  world.closeFailures.push("closing the context failed");

  await assert.rejects(recovery.runWrite({ write: writeOnce(world) }), /closing the context failed/);

  assert.deepEqual(world.states[keyOf(recordedFlag, owner)], disabled);
  assert.equal(store.readPendingRestore(), undefined);
});

test("a failed check and a failed close are both reported, and the restore still runs", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: disabled });
  const { recovery, store } = createRecovery(t, world);
  world.closeFailures.push("closing the context failed");

  await assert.rejects(recovery.runWrite({ write: writeOnce(world, { failWith: "the preferences check failed" }) }), (error) => {
    assert.match(error.message, /the preferences check failed/);
    assert.match(error.message, /closing the context failed/);
    return true;
  });

  assert.deepEqual(world.states[keyOf(recordedFlag, owner)], disabled);
  assert.equal(store.readPendingRestore(), undefined);
});

test("a failed restore keeps the journal and is reported with the check's own failure", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: disabled });
  const { recovery, store } = createRecovery(t, world);
  const write = writeOnce(world, { failWith: "the preferences check failed" });

  await assert.rejects(
    recovery.runWrite({
      write: async (session, tools) => {
        try {
          return await write(session, tools);
        } finally {
          world.toggleFailures.push("the switch did not answer");
        }
      }
    }),
    (error) => {
      assert.match(error.message, /the preferences check failed/);
      assert.match(error.message, /the switch did not answer/);
      assert.ok(error.message.includes(store.pendingRestoreFile), "the journal's path is not reported");
      return true;
    }
  );

  assert.deepEqual(world.states[keyOf(recordedFlag, owner)], enabledByOverride);
  assert.notEqual(store.readPendingRestore(), undefined);
});

test("a failed restore whose context also fails to close reports both", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: enabledByOverride });
  const { recovery, store, log } = createRecovery(t, world);
  store.writePendingRestore(journalOf(disabled));
  world.toggleFailures.push("the switch did not answer");
  world.closeFailures.push("closing the context failed");

  const result = await resume(recovery);

  const reported = [result, ...log].join("\n");
  assert.match(reported, /the switch did not answer/);
  assert.match(reported, /closing the context failed/);
  assert.notEqual(store.readPendingRestore(), undefined);
});

test("a failed restore of a leftover is reported, ends the start, and keeps the journal", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: enabledByOverride });
  const { recovery, store, log } = createRecovery(t, world);
  store.writePendingRestore(journalOf(disabled));
  world.toggleFailures.push("the switch did not answer");

  const result = await resume(recovery);

  assert.equal(result, "failed");
  assert.ok(log.some((line) => line.includes("the switch did not answer") && line.includes(store.pendingRestoreFile)), log.join("\n"));
  assert.notEqual(store.readPendingRestore(), undefined);
});

test("an interrupt during check 8's restore waits for it, so the recorded target is changed by one restore at a time", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: disabled });
  const { recovery, store } = createRecovery(t, world);
  let release;
  let restoreStarted;
  const restoreStart = new Promise((resolve) => {
    restoreStarted = resolve;
  });
  const write = writeOnce(world, { failWith: "the preferences check failed" });

  const run = recovery
    .runWrite({
      write: async (session, tools) => {
        try {
          return await write(session, tools);
        } finally {
          world.onMutationStart = () => {
            world.onMutationStart = undefined;
            release = world.hold();
            restoreStarted();
          };
        }
      }
    })
    .catch((error) => error);
  await restoreStart;
  const interrupted = recovery.interrupt();
  await settle();
  release();
  await Promise.all([run, interrupted]);

  assert.equal(world.maxActive, 1);
  assert.equal(recordedTargetMutations(world).length, 2, JSON.stringify(world.mutations));
  assert.deepEqual(world.states[keyOf(recordedFlag, owner)], disabled);
  assert.equal(store.readPendingRestore(), undefined);
});

test("after an interrupt, check 8 makes no further write", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: disabled });
  const { recovery, store } = createRecovery(t, world);

  await recovery
    .runWrite({
      write: async (session, tools) => {
        const target = { flagKey: recordedFlag, ...owner };
        tools.record(target, await tools.readFlag(target));
        const row = await world.openRow(await session.newPage(), target);
        await recovery.interrupt();
        await tools.mutate(() => row.toggle());
        return {};
      }
    })
    .catch(() => undefined);

  assert.deepEqual(world.mutations, []);
  assert.deepEqual(world.states[keyOf(recordedFlag, owner)], disabled);
  assert.equal(store.readPendingRestore(), undefined);
});

test("a state the switch does not change stops after a bounded wait, keeps the journal, and is reported", async (t) => {
  const world = createWorld({ [keyOf(recordedFlag, owner)]: enabledByOverride });
  const { recovery, store, log } = createRecovery(t, world);
  store.writePendingRestore(journalOf(disabledByOverride));
  world.inert = true;

  const result = await resume(recovery);

  assert.equal(recordedTargetMutations(world).length, 1);
  assert.match([result, ...log].join("\n"), /did not change/);
  assert.notEqual(store.readPendingRestore(), undefined);
});
