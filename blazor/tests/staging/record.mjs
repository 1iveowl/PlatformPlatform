// The record of a staging acceptance run: every check with its verdict, detail and requests, printed as it ends and written
// to one result file under .workspace/blazor-tests/ through writeResult of support/stack.mjs, which redacts it and fails a
// run that recorded no check. A check that is not run is recorded as not passed, so it can never read as a pass.
//
// The functions passed in, each defaulting to the real one: redact and writeResult of support/stack.mjs, log, and now() for
// the result file's name.

import { redact as redactSensitive, writeResult as writeResultFile } from "../support/stack.mjs";

export function assert(condition, message) {
  if (!condition) throw new Error(message);
}

// run: { browser, browserVersion, tag, resourceGroup, subscription, backOfficeUrl, appUrl }; sessions: the verified sessions
// by identity
export function createRecord({ run, sessions, redact = redactSensitive, writeResult = writeResultFile, log = console.log, now = () => new Date() }) {
  const checks = [];

  async function check(id, name, action) {
    const requests = [];
    try {
      const detail = await action(requests);
      checks.push({ id, name, passed: true, detail, requests });
      log(`PASS ${id} ${name}`);
    } catch (error) {
      checks.push({ id, name, passed: false, detail: redact(error.message), requests });
      log(`FAIL ${id} ${name}: ${redact(error.message)}`);
    }
  }

  function notRun(id, name, reason) {
    checks.push({ id, name, passed: false, notRun: true, detail: reason, requests: [] });
    log(`NOT RUN ${id} ${name}: ${reason}`);
  }

  // A verdict another module reached, recorded as it is (the surfaces)
  function add(entry) {
    checks.push(entry);
  }

  function finish(extra = {}) {
    const hostConfiguration = { hostEnvironment: "Production", buildConfiguration: `staging images ${run.tag}` };
    const verdict = writeResult(
      `staging-acceptance-${run.tag}-${run.browser}-${now().toISOString().replaceAll(":", "").slice(0, 15)}.json`,
      {
        browser: run.browser,
        browserVersion: run.browserVersion,
        culture: "en-US",
        ...hostConfiguration,
        tag: run.tag,
        resourceGroup: run.resourceGroup,
        subscription: run.subscription,
        backOfficeUrl: run.backOfficeUrl,
        appUrl: run.appUrl,
        identities: Object.fromEntries(Object.entries(sessions).map(([identity, session]) => [identity, { backOffice: session.me.email, isAdmin: session.me.isAdmin, appEdition: session.appUser?.email ?? null }])),
        ...extra,
        checks
      },
      1
    );
    log(`Result file: ${verdict.resultFile}`);
    return verdict;
  }

  return { checks, check, notRun, add, finish };
}
