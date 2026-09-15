// Smoke test for the trimmed Release publish: a FluentUI event reaches .NET on an authenticated WebAssembly page.
// The trimmer removes members that only reflection or JSON deserialization reaches, so a component can render and still
// lose its events in a publish that works in Development.
//
// Prerequisites, all through the developer CLI from the repository root:
//   1. dotnet run --project developer-cli -- start-stack --without-blazor-host   (with this worktree's stack stopped first)
//   2. dotnet run --project developer-cli -- blazor-publish
//   3. dotnet run --project developer-cli -- blazor-serve   (keeps running)
// Run:
//   dotnet run --project developer-cli -- blazor-harness trimmed-smoke --browser all
//
// The test signs up a new user through the Blazor signup page with the one-time password read from the local mail server,
// so it needs no stored cookies, secrets or debug-only codes. It fails unless the gateway serves this worktree's publish in
// Production, the users page on the shared DataList loads its data, typing in the FluentTextInput search box runs its .NET
// handler (the search reaches the URL and the list), and the page raises no page error. Writes a JSON result file under .workspace/blazor-tests/, and on failure a
// browser trace of the signup or of the users page next to it.

import { mkdirSync, rmSync, writeFileSync } from "node:fs";
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
  signUpThroughBlazor,
  startTrace,
  stopTrace
} from "./support/stack.mjs";

const interactiveTimeoutMs = 60_000;
const searchTimeoutMs = 10_000;
const settleMs = 1_000;

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
mkdirSync(resultsFolder, { recursive: true });

const browser = await launchBrowser(options.browser);
const result = { browser: options.browser, browserVersion: browser.version(), playwrightVersion, baseUrl, startedAt: new Date().toISOString() };
const failures = [];
const signupTraceFile = path.join(resultsFolder, `trimmed-smoke-${options.browser}-signup-trace.zip`);
const usersPageTraceFile = path.join(resultsFolder, `trimmed-smoke-${options.browser}-trace.zip`);
let context;
// A trace from an earlier run would read as this run's failure
for (const traceFile of [signupTraceFile, usersPageTraceFile]) rmSync(traceFile, { force: true });

try {
  const email = `smoke-${options.browser}-${Date.now()}@example.com`;
  const account = await signUpThroughBlazor(browser, options.browser, email, undefined, signupTraceFile);
  result.account = { email: account.email };

  context = await newContext(browser, options.browser, account.storageState);
  await startTrace(context);
  const page = await context.newPage();
  const observations = observeErrors(page);
  const clientAssemblyRequests = [];
  page.on("request", (request) => {
    if (/\/_framework\/Blazor\.Client\.[^/]*\.wasm$/.test(new URL(request.url()).pathname)) clientAssemblyRequests.push(new URL(request.url()).pathname);
  });

  const usersUrl = `${baseUrl}${pathBase}/account/users`;
  const response = await page.goto(usersUrl, { waitUntil: "load" });
  result.status = response.status();
  result.finalUrl = page.url();
  result.productionPolicy = isProductionPolicy(response.headers()["content-security-policy"]);

  const gridState = await page
    .locator('[data-testid="users-grid"][data-list-state="ready"]')
    .waitFor({ timeout: interactiveTimeoutMs })
    .then(() => "ready", () => "not ready");
  result.gridState = gridState;

  // The signed-up owner is the tenant's one user, so searching for the email keeps exactly that row
  result.searchInUrlBeforeTyping = new URL(page.url()).searchParams.has("search");
  await page.getByRole("textbox", { name: "Search" }).fill(account.email);
  result.searchReachedDotNet = await page
    .waitForURL((url) => url.searchParams.get("search") === account.email, { timeout: searchTimeoutMs })
    .then(() => page.locator('[data-testid="users-grid"][data-list-state="ready"][data-list-total-count="1"]').waitFor({ timeout: searchTimeoutMs }))
    .then(() => true, () => false);
  await page.waitForTimeout(settleMs);

  // The Blazor.Client assembly is fingerprinted by content, so a Debug build or another worktree's publish requests a route
  // that this publish does not have
  const publishedRoutes = new Set(readPublishedEndpoints().map((endpoint) => `${pathBase}/${endpoint.Route}`));
  result.clientAssemblyRequests = clientAssemblyRequests;
  result.servedFromThisPublish = clientAssemblyRequests.length > 0 && clientAssemblyRequests.every((route) => publishedRoutes.has(route));
  result.violations = await page.evaluate(() => window.__policyViolations);
  Object.assign(result, observations);

  if (result.status !== 200) failures.push(`status ${result.status}`);
  if (result.finalUrl !== usersUrl) failures.push(`landed on ${result.finalUrl}`);
  if (!result.productionPolicy) failures.push("the host does not run in Production");
  if (!result.servedFromThisPublish) failures.push(`the Blazor.Client assembly requested (${clientAssemblyRequests.join(", ") || "none"}) is not in this publish`);
  if (gridState !== "ready") failures.push("the users grid did not load");
  if (result.searchInUrlBeforeTyping) failures.push("the users page started with a search");
  if (!result.searchReachedDotNet) failures.push("typing in the FluentTextInput search box did not reach its .NET handler");
  if (observations.pageErrors.length > 0) failures.push(`${observations.pageErrors.length} page errors`);
} catch (error) {
  failures.push(String(error.stack ?? error).slice(0, 1_000));
} finally {
  if (context !== undefined) {
    await stopTrace(context, failures.length > 0 ? usersPageTraceFile : undefined);
    await context.close();
  }
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
