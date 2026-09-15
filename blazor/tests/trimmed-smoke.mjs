// Smoke test for the trimmed Release publish: a FluentUI event reaches .NET on an authenticated WebAssembly page.
// The trimmer removes members that only reflection or JSON deserialization reaches, so a component can render and still
// lose its events in a publish that works in Development.
//
// Prerequisites, all through the developer CLI from the repository root:
//   1. The stack is running (aspire-restart skill), and the Aspire resource blazor-host is stopped.
//   2. dotnet run --project developer-cli -- blazor-publish
//   3. dotnet run --project developer-cli -- blazor-serve   (keeps running)
// Run:
//   dotnet run --project developer-cli -- blazor-harness trimmed-smoke --browser all
//
// The test signs up a new user through the Blazor signup page with the one-time password read from the local mail server,
// so it needs no stored cookies, secrets or debug-only codes. It fails unless the gateway serves this worktree's publish in
// Production, the users page on the shared DataList loads its data, a click on a FluentButton runs its .NET handler (the filter
// dialog opens), and the page raises no page error. Writes a JSON result file under .workspace/blazor-tests/.

import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import {
  baseUrl,
  isProductionPolicy,
  launchBrowser,
  newContext,
  observeErrors,
  parseArguments,
  pathBase,
  playwrightVersion,
  readPublishedEndpoints,
  resultsFolder,
  signUpThroughBlazor
} from "./support/stack.mjs";

const interactiveTimeoutMs = 60_000;
const dialogTimeoutMs = 10_000;
const settleMs = 1_000;

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
mkdirSync(resultsFolder, { recursive: true });

const browser = await launchBrowser(options.browser);
const result = { browser: options.browser, browserVersion: browser.version(), playwrightVersion, baseUrl, startedAt: new Date().toISOString() };
const failures = [];

try {
  const account = await signUpThroughBlazor(browser, options.browser, `smoke-${options.browser}-${Date.now()}@example.com`);
  result.account = { email: account.email };

  const context = await newContext(browser, options.browser, account.storageState);
  const page = await context.newPage();
  const observations = observeErrors(page);
  const clientAssemblyRequests = [];
  page.on("request", (request) => {
    if (/\/_framework\/Blazor\.Client\.[^/]*\.wasm$/.test(new URL(request.url()).pathname)) clientAssemblyRequests.push(new URL(request.url()).pathname);
  });

  const usersUrl = `${baseUrl}${pathBase}/app/users/quick`;
  const response = await page.goto(usersUrl, { waitUntil: "load" });
  result.status = response.status();
  result.finalUrl = page.url();
  result.productionPolicy = isProductionPolicy(response.headers()["content-security-policy"]);

  const gridState = await page
    .locator('[data-testid="users-grid"][data-list-state="ready"]')
    .waitFor({ timeout: interactiveTimeoutMs })
    .then(() => "ready", () => "not ready");
  result.gridState = gridState;

  const applyButton = page.locator('[data-testid="filter-apply"]');
  result.dialogVisibleBeforeClick = await applyButton.isVisible();
  await page.locator('[data-testid="filters"]').click();
  result.dialogOpenedByClick = await applyButton.waitFor({ state: "visible", timeout: dialogTimeoutMs }).then(() => true, () => false);
  await page.waitForTimeout(settleMs);

  // The Blazor.Client assembly is fingerprinted by content, so a Debug build or another worktree's publish requests a route
  // that this publish does not have
  const publishedRoutes = new Set(readPublishedEndpoints().map((endpoint) => `${pathBase}/${endpoint.Route}`));
  result.clientAssemblyRequests = clientAssemblyRequests;
  result.servedFromThisPublish = clientAssemblyRequests.length > 0 && clientAssemblyRequests.every((route) => publishedRoutes.has(route));
  result.violations = await page.evaluate(() => window.__policyViolations);
  Object.assign(result, observations);
  await context.close();

  if (result.status !== 200) failures.push(`status ${result.status}`);
  if (result.finalUrl !== usersUrl) failures.push(`landed on ${result.finalUrl}`);
  if (!result.productionPolicy) failures.push("the host does not run in Production");
  if (!result.servedFromThisPublish) failures.push(`the Blazor.Client assembly requested (${clientAssemblyRequests.join(", ") || "none"}) is not in this publish`);
  if (gridState !== "ready") failures.push("the users grid did not load");
  if (result.dialogVisibleBeforeClick) failures.push("the filter dialog was open before the click");
  if (!result.dialogOpenedByClick) failures.push("the FluentButton click did not reach its .NET handler");
  if (observations.pageErrors.length > 0) failures.push(`${observations.pageErrors.length} page errors`);
} catch (error) {
  failures.push(String(error.stack ?? error).slice(0, 1_000));
} finally {
  await browser.close();
}

result.failures = failures;
result.passed = failures.length === 0;
result.finishedAt = new Date().toISOString();
const resultFile = path.join(resultsFolder, `trimmed-smoke-${options.browser}.json`);
writeFileSync(resultFile, JSON.stringify(result, null, 2));
console.log(`${options.browser} ${result.browserVersion}: ${result.passed ? "passed" : `failed: ${failures.join(" ; ")}`}`);
console.log(`Result file: ${resultFile}`);
process.exitCode = result.passed ? 0 : 1;
