// Smoke test for the trimmed Release publish: a FluentUI event reaches .NET on an authenticated WebAssembly page.
// The trimmer removes members that only reflection or JSON deserialization reaches, so a component can render and still
// lose its events in a publish that works in Development.
//
// Prerequisites, all through the developer CLI from the repository root:
//   1. dotnet run --project developer-cli -- start-stack --without-blazor-host   (with this worktree's stack stopped first)
//   2. dotnet run --project developer-cli -- blazor-publish
//   3. dotnet run --project developer-cli -- blazor-serve   (keeps running)
// Run:
//   dotnet run --project developer-cli -- blazor-harness trimmed-smoke --browser all [--culture en-US|da-DK]
//
// The test signs up a new user through the Blazor signup and welcome pages in the given culture with the one-time password
// read from the local mail server, so it needs no stored cookies, secrets or debug-only codes. It fails unless the gateway
// serves this worktree's publish in Production, the users page on the shared DataList loads its data and typing in the
// FluentTextInput search box runs its .NET handler (the search reaches the URL and the list). The verdict is strict: any
// content security policy violation, console error, page error or HTTP error response on the signup, welcome or users
// pages fails it, with nothing allowlisted. Writes a JSON result file under .workspace/blazor-tests/, and on failure a
// browser trace of the signup or of the users page next to it. Traces hold cookies and typed values, so they stay local.

import { mkdirSync, readFileSync, rmSync } from "node:fs";
import path from "node:path";
import {
  baseUrl,
  isProductionPolicy,
  launchBrowser,
  hostConfiguration,
  newContext,
  observeErrors,
  parseArguments,
  pathBase,
  playwrightVersion,
  policyViolationsOf,
  readPublishedEndpoints,
  redact,
  resultsFolder,
  signUpThroughBlazor,
  startTrace,
  stopTrace,
  strictFailures,
  writeResult
} from "./support/stack.mjs";

const interactiveTimeoutMs = 60_000;
const searchTimeoutMs = 10_000;
const settleMs = 1_000;

const options = parseArguments(process.argv.slice(2), { browser: "chromium", culture: "en-US" });
const cultures = ["en-US", "da-DK"];
if (!cultures.includes(options.culture)) throw new Error(`Unknown culture '${options.culture}'. Use ${cultures.join(" or ")}.`);
mkdirSync(resultsFolder, { recursive: true });

const browser = await launchBrowser(options.browser);
const result = { browser: options.browser, browserVersion: browser.version(), culture: options.culture, playwrightVersion, baseUrl, startedAt: new Date().toISOString() };
const failures = [];
const checks = [];
const expectedCheckCount = 9;
const runName = `trimmed-smoke-${options.browser}-${options.culture}`;
const signupTraceFile = path.join(resultsFolder, `${runName}-signup-trace.zip`);
const usersPageTraceFile = path.join(resultsFolder, `${runName}-trace.zip`);
let context;
// A trace from an earlier run would read as this run's failure
for (const traceFile of [signupTraceFile, usersPageTraceFile]) rmSync(traceFile, { force: true });

try {
  const email = `smoke-${options.browser}-${options.culture.toLowerCase()}-${Date.now()}@example.com`;
  const account = await signUpThroughBlazor(browser, options.browser, email, options.culture, signupTraceFile, true);
  result.account = { email: account.email };
  result.signup = { violations: account.violations, ...account.observations };
  checkStrict("signup and welcome", account.observations, account.violations);

  context = await newContext(browser, options.browser, account.storageState, options.culture);
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
  Object.assign(result, hostConfiguration(response.headers()["content-security-policy"]));

  const gridState = await page
    .locator('[data-testid="users-grid"][data-list-state="ready"]')
    .waitFor({ timeout: interactiveTimeoutMs })
    .then(() => "ready", () => "not ready");
  result.gridState = gridState;

  // The signed-up owner is the tenant's one user, so searching for the email keeps exactly that row
  result.searchInUrlBeforeTyping = new URL(page.url()).searchParams.has("search");
  await page.getByRole("textbox", { name: searchLabel(options.culture) }).fill(account.email);
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
  result.usersPage = { violations: [...policyViolationsOf(context)], ...observations };

  check("status 200", result.status === 200, `status ${result.status}`);
  check("landed on the users page", result.finalUrl === usersUrl, `landed on ${result.finalUrl}`);
  check("host runs in Production", result.productionPolicy, "the host does not run in Production");
  check("served from this publish", result.servedFromThisPublish, `the Blazor.Client assembly requested (${clientAssemblyRequests.join(", ") || "none"}) is not in this publish`);
  check("users grid loaded", gridState === "ready", "the users grid did not load");
  check("users page started without a search", !result.searchInUrlBeforeTyping, "the users page started with a search");
  check("search reached its .NET handler", result.searchReachedDotNet, "typing in the FluentTextInput search box did not reach its .NET handler");
  checkStrict("users page", observations, result.usersPage.violations);
} catch (error) {
  failures.push(redact(String(error.stack ?? error)).slice(0, 1_000));
} finally {
  if (context !== undefined) {
    await stopTrace(context, failures.length > 0 ? usersPageTraceFile : undefined);
    await context.close();
  }
  await browser.close();
}

result.checks = checks;
result.failures = failures;
result.finishedAt = new Date().toISOString();
const verdict = writeResult(`${runName}.json`, result, expectedCheckCount);
console.log(`${options.browser} ${result.browserVersion} ${options.culture}: ${verdict.passed ? "passed" : `failed: ${redact(verdict.failures.join(" ; "))}`}`);
console.log(`Result file: ${verdict.resultFile}`);
process.exitCode = verdict.passed ? 0 : 1;

function check(name, passed, failure) {
  checks.push({ name, passed: passed === true });
  if (passed !== true) failures.push(failure);
}

// Any policy violation, console error, page error or error response of the journey fails it
function checkStrict(label, observations, violations) {
  const strict = strictFailures(label, observations, violations);
  checks.push({ name: `${label}: no policy violation, console error, page error or error response`, passed: strict.length === 0 });
  failures.push(...strict);
}

// The accessible name of the users search box, read from the shared resources the page renders it from
function searchLabel(culture) {
  const suffix = culture === "en-US" ? "" : `.${culture}`;
  const resources = readFileSync(path.join(import.meta.dirname, `../../application/shared-kernel/SharedKernel.Localization/Resources/CommonStrings${suffix}.resx`), "utf8");
  const match = resources.match(/<data name="Search"[^>]*>\s*<value>([^<]*)<\/value>/);
  if (match === null) throw new Error(`No Search string in the ${culture} common resources.`);
  return match[1];
}
