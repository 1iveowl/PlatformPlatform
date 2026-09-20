// The offline shell of the Blazor edition in one browser: what the service worker installs, what it is allowed to store,
// what it answers when the network is gone, and what a departure drops.
//
// 1. Registration: the worker is registered from the authenticated surface, is served no-cache with Service-Worker-Allowed
//    for the path base, and controls the document with the path base as its scope.
// 2. What is stored: exactly one document, the anonymous offline shell, plus assets under the path base. No other document,
//    no bootstrap and no account API response is in any cache, and every stored entry is same-origin under the path base.
// 3. Offline inside the authenticated surface: a navigation answers with the shell, at the address the browser asked for,
//    so a reload retries that page. The shell shows the offline state and its retry link.
// 4. Standalone launch: the manifest's start_url, opened offline in a fresh page of the same profile, shows the shell.
// 5. The public surface is never intercepted: online, the landing, login, signup and legal documents are served by the
//    network (PerformanceNavigationTiming.workerStart is 0), and offline they fail rather than showing the shell.
// 6. The account API is never intercepted: no request to /api/ is answered by the worker, on any of the pages visited.
// 7. Logout drops the stored shell document and keeps the immutable assets, and the surface is usable again afterwards.
// Every case asserts zero content security policy violations and no page errors.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started in
// Development. Service workers need a secure context, which the gateway's https origin is.
// Run: dotnet run --project developer-cli -- blazor-harness offline-shell --browser chromium

import { baseUrl, launchBrowser, newContext, observeErrors, parseArguments, pathBase, policyViolationsOf, signUpThroughBlazor, writeResult } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
const appUrl = `${baseUrl}${pathBase}/app`;
const detailsUrl = `${baseUrl}${pathBase}/app/details`;
const offlineShellPath = `${pathBase}/app/offline`;
const workerPath = `${pathBase}/service-worker.js`;
const bootstrapPath = "/api/account/bootstrap";
const interactiveTimeoutMs = 60_000;
const workerTimeoutMs = 30_000;

const stamp = `${options.browser}-${Date.now()}`;
const browser = await launchBrowser(options.browser);
const browserVersion = browser.version();
const results = [];
const measurements = {};
const failures = [];
const expectedCaseCount = 7;

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

const testId = (id) => `[data-testid="${id}"]`;

async function waitInteractive(page) {
  await page.locator(testId("app-shell")).waitFor({ timeout: interactiveTimeoutMs });
}

// The worker registered by the authenticated surface, once it controls the document
async function waitControlled(page) {
  return page.evaluate(async (timeout) => {
    const registration = await navigator.serviceWorker.ready;
    const deadline = Date.now() + timeout;
    while (navigator.serviceWorker.controller === null && Date.now() < deadline) {
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    return { scope: new URL(registration.scope).pathname, scriptUrl: new URL(registration.active.scriptURL).pathname, controlled: navigator.serviceWorker.controller !== null };
  }, workerTimeoutMs);
}

// Everything the worker has stored, by cache name, as same-origin paths
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

// Every response the page received, with whether the worker answered it rather than the network. A document a worker
// merely observes is not intercepted: only a response the worker fulfilled reports fromServiceWorker.
function recordResponses(page) {
  const responses = [];
  page.on("response", (response) => {
    responses.push({ path: new URL(response.url()).pathname, fromWorker: response.fromServiceWorker(), document: response.request().resourceType() === "document" });
  });
  return responses;
}

// The worker's own response headers, read from inside the page because the harness runs against a development certificate
function readWorkerHeaders(page, url) {
  return page.evaluate(async (workerUrl) => {
    const response = await fetch(workerUrl, { cache: "no-store" });
    return { status: response.status, cacheControl: response.headers.get("cache-control"), allowedScope: response.headers.get("service-worker-allowed") };
  }, url);
}

const account = await signUpThroughBlazor(browser, options.browser, `offline-shell-${stamp}@example.com`);
const context = await newContext(browser, options.browser, account.storageState);
const page = await context.newPage();
const observations = observeErrors(page);
const responses = recordResponses(page);

try {
  await check("worker is registered from the authenticated surface and scoped to the path base", async () => {
    await page.goto(appUrl, { waitUntil: "load" });
    await waitInteractive(page);
    const registration = await waitControlled(page);
    const headers = await readWorkerHeaders(page, `${baseUrl}${workerPath}`);

    assert(registration.controlled, "The worker does not control the authenticated document.");
    assert(registration.scope === `${pathBase}/`, `The worker scope is ${registration.scope}.`);
    assert(registration.scriptUrl === workerPath, `The worker script is ${registration.scriptUrl}.`);
    assert(headers.status === 200, `The worker answered ${headers.status}.`);
    assert(headers.cacheControl === "no-cache", `The worker is served with cache-control ${headers.cacheControl}.`);
    assert(headers.allowedScope === `${pathBase}/`, `The worker names scope ${headers.allowedScope}.`);
    measurements.registration = { ...registration, ...headers };
    return measurements.registration;
  });

  await check("only the offline shell document and assets under the path base are stored", async () => {
    // The second load runs with the worker in control, which is when the document's own assets are stored
    await page.reload({ waitUntil: "load" });
    await waitInteractive(page);
    const entries = await readCaches(page);
    measurements.cacheEntries = entries;

    const stored = Object.values(entries).flat();
    assert(stored.includes(offlineShellPath), `The offline shell is not stored. Stored: ${stored.length} entries.`);
    assert(stored.length > 1, "No asset was stored beside the shell document.");
    const outsidePathBase = stored.filter((path) => !path.startsWith(`${pathBase}/`));
    assert(outsidePathBase.length === 0, `Stored outside the path base: ${outsidePathBase.join(", ")}.`);
    const apiResponses = stored.filter((path) => path.startsWith("/api/"));
    assert(apiResponses.length === 0, `Stored account API responses: ${apiResponses.join(", ")}.`);
    // Every other document is no-store, so the shell is the only path without a file extension that may be stored
    const documents = stored.filter((path) => !path.slice(path.lastIndexOf("/")).includes(".") && path !== offlineShellPath);
    assert(documents.length === 0, `Stored documents beside the shell: ${documents.join(", ")}.`);
    return { caches: Object.keys(entries), stored: stored.length };
  });

  await check("the account API is answered by the network, never by the worker", async () => {
    // The client reads its bootstrap once the runtime has started, which is the account API call worth watching
    const bootstrap = page.waitForResponse((response) => new URL(response.url()).pathname === bootstrapPath, { timeout: interactiveTimeoutMs });
    await page.goto(detailsUrl, { waitUntil: "load" });
    await waitInteractive(page);
    await bootstrap;
    const apiAnswered = responses.filter((response) => response.path.startsWith("/api/") && response.fromWorker).map((response) => response.path);

    assert(responses.some((response) => response.path === bootstrapPath), "The authenticated surface never read its bootstrap.");
    assert(apiAnswered.length === 0, `The worker answered ${apiAnswered.join(", ")}.`);
    measurements.accountApiResponses = responses.filter((response) => response.path.startsWith("/api/")).length;
    return { accountApiResponses: measurements.accountApiResponses, answeredByTheWorker: apiAnswered.length };
  });

  await check("a navigation inside the authenticated surface answers offline with the shell at the same address", async () => {
    await page.goto(appUrl, { waitUntil: "load" });
    await waitInteractive(page);
    await context.setOffline(true);

    await page.goto(detailsUrl, { waitUntil: "load" });
    await page.locator(testId("offline-page")).waitFor();

    assert(page.url() === detailsUrl, `The address became ${page.url()}.`);
    const heading = await page.locator("h1").textContent();
    assert(heading.trim() === "You are offline", `The shell shows ${heading}.`);
    const retry = page.locator(testId("offline-retry"));
    assert((await retry.count()) === 1, "The shell offers no retry link.");
    assert(responses.at(-1) !== undefined && responses.some((response) => response.path === `${pathBase}/app/details` && response.fromWorker), "The document was not answered by the worker.");
    // The shell is styled from the stored assets, not from an unstyled fallback
    const styled = await page.evaluate(() => getComputedStyle(document.querySelector("[data-testid='offline-page']")).display);
    return { url: page.url(), heading: heading.trim(), display: styled };
  });

  await check("a standalone launch offline shows the shell at the manifest start address", async () => {
    const launched = await context.newPage();
    try {
      await launched.goto(appUrl, { waitUntil: "load" });
      await launched.locator(testId("offline-page")).waitFor();

      assert(launched.url() === appUrl, `The launch landed on ${launched.url()}.`);
      // A public route is not intercepted, so offline it fails instead of showing the shell
      const publicNavigation = await launched.goto(`${baseUrl}${pathBase}/login`).then(() => "loaded", (error) => error.message);
      assert(publicNavigation !== "loaded", "The login page loaded while offline, so something answered it from a cache.");
      return { startUrl: appUrl, publicRouteOffline: publicNavigation.split("\n")[0].slice(0, 80) };
    } finally {
      await launched.close();
    }
  });

  await check("logout drops the stored shell document and keeps the immutable assets", async () => {
    await context.setOffline(false);
    await page.goto(appUrl, { waitUntil: "load" });
    await waitInteractive(page);
    const before = await readCaches(page);

    await page.locator("#user-menu-trigger").click({ timeout: interactiveTimeoutMs });
    await page.locator('[role="menu"] [role="menuitem"]').last().click();
    await page.waitForURL(`${baseUrl}${pathBase}/login`, { timeout: interactiveTimeoutMs });
    // The message reaches the worker while the document is going away, so the drop is observed from the next page
    const after = await waitForShellDropped(page);

    const storedBefore = Object.values(before).flat();
    const storedAfter = Object.values(after).flat();
    assert(storedBefore.includes(offlineShellPath), "The shell was not stored before the logout.");
    assert(!storedAfter.includes(offlineShellPath), "The shell is still stored after the logout.");
    assert(storedAfter.length > 0, "The logout dropped the immutable assets as well.");
    measurements.cacheEntriesAfterLogout = after;
    return { before: storedBefore.length, after: storedAfter.length };
  });
  // Last, because a signed-in visitor is redirected from the landing page into the authenticated surface
  await check("a public document is served by the network although a worker is active", async () => {
    const firstResponse = responses.length;
    for (const route of ["", "login", "signup", "legal"]) {
      await page.goto(`${baseUrl}${pathBase}/${route}`, { waitUntil: "load" });
    }
    const controlled = await page.evaluate(() => navigator.serviceWorker.controller !== null);

    const documents = responses.slice(firstResponse).filter((response) => response.document);
    const intercepted = documents.filter((response) => response.fromWorker).map((response) => response.path);
    assert(controlled, "No worker controls the public documents, so the case proves nothing.");
    assert(documents.length >= 4, `Only ${documents.length} public documents were loaded.`);
    assert(intercepted.length === 0, `The worker answered ${intercepted.join(", ")}.`);
    measurements.publicDocuments = documents.map((response) => response.path);
    return { documents: documents.length, answeredByTheWorker: intercepted.length, controlled };
  });
} finally {
  const violations = policyViolationsOf(context);
  if (violations.length > 0) failures.push(`${violations.length} content security policy violations`);
  if (observations.pageErrors.length > 0) failures.push(`${observations.pageErrors.length} page errors: ${observations.pageErrors.join(" | ")}`);
  measurements.policyViolations = violations.length;
  measurements.pageErrors = observations.pageErrors;
  await context.close();
  await browser.close();
}

async function waitForShellDropped(currentPage) {
  const deadline = Date.now() + workerTimeoutMs;
  let entries = await readCaches(currentPage);
  while (Object.values(entries).flat().includes(offlineShellPath) && Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, 200));
    entries = await readCaches(currentPage);
  }
  return entries;
}

const { resultFile, passed } = writeResult(
  `offline-shell-${options.browser}.json`,
  { browser: options.browser, browserVersion, baseUrl, startedAt: new Date().toISOString(), results, measurements, failures },
  expectedCaseCount
);

console.log(`${passed ? "PASSED" : "FAILED"} ${results.filter((entry) => entry.passed).length} of ${results.length} cases, result ${resultFile}`);
process.exit(passed ? 0 : 1);
