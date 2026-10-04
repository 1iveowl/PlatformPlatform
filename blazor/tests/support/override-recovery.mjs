// The admin write of the staging acceptance (check 8) and its recovery: the pending-restore journal, the restore of the
// target user's override to the state recorded before the write, and the restore after an interrupt or on the next start.
// Every effect goes through the functions passed in, so blazor/tests/staging-recovery.mjs runs this without a browser,
// a stack or staging.
//
// The recorded target (back-office host, flag, user email, user id, account id and the state before) is the only target of
// every read, navigation and write a restore makes. The current run's --flag and --app-user never redirect it: a journal
// left by a run with other arguments restores what that run changed. A journal that cannot be read as a whole target, or a
// row that is not the recorded user's, stops the restore before anything is pressed, and the journal stays. The journal is
// cleared only after a read of the recorded flag, user and account equals the state before.
//
// Normal and signal-triggered restores, and check 8's own write, run one at a time; after an interrupt, check 8 makes no
// further write. Closing a browser context never skips a restore: its failure is reported with the others.
//
// The functions passed in:
// - store: the session store of support/deployed.mjs (readPendingRestore, writePendingRestore, clearPendingRestore,
//   pendingRestoreFile), whose journal file is specific to the back-office host
// - backOfficeHost: the host this run checks
// - current: { flagKey, email }, the flag and target user this run names (--flag and --app-user)
// - fetchUserFlags(userId): the user's flag rows from the back-office API, [{ flagKey, tenantId, isEnabled, source }]
// - openSession(): an admin context on the back office, { newPage(), close() }
// - openRow(page, { flagKey, email, userId, tenantId }): the users list of the flag detail opened on that row,
//   { testId, text, toggle(), removeOverride() }
// - log(message), and now() and sleep(ms) for the polls
// - redact(text), redact of support/stack.mjs unless a test passes another
//
// Every line this module logs, and the message of every error runWrite throws, passes through redact first: a failed request
// of the automation library carries the admin session's cookie header in its message (observed on 2026-10-04 by the G7-03
// review, on a restore read to a host that never answered).

import { redact as redactSensitive } from "./stack.mjs";

export function createOverrideRecovery({
  store,
  backOfficeHost,
  current,
  fetchUserFlags,
  openSession,
  openRow,
  log: print = console.log,
  redact = redactSensitive,
  now = Date.now,
  sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))
}) {
  const log = (message) => print(redact(message));
  let interrupted = false;
  let queue = Promise.resolve();

  // Runs the action after every action queued before it, whether those succeeded or failed
  function serialize(action) {
    const run = queue.then(action);
    queue = run.catch(() => undefined);
    return run;
  }

  // The journal as one immutable target. A journal written before the host was recorded (2026-10-04 and earlier) lives in
  // the journal file of its host, so that file's host is its host.
  function targetOf(journal) {
    const missing = ["flagKey", "email", "userId"].filter((field) => typeof journal?.[field] !== "string" || journal[field] === "");
    if (!["string", "number"].includes(typeof journal?.tenantId) || String(journal.tenantId) === "") missing.push("tenantId");
    if (typeof journal?.before?.isEnabled !== "boolean" || typeof journal?.before?.source !== "string") missing.push("before");
    assert(missing.length === 0, `The pending-restore journal has no usable ${missing.join(", ")}.`);
    const recordedHost = journal.backOfficeHost ?? backOfficeHost;
    assert(recordedHost === backOfficeHost, `The pending-restore journal was recorded for ${recordedHost}, not ${backOfficeHost}.`);
    return Object.freeze({
      backOfficeHost: recordedHost,
      flagKey: journal.flagKey,
      email: journal.email,
      userId: journal.userId,
      tenantId: String(journal.tenantId),
      before: Object.freeze({ isEnabled: journal.before.isEnabled, source: journal.before.source })
    });
  }

  const describe = (target) => `host ${target.backOfficeHost}, flag ${target.flagKey}, user ${target.email} (${target.userId}), account ${target.tenantId}`;

  async function readFlag(target) {
    const entry = (await fetchUserFlags(target.userId)).find((flag) => flag.flagKey === target.flagKey && String(flag.tenantId) === String(target.tenantId));
    assert(entry !== undefined, `The user ${target.userId} has no ${target.flagKey} row for account ${target.tenantId}.`);
    return { isEnabled: entry.isEnabled, source: entry.source };
  }

  async function waitForFlagChange(target, from) {
    const deadline = now() + 30_000;
    while (now() < deadline) {
      const state = await readFlag(target);
      if (!sameFlagState(state, from)) return state;
      await sleep(1_000);
    }
    throw new Error(`The ${target.flagKey} override of ${target.userId} did not change from ${JSON.stringify(from)} within 30 s.`);
  }

  // The row of the target, checked to be that user's row of that account before anything on it is pressed
  async function openTargetRow(page, target) {
    const row = await openRow(page, target);
    const expected = `feature-flag-user-override-${target.userId}-${target.tenantId}`;
    assert(row.testId === expected, `The users list of ${target.flagKey} opened ${row.testId}, not ${expected}; nothing was pressed.`);
    assert(row.text?.includes(target.email), `The row ${expected} does not name ${target.email}; nothing was pressed.`);
    return row;
  }

  // Puts the target's override back to the recorded state through the same UI: a removed override when there was none, the
  // switch otherwise, whose press follows FeatureFlagOverrides.GetSwitchChange. Each step reads the state first and takes
  // the one action that state needs, a step that changes nothing ends the restore, and at most four steps run.
  async function restoreOn(page, target) {
    for (let attempt = 0; attempt < 4; attempt++) {
      const state = await readFlag(target);
      if (sameFlagState(state, target.before)) {
        store.clearPendingRestore();
        return state;
      }
      const row = await openTargetRow(page, target);
      if (target.before.source !== "manual_override" && state.source === "manual_override") await row.removeOverride();
      else await row.toggle();
      await waitForFlagChange(target, state);
    }
    const state = await readFlag(target);
    assert(sameFlagState(state, target.before), `The ${target.flagKey} override of ${target.userId} in account ${target.tenantId} is ${JSON.stringify(state)}, not the recorded ${JSON.stringify(target.before)}.`);
    store.clearPendingRestore();
    return state;
  }

  // One restore in its own admin context; a failure to close that context is reported, and never hides the restore's own
  // failure or undoes a verified restore (the error then carries the restored state)
  async function restore(target) {
    const failures = [];
    let session;
    let restored;
    try {
      session = await openSession();
      restored = await restoreOn(await session.newPage(), target);
    } catch (error) {
      failures.push(error.message);
    }
    if (session !== undefined) {
      try {
        await session.close();
      } catch (error) {
        failures.push(`Closing the restore's admin context failed: ${error.message}`);
      }
    }
    if (failures.length > 0) throw Object.assign(new Error(failures.join(" ")), { restored });
    return restored;
  }

  const recover = (target) => serialize(() => restore(target));

  const restoreFailure = (error) =>
    error.restored !== undefined
      ? `The override was restored to ${JSON.stringify(error.restored)} and verified, but: ${error.message}`
      : `The restore failed: ${error.message} The state before is in ${store.pendingRestoreFile}; the next run restores it first.`;

  // Check 8's body: write(session, write tools) records the target before its write and returns the outcome. Whatever
  // happened in it, its admin context is closed and then the recorded target restored, followed by afterRestore(restored);
  // every failure (the check's, the close's, the restore's, afterRestore's) is reported together.
  async function runWrite({ write, afterRestore = async () => ({}) }) {
    const failures = [];
    let session;
    let target;
    let outcome;
    try {
      session = await openSession();
      outcome = await write(session, {
        readFlag,
        waitForFlagChange,
        record: (recorded, before) => {
          assert(!interrupted, "Interrupted before the write; nothing was written.");
          const journal = { backOfficeHost, flagKey: recorded.flagKey, email: recorded.email, userId: recorded.userId, tenantId: recorded.tenantId, before, recordedAt: new Date().toISOString() };
          target = targetOf(journal);
          store.writePendingRestore(journal);
        },
        mutate: (action) =>
          serialize(async () => {
            assert(!interrupted, "Interrupted: the write was not made.");
            return action();
          })
      });
    } catch (error) {
      failures.push(error.message);
    }
    if (session !== undefined) {
      try {
        await session.close();
      } catch (error) {
        failures.push(`Closing check 8's admin context failed: ${error.message}`);
      }
    }

    if (target !== undefined) {
      let restored;
      try {
        restored = await recover(target);
      } catch (error) {
        failures.push(restoreFailure(error));
      }
      if (restored !== undefined) {
        try {
          const extra = await afterRestore(restored);
          if (outcome !== undefined) Object.assign(outcome, { restored, ...extra });
        } catch (error) {
          failures.push(error.message);
        }
      }
    }
    if (failures.length > 0) throw new Error(redact(failures.join(" ")));
    return outcome;
  }

  // The journal, or "unusable" (reported with its path) when the file is there but cannot be read as JSON; the file stays
  function readJournal(context, outcome = "") {
    try {
      return store.readPendingRestore();
    } catch (error) {
      log(`${context}: the pending-restore file ${store.pendingRestoreFile} cannot be read (${error.message}). Nothing was changed; the file stays.${outcome}`);
      return "unusable";
    }
  }

  // An interrupt: no further write from check 8, and once whatever runs has finished, the journal's target is restored.
  // It never rejects, so the runner still ends with code 130.
  function interrupt() {
    interrupted = true;
    return serialize(async () => {
      const journal = readJournal("Interrupted");
      if (journal === undefined || journal === "unusable") return;
      let target;
      try {
        target = targetOf(journal);
      } catch (error) {
        log(`Interrupted: ${error.message} Nothing was changed; the journal stays in ${store.pendingRestoreFile}.`);
        return;
      }
      log(`Interrupted: restoring ${describe(target)} to ${JSON.stringify(target.before)}...`);
      try {
        log(`Restored: ${JSON.stringify(await restore(target))}.`);
      } catch (error) {
        log(restoreFailure(error));
      }
    });
  }

  // A second interrupt ends the run at once, before the first one's restore has finished: the journal's path is printed when
  // a journal is there, and the next run finds it and offers its restore first
  function interruptedAgain() {
    const journal = readJournal("Interrupted again");
    if (journal !== undefined && journal !== "unusable") {
      log(`Interrupted again: the restore did not finish. The state before is in ${store.pendingRestoreFile}; the next run restores it first.`);
    }
  }

  // A journal an earlier run left, restored before any check once the person running the command agrees:
  // "none", "declined", "restored" or "failed" (reported, and the run ends)
  async function resumeLeftover(ask) {
    const journal = readJournal("An earlier run left a pending restore that cannot be used", " No check ran.");
    if (journal === undefined) return "none";
    if (journal === "unusable") return "failed";
    let target;
    try {
      target = targetOf(journal);
    } catch (error) {
      log(`An earlier run left a pending restore that cannot be used: ${error.message} Nothing was changed. No check ran; the journal stays in ${store.pendingRestoreFile}.`);
      return "failed";
    }
    log(`An earlier run left an override changed. Saved target: ${describe(target)}; the state before it was ${JSON.stringify(target.before)}.`);
    if (current.flagKey !== target.flagKey || current.email !== target.email) {
      log(`This run names --flag ${current.flagKey} and --app-user ${current.email}; the restore acts only on the saved target above.`);
    }
    const answer = await ask("Restore the saved target now, before the checks? [y/N] ");
    if (answer?.toLowerCase() !== "y") {
      log(`No check ran. The state to restore is in ${store.pendingRestoreFile}.`);
      return "declined";
    }
    try {
      log(`Restored: ${JSON.stringify(await recover(target))}.`);
      return "restored";
    } catch (error) {
      log(`${restoreFailure(error)} No check ran.`);
      return "failed";
    }
  }

  return { readFlag, waitForFlagChange, runWrite, interrupt, interruptedAgain, resumeLeftover };
}

export const sameFlagState = (left, right) => left.isEnabled === right.isEnabled && left.source === right.source;

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
