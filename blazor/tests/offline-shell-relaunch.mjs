// The offline shell with the network taken away below the browser, rather than from the browser context.
//
// blazor/tests/e2e/offline-shell-flows.spec.ts exercises the offline navigation on Chromium only, because taking a
// context offline does not take the network away from the service worker in the other two browsers. Measured at
// b5a22f2d7 on 2026-09-21 with probe code inside the worker: in Firefox the worker's own fetch is still answered 200 by
// the network while the document cannot reach it, so the real page is returned and the shell can never appear; in WebKit
// the navigation fails before the worker's fetch handler is dispatched at all. This script closes the Firefox half of
// that gap: it installs the worker in a browser profile of its own, then relaunches the same profile behind a proxy at a
// closed port, so every connection the browser makes is refused, the worker's included, and the stored shell is the only
// thing left that can answer.
//
// 1. A navigation into the authenticated surface is answered by the stored shell, at the address that was asked for, with
//    the shell's heading and its retry link.
// 2. The stored shell is still the only document in the caches after the relaunch, and the assets are still there.
// 3. A public route is not answered by the worker, so it fails rather than showing the shell.
//
// Browsers: Firefox, and Chromium as a control. WebKit cannot run this script, and is not silently left out: a relaunched
// WebKit profile returns with its Cache Storage empty (measured the same day), so the stored shell is gone before the
// navigation and case 1 fails there with that message rather than with a defect of this edition. WebKit's offline shell
// is carried by the device pass on real Safari; docs/blazor-recovery-runbook.md records both.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started in
// Development. Service workers need a secure context, which the gateway's https origin is.
// Run: dotnet run --project developer-cli -- blazor-harness offline-shell-relaunch --browser firefox

import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { baseUrl, completeWelcomeThroughBlazor, gatewayCertificateFingerprint, parseArguments, pathBase, playwright, readOneTimePassword, submitOneTimePasswordThroughBlazor, writeResult } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "firefox" });
const appUrl = `${baseUrl}${pathBase}/app`;
const detailsUrl = `${baseUrl}${pathBase}/app/details`;
const loginUrl = `${baseUrl}${pathBase}/login`;
const offlineShellPath = `${pathBase}/app/offline`;
const interactiveTimeoutMs = 60_000;
const workerTimeoutMs = 30_000;
// Nothing listens here, so every connection the browser makes through it is refused
const unreachableProxy = { server: "http://127.0.0.1:9" };

const stamp = `${options.browser}-${Date.now()}`;
const userDataDir = mkdtempSync(path.join(tmpdir(), `offline-shell-relaunch-${options.browser}-`));
const results = [];
const measurements = {};
const failures = [];
const violations = [];
const expectedCaseCount = 3;

const testId = (id) => `[data-testid="${id}"]`;

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function check(name, action) {
  try {
    const detail = await action();
    results.push({ name, passed: true, detail });
    console.log(`PASS ${name}${detail ? `: ${JSON.stringify(detail)}` : ""}`);
  } catch (error) {
    results.push({ name, passed: false, detail: error.message });
    console.log(`FAIL ${name}: ${error.message}`);
  }
}

// The same allowance stack.mjs makes for Chromium: a service worker script is fetched outside the context, so the
// context's own certificate option does not cover it
async function launch(extra) {
  const certificate = options.browser === "chromium" ? { args: [`--ignore-certificate-errors-spki-list=${await gatewayCertificateFingerprint()}`] } : {};
  const context = await playwright[options.browser].launchPersistentContext(userDataDir, { ignoreHTTPSErrors: true, locale: "en-US", ...certificate, ...extra });
  await context.exposeBinding("__reportPolicyViolation", (_source, violation) => violations.push(violation));
  await context.addInitScript(() => {
    document.addEventListener("securitypolicyviolation", (event) => {
      window.__reportPolicyViolation?.({ effectiveDirective: event.effectiveDirective, blockedURI: event.blockedURI });
    });
  });
  return context;
}

function readCaches(page) {
  return page.evaluate(async () => {
    const entries = {};
    for (const name of await caches.keys()) {
      const cache = await caches.open(name);
      entries[name] = (await cache.keys()).map((request) => new URL(request.url).pathname).sort();
    }
    return entries;
  });
}

// A profile with the worker installed, the shell stored and the assets stored, signed up through the browser
const installing = await launch({});
try {
  const page = installing.pages()[0] ?? (await installing.newPage());
  const email = `offline-relaunch-${stamp}@example.com`;
  await page.goto(`${baseUrl}${pathBase}/signup`, { waitUntil: "load" });
  await page.locator(testId("email")).fill(email);
  const sentAfter = Date.now();
  await page.locator(testId("submit")).click();
  await page.waitForURL(/\/blazor\/signup\/verify\?/);
  await submitOneTimePasswordThroughBlazor(page, await readOneTimePassword(email, sentAfter));
  await completeWelcomeThroughBlazor(page);

  await page.goto(appUrl, { waitUntil: "load" });
  await page.locator(testId("app-shell")).waitFor({ timeout: interactiveTimeoutMs });
  await page.evaluate(async (timeout) => {
    await navigator.serviceWorker.ready;
    const deadline = Date.now() + timeout;
    while (navigator.serviceWorker.controller === null && Date.now() < deadline) await new Promise((resolve) => setTimeout(resolve, 100));
  }, workerTimeoutMs);
  // The second load runs with the worker in control, which is when the document's own assets are stored
  await page.reload({ waitUntil: "load" });
  await page.locator(testId("app-shell")).waitFor({ timeout: interactiveTimeoutMs });
  await page.waitForFunction(
    async (shell) => (await Promise.all((await caches.keys()).map(async (name) => (await (await caches.open(name)).keys()).map((request) => new URL(request.url).pathname)))).flat().includes(shell),
    offlineShellPath,
    { timeout: workerTimeoutMs }
  );
  measurements.storedBeforeTheRelaunch = await readCaches(page);
} finally {
  await installing.close();
}

const relaunched = await launch({ proxy: unreachableProxy });
try {
  const page = relaunched.pages()[0] ?? (await relaunched.newPage());

  await check("a navigation into the authenticated surface is answered by the stored shell at the same address", async () => {
    await page.goto(detailsUrl, { waitUntil: "load" });
    await page.locator(testId("offline-page")).waitFor({ timeout: workerTimeoutMs });

    assert(page.url() === detailsUrl, `The address became ${page.url()}.`);
    const heading = (await page.locator("h1").textContent()).trim();
    assert(heading === "You are offline", `The shell shows ${heading}.`);
    assert((await page.locator(testId("offline-retry")).count()) === 1, "The shell offers no retry link.");
    assert((await page.locator(testId("app-shell")).count()) === 0, "The authenticated surface answered instead of the shell.");
    measurements.shell = { url: page.url(), heading };
    return measurements.shell;
  });

  await check("the stored shell is still the only document in the caches after the relaunch", async () => {
    const entries = await readCaches(page);
    measurements.storedAfterTheRelaunch = entries;

    const stored = Object.values(entries).flat();
    assert(stored.includes(offlineShellPath), `The relaunched profile holds no stored shell. Stored: ${stored.length} entries.`);
    assert(stored.length > 1, "The relaunched profile kept no asset beside the shell document.");
    const documents = stored.filter((storedPath) => !storedPath.slice(storedPath.lastIndexOf("/")).includes(".") && storedPath !== offlineShellPath);
    assert(documents.length === 0, `Stored documents beside the shell: ${documents.join(", ")}.`);
    return { caches: Object.keys(entries), stored: stored.length };
  });

  await check("a public route is not answered by the worker and fails with the network unreachable", async () => {
    const publicPage = await relaunched.newPage();
    try {
      const outcome = await publicPage.goto(loginUrl, { waitUntil: "load" }).then(() => "loaded", (error) => error.message.split("\n")[0]);

      assert(outcome !== "loaded", "The login page loaded with the network unreachable, so something answered it from a cache.");
      measurements.publicRoute = outcome.slice(0, 120);
      return { publicRoute: measurements.publicRoute };
    } finally {
      await publicPage.close();
    }
  });
} finally {
  if (violations.length > 0) failures.push(`${violations.length} content security policy violations`);
  measurements.policyViolations = violations.length;
  await relaunched.close();
  rmSync(userDataDir, { recursive: true, force: true });
}

const { resultFile, passed } = writeResult(
  `offline-shell-relaunch-${options.browser}.json`,
  { browser: options.browser, baseUrl, startedAt: new Date().toISOString(), results, measurements, failures },
  expectedCaseCount
);

console.log(`${passed ? "PASSED" : "FAILED"} ${results.filter((entry) => entry.passed).length} of ${results.length} cases, result ${resultFile}`);
process.exit(passed ? 0 : 1);
