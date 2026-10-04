// Deterministic tests of the parts the staging acceptance runner (staging-acceptance.mjs) is built from: the record and its
// result file (staging/record.mjs), the probe job's approval, run and cleanup (staging/probe-job.mjs), the session
// requirement and the context lifecycle (staging/browser-sessions.mjs) and the image and fingerprint gate
// (staging/deployment.mjs). No browser, stack, Azure or staging: the az CLI, the contexts, the requests and the terminal are
// fakes. The admin write's recovery has its own tests in staging-recovery.mjs.
//
//   dotnet run --project developer-cli -- blazor-harness staging-runner
//
// The harness passes --browser, which these tests ignore. Exit code 1 when a test fails.

import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { test } from "node:test";
import { createBrowserSessions, requireSessions } from "./staging/browser-sessions.mjs";
import { checkImageGate, readDeployment, referencedAssets } from "./staging/deployment.mjs";
import { askProbeApproval, probeChecks, probeJobName, runProbeJob } from "./staging/probe-job.mjs";
import { createRecord } from "./staging/record.mjs";

// ---------------------------------------------------------------------------------------------------------------------
// The record

const run = { browser: "chromium", browserVersion: "140.0", tag: "2026.10.04.1200", resourceGroup: "rg-staging", subscription: "Staging", backOfficeUrl: "https://back-office.staging.test", appUrl: "https://staging.test" };
const sessions = {
  admin: { me: { email: "owner@etara.dk", isAdmin: true }, appUser: { email: "owner@etara.dk" } },
  "non-admin": { me: { email: "outside@example.com", isAdmin: false } }
};

function createTestRecord() {
  const written = [];
  const log = [];
  const record = createRecord({
    run,
    sessions,
    redact: (text) => String(text).replaceAll("cookie-value", "[redacted]"),
    writeResult: (fileName, result, expectedCaseCount) => {
      written.push({ fileName, result, expectedCaseCount });
      return { resultFile: `/results/${fileName}`, passed: result.checks.every((entry) => entry.passed), failures: [] };
    },
    log: (message) => log.push(message),
    now: () => new Date("2026-10-04T14:02:33.123Z")
  });
  return { record, written, log };
}

test("A check records its verdict, detail and requests, and a failure is redacted and never passes", async () => {
  const { record, log } = createTestRecord();

  await record.check("1", "Passes", async (requests) => {
    requests.push({ method: "GET", url: "https://back-office.staging.test/api/back-office/me", status: 200 });
    return { me: "owner@etara.dk" };
  });
  await record.check("2", "Fails", async (requests) => {
    requests.push({ method: "GET", status: 500 });
    throw new Error("The cookie cookie-value was refused.");
  });
  record.notRun("3b", "Probe", "the owner did not approve the probe job for this run");
  record.add({ id: "surfaces", name: "users at phone width", passed: true, detail: {}, requests: [] });

  assert.deepEqual(record.checks, [
    { id: "1", name: "Passes", passed: true, detail: { me: "owner@etara.dk" }, requests: [{ method: "GET", url: "https://back-office.staging.test/api/back-office/me", status: 200 }] },
    { id: "2", name: "Fails", passed: false, detail: "The cookie [redacted] was refused.", requests: [{ method: "GET", status: 500 }] },
    { id: "3b", name: "Probe", passed: false, notRun: true, detail: "the owner did not approve the probe job for this run", requests: [] },
    { id: "surfaces", name: "users at phone width", passed: true, detail: {}, requests: [] }
  ]);
  assert.deepEqual(log, ["PASS 1 Passes", "FAIL 2 Fails: The cookie [redacted] was refused.", "NOT RUN 3b Probe: the owner did not approve the probe job for this run"]);
});

test("The result file keeps its name, its fields in order and at least one expected check", async () => {
  const { record, written, log } = createTestRecord();
  await record.check("gate", "Gate", async () => ({}));

  const verdict = record.finish({ viewports: ["desktop"], surfaceIds: { tenantId: "1" } });

  assert.equal(written.length, 1);
  assert.equal(written[0].fileName, "staging-acceptance-2026.10.04.1200-chromium-2026-10-04T1402.json");
  assert.equal(written[0].expectedCaseCount, 1);
  assert.deepEqual(Object.keys(written[0].result), ["browser", "browserVersion", "culture", "hostEnvironment", "buildConfiguration", "tag", "resourceGroup", "subscription", "backOfficeUrl", "appUrl", "identities", "viewports", "surfaceIds", "checks"]);
  assert.equal(written[0].result.hostEnvironment, "Production");
  assert.equal(written[0].result.buildConfiguration, "staging images 2026.10.04.1200");
  assert.deepEqual(written[0].result.identities, {
    admin: { backOffice: "owner@etara.dk", isAdmin: true, appEdition: "owner@etara.dk" },
    "non-admin": { backOffice: "outside@example.com", isAdmin: false, appEdition: null }
  });
  assert.equal(written[0].result.checks, record.checks);
  assert.equal(verdict.passed, true);
  assert.equal(log.at(-1), "Result file: /results/staging-acceptance-2026.10.04.1200-chromium-2026-10-04T1402.json");
});

// ---------------------------------------------------------------------------------------------------------------------
// The probe job

const placeholders = {
  LOCATION: "northeurope",
  ENVIRONMENT_ID: "/subscriptions/0/resourceGroups/rg-staging/providers/Microsoft.App/managedEnvironments/env",
  ENVIRONMENT_DOMAIN: "env.northeurope.azurecontainerapps.io",
  BACK_OFFICE_HOST: "back-office.staging.test",
  BACK_OFFICE_FQDN: "back-office.env.northeurope.azurecontainerapps.io",
  FORGED_NAME: "forged-admin@example.com",
  FORGED_ID: "00000000-0000-4000-8000-0000000000f0",
  FORGED_PRINCIPAL: "eyJmb3JnZWQiOnRydWV9"
};
const goodLog = ["x PROBE 3b 404 http://account-api/ a text/html", "x PROBE 3b 401 http://account-api/ b application/json", "x PROBE 3c 302 https://bo/ c text/html", "x PROBE 3c 401 https://bo/ d application/json", "x PROBE 3c 401 https://bo/ e application/json", "x PROBE done"].join("\n");

// The az CLI as the probe job sees it: every call recorded, the yaml file read when the job is created
function createFakeAzure({ createFails = false, deleteFails = false, statuses = ["Running", "Succeeded"], log = goodLog } = {}) {
  const calls = [];
  const azure = {
    yaml: undefined,
    yamlFile: undefined,
    calls,
    raw(argumentList) {
      calls.push(argumentList.slice(0, 3).join(" "));
      if (argumentList[2] === "create") {
        if (createFails) throw new Error("az containerapp job create failed: quota");
        azure.yamlFile = argumentList[argumentList.indexOf("--yaml") + 1];
        azure.yaml = readFileSync(azure.yamlFile, "utf8");
        return "";
      }
      if (argumentList[2] === "delete") {
        if (deleteFails) throw new Error("az containerapp job delete failed: conflict");
        return "";
      }
      if (argumentList[2] === "logs") return log;
      throw new Error(`Unexpected az ${argumentList.join(" ")}`);
    },
    json(argumentList) {
      calls.push(argumentList.slice(0, argumentList[2] === "execution" ? 4 : 3).join(" "));
      if (argumentList[2] === "start") return { id: "/subscriptions/0/jobs/bo-probe-2610041402/executions/bo-probe-2610041402-abc" };
      if (argumentList[2] === "execution") return { properties: { status: statuses.length > 1 ? statuses.shift() : statuses[0] } };
      throw new Error(`Unexpected az ${argumentList.join(" ")}`);
    }
  };
  return azure;
}

function runFakeProbe(azure) {
  let clock = 0;
  const log = [];
  const result = runProbeJob({
    azure,
    resourceGroup: "rg-staging",
    jobName: "bo-probe-2610041402",
    placeholders,
    log: (message) => log.push(message),
    now: () => clock,
    sleep: async (ms) => {
      clock += ms;
    }
  });
  return { result, log };
}

test("The probe job's name is bo-probe and ten digits of the time", () => {
  assert.equal(probeJobName(new Date("2026-10-04T14:02:33.123Z")), "bo-probe-2610041402");
});

test("The probe job runs only when the owner types its name, and is otherwise recorded as not run with the reason", async () => {
  const questions = [];
  const log = [];
  const asking = (answer) => async (question) => {
    questions.push(question);
    return answer;
  };
  const describe = { jobName: "bo-probe-2610041402", resourceGroup: "rg-staging", environmentName: "env", log: (message) => log.push(message) };

  assert.deepEqual(await askProbeApproval({ ...describe, ask: asking("bo-probe-2610041402") }), { approved: true });
  assert.deepEqual(await askProbeApproval({ ...describe, ask: asking("y") }), { approved: false, reason: "the owner did not approve the probe job for this run" });
  assert.deepEqual(await askProbeApproval({ ...describe, ask: asking("") }), { approved: false, reason: "the owner did not approve the probe job for this run" });
  assert.deepEqual(await askProbeApproval({ ...describe, ask: asking(null) }), { approved: false, reason: "no terminal to ask for the probe job's approval" });

  assert.equal(questions[0], "Type the job name (bo-probe-2610041402) to approve this run, or press Enter to skip: ");
  assert.match(log[0], /create {2}Container Apps job bo-probe-2610041402 in rg-staging, environment env/);
  assert.deepEqual(
    probeChecks.map((probe) => probe.id),
    ["3b", "3c"]
  );
});

test("An approved probe job is created from the filled template, run once, read back and deleted", async () => {
  const azure = createFakeAzure();
  const { result, log } = runFakeProbe(azure);

  const detail = await result;

  assert.equal(detail.job, "bo-probe-2610041402");
  assert.equal(detail.execution, "bo-probe-2610041402-abc");
  assert.equal(detail.answers.length, 5);
  assert.deepEqual(detail.answers[0], { check: "3b", status: 404, url: "http://account-api/", host: "a", accept: "text/html" });
  for (const [name, value] of Object.entries(placeholders)) {
    assert.ok(!azure.yaml.includes(`{{${name}}}`), `{{${name}}} is left in the job definition`);
    assert.ok(azure.yaml.includes(value), `${name} is not in the job definition`);
  }
  assert.deepEqual(azure.calls, [
    "containerapp job create",
    "containerapp job start",
    "containerapp job execution show",
    "containerapp job execution show",
    "containerapp job logs",
    "containerapp job delete"
  ]);
  assert.ok(!existsSync(path.dirname(azure.yamlFile)), "The filled job definition is left on disk");
  assert.deepEqual(log, ["Deleted the probe job bo-probe-2610041402."]);
});

test("A probe execution that fails, never ends or answers wrongly fails the check, and the job is still deleted", async () => {
  const cases = [
    { azure: createFakeAzure({ statuses: ["Failed"] }), error: /The probe execution bo-probe-2610041402-abc ended Failed\./ },
    { azure: createFakeAzure({ statuses: ["Running"] }), error: /The probe execution bo-probe-2610041402-abc ended Running\./ },
    { azure: createFakeAzure({ log: goodLog.replace(" PROBE done", " nothing") }), error: /The probe log has no end marker/ },
    { azure: createFakeAzure({ log: goodLog.replace("3b 404", "3b 200") }), error: /Answers: / },
    { azure: createFakeAzure({ log: goodLog.replace("x PROBE 3c 401 https://bo/ e application/json\n", "") }), error: /Answers: / }
  ];
  for (const { azure, error } of cases) {
    await assert.rejects(runFakeProbe(azure).result, error);
    assert.equal(azure.calls.at(-1), "containerapp job delete");
    assert.ok(!existsSync(path.dirname(azure.yamlFile)));
  }
});

test("A job that was never created is not deleted, and a job that cannot be deleted fails the check and names it", async () => {
  const notCreated = createFakeAzure({ createFails: true });
  await assert.rejects(runFakeProbe(notCreated).result, /az containerapp job create failed: quota/);
  assert.deepEqual(notCreated.calls, ["containerapp job create"]);

  const notDeleted = createFakeAzure({ deleteFails: true });
  await assert.rejects(runFakeProbe(notDeleted).result, /The probe job bo-probe-2610041402 could not be deleted; delete it by hand: az containerapp job delete failed: conflict/);
});

// ---------------------------------------------------------------------------------------------------------------------
// Sessions and contexts

const target = { backOfficeUrl: "https://back-office.staging.test", appUrl: "https://staging.test", appUserEmail: "owner@etara.dk" };

function sessionFakes(verdicts, answer) {
  const calls = [];
  return {
    calls,
    verifyStoredSession: async (store, identity) => {
      calls.push(`verify ${identity}`);
      return verdicts[identity].shift();
    },
    captureSession: async (store, identity, captureTarget) => calls.push(`capture ${identity} ${captureTarget.desktopPort}`),
    ask: async (question) => {
      calls.push(`ask ${question}`);
      return answer;
    },
    log: (message) => calls.push(`log ${message}`)
  };
}

const valid = (email) => ({ valid: true, me: { email } });
const expired = { valid: false, reason: "the session has expired" };

test("Every identity's stored session is verified before any check, and an expired one is offered a sign-in", async () => {
  const signedIn = sessionFakes({ admin: [valid("owner@etara.dk")], "non-admin": [expired, valid("outside@example.com")] }, "y");
  const result = await requireSessions({ store: {}, target, desktopPort: 6080, ...signedIn });

  assert.deepEqual(Object.keys(result.sessions), ["admin", "non-admin"]);
  assert.equal(result.missing, undefined);
  assert.deepEqual(signedIn.calls, [
    "verify admin",
    "log Session admin: owner@etara.dk.",
    "verify non-admin",
    "log The non-admin session cannot be used: the session has expired.",
    "ask Sign in as non-admin now? [y/N] ",
    "capture non-admin 6080",
    "verify non-admin",
    "log Session non-admin: outside@example.com."
  ]);
});

test("A session that stays unusable stops before any other identity and names the identity, never failing a check", async () => {
  for (const answer of [null, "", "n"]) {
    const declined = sessionFakes({ admin: [expired], "non-admin": [valid("outside@example.com")] }, answer);
    const result = await requireSessions({ store: {}, target, desktopPort: 6080, ...declined });
    assert.deepEqual(result, { missing: "admin" });
    assert.ok(!declined.calls.some((call) => call.startsWith("capture") || call === "verify non-admin"));
  }

  const stillExpired = sessionFakes({ admin: [expired, expired], "non-admin": [] }, "Y");
  assert.deepEqual(await requireSessions({ store: {}, target, desktopPort: 6080, ...stillExpired }), { missing: "admin" });
});

const backOfficeCookie = (value) => ({ name: "back-office", value, domain: "back-office.staging.test" });
const appCookie = (value) => ({ name: "__Host-refresh-token", value, domain: "staging.test" });

function createContextFakes(stored) {
  const store = {
    states: { ...stored },
    load: (identity) => store.states[identity],
    save: (identity, state) => {
      store.states[identity] = state;
    }
  };
  const steps = [];
  const fakeContext = (endState) => {
    const handlers = [];
    return {
      on: (event, handler) => event === "request" && handlers.push(handler),
      visit: (url) => handlers.forEach((handler) => handler({ url: () => url })),
      storageState: async () => {
        steps.push("storageState");
        return endState;
      },
      close: async () => steps.push("close")
    };
  };
  return { store, steps, fakeContext };
}

test("A context that never visited the app host keeps the stored app-edition cookies, and one that did stores its own", async () => {
  const stored = { cookies: [backOfficeCookie("old"), appCookie("stored-refresh")] };
  const endState = { cookies: [backOfficeCookie("new"), appCookie("stale-copy")], origins: [] };

  for (const [identity, visits, expected, reconciled] of [
    ["admin", [], [appCookie("stored-refresh"), backOfficeCookie("new")], false],
    ["admin", ["https://staging.test/blazor/"], endState.cookies, true],
    ["non-admin", ["https://staging.test/blazor/"], endState.cookies, false]
  ]) {
    const { store, steps, fakeContext } = createContextFakes({ [identity]: stored });
    const context = fakeContext(endState);
    const browserSessions = createBrowserSessions({
      browser: {},
      browserName: "chromium",
      store,
      appUrl: "https://staging.test",
      newContext: async (browser, browserName, storageState, locale, contextOptions) => {
        assert.equal(storageState, stored);
        assert.equal(locale, "en-US");
        assert.equal(contextOptions.ignoreHTTPSErrors, false);
        return context;
      },
      settleAndClosePages: async () => steps.push("settle"),
      reconcileAppSession: async () => steps.push("reconcile")
    });

    const { close } = await browserSessions.openContext(identity);
    for (const url of visits) context.visit(url);
    await close();

    assert.deepEqual(store.states[identity].cookies, expected);
    assert.deepEqual(steps, reconciled ? ["settle", "reconcile", "storageState", "close"] : ["settle", "storageState", "close"]);
  }
});

test("A context or request of nobody stores nothing, and a request stores the identity's cookies it ended with", async () => {
  const { store, steps, fakeContext } = createContextFakes({ admin: { cookies: [backOfficeCookie("old")] } });
  const disposed = [];
  const recorded = [];
  const browserSessions = createBrowserSessions({
    browser: {},
    browserName: "chromium",
    store,
    appUrl: "https://staging.test",
    newContext: async (browser, browserName, storageState) => {
      assert.equal(storageState, undefined);
      return fakeContext({ cookies: [] });
    },
    settleAndClosePages: async () => steps.push("settle"),
    recordResponse: async (method, url, response, headers) => {
      recorded.push({ method, url, headers });
      return { method, url, status: response.status() };
    },
    requestContext: async (storageState) => ({
      fetch: async (url, requestOptions) => {
        assert.deepEqual({ maxRedirects: requestOptions.maxRedirects, failOnStatusCode: requestOptions.failOnStatusCode }, { maxRedirects: 0, failOnStatusCode: false });
        return { status: () => 200, headers: () => ({ "content-type": "text/html" }), text: async () => `body for ${storageState ? "admin" : "nobody"}` };
      },
      storageState: async () => ({ cookies: [backOfficeCookie("rotated")] }),
      dispose: async () => disposed.push(storageState ? "admin" : "nobody")
    })
  });

  const { close } = await browserSessions.openContext(null);
  await close();
  assert.deepEqual(steps, ["settle", "close"]);

  const anonymous = await browserSessions.send(null, "GET", "https://staging.test/blazor/", { headers: { Accept: "text/html" } });
  assert.equal(anonymous.body, "body for nobody");
  assert.deepEqual(store.states.admin.cookies, [backOfficeCookie("old")]);

  const asAdmin = await browserSessions.send("admin", "GET", "https://back-office.staging.test/api/back-office/me");
  assert.deepEqual(asAdmin.recorded, { method: "GET", url: "https://back-office.staging.test/api/back-office/me", status: 200 });
  assert.deepEqual(store.states.admin.cookies, [backOfficeCookie("rotated")]);
  assert.deepEqual(disposed, ["nobody", "admin"]);
  assert.deepEqual(recorded[0].headers, { Accept: "text/html" });
});

// ---------------------------------------------------------------------------------------------------------------------
// The deployment and the image gate

const tag = "2026.10.04.1200";
const imageOf = (repository, imageTag = tag) => `registry.azurecr.io/${repository}:${imageTag}`;
const routes = new Set(["app.abc123.css", "_framework/blazor.web.def456.js", "_framework/dotnet.js"]);
const goodDocument = '<link href="/blazor/app.abc123.css"><script src="/blazor/_framework/blazor.web.def456.js"></script><link href="brand.css">';

function gateAzure(images) {
  return {
    json: (argumentList) => {
      const name = argumentList[argumentList.indexOf("--name") + 1];
      return [{ name: `${name}--1`, properties: { active: true, template: { containers: [{ image: images[name] }] }, trafficWeight: 100, healthState: "Healthy", runningState: "Running" } }];
    }
  };
}

const allOnTag = { "account-api": imageOf("account-api"), "back-office": imageOf("account-api"), "account-workers": imageOf("account-workers"), "blazor-host": imageOf("blazor-host") };

async function runGate(images, documents) {
  const readImages = [];
  const requests = [];
  const promise = checkImageGate({
    azure: gateAzure(images),
    resourceGroup: "rg-staging",
    tag,
    appUrl: "https://staging.test",
    backOfficeUrl: "https://back-office.staging.test",
    send: async (identity, method, url) => ({ status: 200, body: documents[identity ?? "nobody"], recorded: { identity, url } }),
    requests,
    readImageManifest: (image) => {
      readImages.push(image);
      return routes;
    }
  });
  return { promise, readImages, requests };
}

test("The gate passes when every app runs the tag and both documents reference only that image's assets", async () => {
  const { promise, readImages, requests } = await runGate(allOnTag, { nobody: goodDocument, admin: goodDocument });
  const detail = await promise;

  assert.deepEqual(readImages, [imageOf("blazor-host")]);
  assert.deepEqual(
    requests.map((request) => request.url),
    ["https://staging.test/blazor/", "https://back-office.staging.test/blazor/back-office"]
  );
  assert.equal(detail.appCss, "app.abc123.css");
  assert.deepEqual(detail.documents["app host"].notInImage, []);
});

test("The gate fails on another tag before reading the image, and on a document referencing an asset not in the image", async () => {
  const otherTag = await runGate({ ...allOnTag, "back-office": imageOf("account-api", "2026.09.30.1815") }, { nobody: goodDocument, admin: goodDocument });
  await assert.rejects(otherTag.promise, /back-office revision back-office--1 runs registry\.azurecr\.io\/account-api:2026\.09\.30\.1815, not account-api:2026\.10\.04\.1200/);
  assert.deepEqual(otherTag.readImages, []);

  const foreign = await runGate(allOnTag, { nobody: goodDocument, admin: `${goodDocument}<script src="/blazor/_framework/blazor.web.old999.js"></script>` });
  await assert.rejects(foreign.promise, /back-office host: .* references _framework\/blazor\.web\.old999\.js, which the image .* does not contain/);
});

test("Referenced assets are read from attributes below the path base, and a back office without a custom domain stops", () => {
  assert.deepEqual(referencedAssets(goodDocument), ["app.abc123.css", "_framework/blazor.web.def456.js", "brand.css"]);
  const azure = { json: () => ({ properties: { template: { containers: [{ env: [] }] }, configuration: { ingress: {} } } }) };
  assert.throws(() => readDeployment(azure, "rg-staging"), /The back-office app has no custom domain\./);
});
