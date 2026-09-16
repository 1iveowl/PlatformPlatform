// Interim harness for the Blazor host shell and its content security policy, run through the gateway of the running
// stack. It is replaced by the Playwright specification folder under blazor/ that the developer CLI and CI task adds.
// Run with node, one browser at a time, from the repository root:
//   node blazor/tests/shell-policy.mjs --browser chromium|firefox|webkit
//   node blazor/tests/shell-policy.mjs --browser chromium --environment production
// The default run expects the host in Development. The production run expects the host restarted in Production and only
// checks that the Development-only probe page is not reachable. Each run writes a JSON result file under
// .workspace/blazor-tests/ and exits non-zero when a case fails.

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";

const repositoryRoot = path.resolve(import.meta.dirname, "../..");
const requireFromApplication = createRequire(path.join(repositoryRoot, "application/"));
const playwright = requireFromApplication("playwright");
const basePort = readFileSync(path.join(repositoryRoot, ".workspace/port.txt"), "utf8").trim();
const baseUrl = `https://app.dev.localhost:${basePort}`;
const pathBase = "/blazor";
const verificationCode = "UNLOCK";
const interactiveTimeoutMs = 60_000;
const mockProviderCookie = "__Test_Use_Mock_Provider";
// MitID is not a signup provider, so it has a login start and a verification start only
const externalStarts = [
  { provider: "Google", flow: "login" },
  { provider: "Google", flow: "signup" },
  { provider: "Entra", flow: "login" },
  { provider: "Entra", flow: "signup" },
  { provider: "MitId", flow: "login" }
];

const options = parseArguments(process.argv.slice(2));
const resultsFolder = path.join(repositoryRoot, ".workspace/blazor-tests");
mkdirSync(resultsFolder, { recursive: true });

const browser = await playwright[options.browser].launch();
const result = {
  browser: options.browser,
  browserVersion: browser.version(),
  environment: options.environment,
  playwrightVersion: requireFromApplication("playwright/package.json").version,
  baseUrl,
  startedAt: new Date().toISOString(),
  cases: {}
};

try {
  if (options.environment === "production") {
    result.cases.productionNotFound = await runProductionNotFound();
  } else {
    const account = await signUpThroughReact();
    result.account = { email: account.email };
    result.cases.policyPositive = await runPolicyPositive(account);
    result.cases.policyNegative = await runPolicyNegative();
    result.cases.deeperRoute = await runDeeperRoute(account);
    result.cases.enhancedNavigation = await runEnhancedNavigation();
    result.cases.workerSource = await runWorkerSource(account);
    result.cases.formAction = await runFormAction(account);
    result.cases.manifest = await runManifest();
  }
} finally {
  await browser.close();
}

result.finishedAt = new Date().toISOString();
const resultFile = path.join(resultsFolder, `shell-policy-${options.browser}-${options.environment}.json`);
writeFileSync(resultFile, JSON.stringify(result, null, 2));
console.table(Object.entries(result.cases).map(([name, value]) => ({ case: name, passed: value.passed, failures: value.failures.join(" ; ") })));
console.log(`${options.browser} ${result.browserVersion} (${options.environment})\nResult file: ${resultFile}`);
process.exitCode = Object.values(result.cases).every((value) => value.passed) ? 0 : 1;

function parseArguments(argumentList) {
  const parsed = { browser: "chromium", environment: "development" };
  for (let index = 0; index < argumentList.length; index += 2) {
    parsed[argumentList[index].replace(/^--/, "")] = argumentList[index + 1];
  }
  return parsed;
}

function outcome(details, failures) {
  return { passed: failures.length === 0, failures, ...details };
}

// An explicit locale: headless Chromium in the container otherwise reports "en-US@posix", which the .NET runtime rejects
// as a culture name and aborts the WebAssembly start
async function newContext(storageState) {
  const context = await browser.newContext({ ignoreHTTPSErrors: true, locale: "en-US", storageState });
  await context.addInitScript(() => {
    window.__policyViolations = [];
    document.addEventListener("securitypolicyviolation", (event) => {
      window.__policyViolations.push({ effectiveDirective: event.effectiveDirective, blockedURI: event.blockedURI, sample: event.sample });
    });
  });
  return context;
}

function observe(page) {
  const observations = { consoleErrors: [], pageErrors: [], errorResponses: [] };
  page.on("response", (response) => {
    if (response.status() >= 400) observations.errorResponses.push(`${response.status()} ${response.url()}`);
  });
  page.on("console", (message) => {
    if (message.type() === "error") observations.consoleErrors.push(message.text().slice(0, 400));
  });
  page.on("pageerror", (error) => observations.pageErrors.push(String(error.message).slice(0, 400)));
  return observations;
}

function readViolations(page) {
  return page.evaluate(() => window.__policyViolations);
}

async function waitForInteractive(page) {
  return page
    .waitForFunction(() => document.querySelector('[data-testid="render-mode"]')?.textContent === "Interactive: True", null, {
      timeout: interactiveTimeoutMs
    })
    .then(() => true, () => false);
}

// Lets late violation reports and console messages arrive before they are read
function settle(page) {
  return page.waitForTimeout(1_000);
}

function cleanPageFailures(label, violations, observations) {
  const failures = [];
  if (violations.length > 0) failures.push(`${label}: ${violations.length} violations`);
  if (observations.pageErrors.length > 0) failures.push(`${label}: ${observations.pageErrors.length} page errors`);
  if (observations.consoleErrors.length > 0) failures.push(`${label}: ${observations.consoleErrors.length} console errors`);
  return failures;
}

async function typeOneTimeCode(page) {
  const input = page.locator('input[autocomplete="one-time-code"]');
  await input.waitFor();
  await page.waitForFunction(() => document.activeElement?.getAttribute("autocomplete") === "one-time-code");
  await page.evaluate((code) => {
    const element = document.activeElement;
    Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value").set.call(element, code);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  }, verificationCode);
}

// Mirrors completeSignupFlow in application/shared-webapp/tests/e2e/utils/test-data.ts
async function signUpThroughReact() {
  const email = `shell-${options.browser}-${Date.now()}@example.com`;
  const context = await newContext();
  const page = await context.newPage();
  for (let attempt = 1; ; attempt++) {
    await page.goto(`${baseUrl}/signup`);
    const ready = await page.getByRole("textbox", { name: "Email" }).waitFor({ timeout: 30_000 }).then(() => true, () => false);
    if (ready) break;
    if (attempt === 6) throw new Error("The React signup page did not render.");
  }
  await page.getByRole("textbox", { name: "Email" }).fill(email);
  await page.getByRole("button", { name: "Sign up with email" }).click();
  await page.waitForURL(`${baseUrl}/signup/verify`);
  await typeOneTimeCode(page);
  await page.waitForURL(/\/welcome/);
  await page.getByRole("textbox", { name: "Account name" }).fill("Shell Policy");
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByRole("heading", { name: "Let's set up your profile" }).waitFor();
  await page.getByRole("textbox", { name: "First name" }).fill("Shell");
  await page.getByRole("textbox", { name: "Last name" }).fill("Policy");
  await page.getByRole("button", { name: "Continue" }).click();
  await page.waitForURL(`${baseUrl}/dashboard`);
  const storageState = await context.storageState();
  await context.close();
  return { email, storageState };
}

async function loadAndCheck(context, url, { interactive }) {
  const page = await context.newPage();
  const observations = observe(page);
  const response = await page.goto(url, { waitUntil: "load" });
  const becameInteractive = interactive ? await waitForInteractive(page) : null;
  await settle(page);
  const violations = await readViolations(page);
  const details = {
    url,
    status: response.status(),
    contentSecurityPolicy: response.headers()["content-security-policy"] ?? null,
    finalUrl: page.url(),
    interactive: becameInteractive,
    violations,
    ...observations
  };
  await page.close();

  const failures = cleanPageFailures(new URL(url).pathname, violations, observations);
  if (details.status !== 200) failures.push(`${url}: status ${details.status}`);
  if (interactive && !becameInteractive) failures.push(`${url}: did not turn interactive`);
  if (details.finalUrl !== url) failures.push(`${url}: landed on ${details.finalUrl}`);
  return { details, failures };
}

async function runPolicyPositive(account) {
  const anonymous = await newContext();
  const shell = await loadAndCheck(anonymous, `${baseUrl}${pathBase}/`, { interactive: false });
  await anonymous.close();

  const signedIn = await newContext(account.storageState);
  const authenticated = await loadAndCheck(signedIn, `${baseUrl}${pathBase}/app`, { interactive: true });
  await signedIn.close();

  return outcome({ shell: shell.details, authenticated: authenticated.details }, [...shell.failures, ...authenticated.failures]);
}

async function runPolicyNegative() {
  const context = await newContext();
  const page = await context.newPage();
  const response = await page.goto(`${baseUrl}${pathBase}/development/policy-probe`, { waitUntil: "load" });
  await settle(page);
  const markers = await page.evaluate(() => ({ ...document.documentElement.dataset }));
  const violations = await readViolations(page);
  await context.close();

  const failures = [];
  if (response.status() !== 200) failures.push(`probe status ${response.status()}`);
  if (markers.probeInlineWithNonce !== "ran") failures.push("the nonced inline script did not run");
  if (markers.probeInlineWithoutNonce !== undefined) failures.push("the inline script without the nonce ran");
  const inlineBlocked = violations.some((violation) => violation.effectiveDirective === "script-src-elem" && violation.blockedURI === "inline");
  const untrustedBlocked = violations.some(
    (violation) => violation.effectiveDirective === "script-src-elem" && violation.blockedURI.startsWith("https://untrusted.invalid")
  );
  if (!inlineBlocked) failures.push("no script-src-elem violation for the inline script");
  if (!untrustedBlocked) failures.push("no script-src-elem violation for the untrusted host");
  return outcome({ status: response.status(), markers, violations }, failures);
}

async function runDeeperRoute(account) {
  const context = await newContext(account.storageState);
  const checked = await loadAndCheck(context, `${baseUrl}${pathBase}/app/details`, { interactive: true });
  await context.close();
  // Recorded, not asserted: WebKit resolves the scoped CSS bundle's relative @import of the package bundles against the
  // document URL, so two segments below the path base those two stylesheet requests return 404
  const failures = checked.failures.filter((failure) => !failure.endsWith("console errors"));
  return outcome(checked.details, failures);
}

async function runEnhancedNavigation() {
  const context = await newContext();
  const page = await context.newPage();
  const observations = observe(page);
  await page.goto(`${baseUrl}${pathBase}/`, { waitUntil: "load" });
  await page.waitForFunction(() => window.Blazor !== undefined);
  // A full document load would drop this marker, so its survival proves the navigation was enhanced
  await page.evaluate(() => (window.__sameDocument = true));
  await page.locator('[data-testid="nav-terms"]').click();
  await page.locator('[data-testid="terms-text"]').waitFor();
  await settle(page);
  const sameDocument = await page.evaluate(() => window.__sameDocument === true);
  const violations = await readViolations(page);
  const finalUrl = page.url();
  await context.close();

  const failures = cleanPageFailures("landing to terms", violations, observations);
  if (!sameDocument) failures.push("the navigation was a full document load, not enhanced");
  if (finalUrl !== `${baseUrl}${pathBase}/legal/terms`) failures.push(`landed on ${finalUrl}`);
  return outcome({ finalUrl, sameDocument, violations, ...observations }, failures);
}

// worker-src 'self': a same-origin worker script must be allowed. The blob: worker outcome is recorded, not asserted, because
// browsers differ in whether and where they report it
async function runWorkerSource(account) {
  const context = await newContext(account.storageState);
  const page = await context.newPage();
  await page.goto(`${baseUrl}${pathBase}/app`, { waitUntil: "load" });
  await waitForInteractive(page);
  const creation = await page.evaluate((sameOriginScript) => {
    const attempt = (url) => {
      try {
        new Worker(url).terminate();
        return "created";
      } catch (error) {
        return `threw ${error.name}`;
      }
    };
    return { sameOrigin: attempt(sameOriginScript), blob: attempt(URL.createObjectURL(new Blob(["close()"], { type: "text/javascript" }))) };
  }, `${pathBase}/js/document-base-uri.js`);
  await settle(page);
  const violations = await readViolations(page);
  await context.close();

  const failures = [];
  if (violations.some((violation) => violation.effectiveDirective === "worker-src" && !violation.blockedURI.startsWith("blob")))
    failures.push("the same-origin worker was blocked");
  return outcome({ creation, violations }, failures);
}

// Each start is submitted as a form from a Blazor page, so a form-action directive in the page's policy would govern the
// submission and the redirects that follow it. The mock provider redirects to the callback on https://localhost:<port>,
// another origin than the gateway host, the way a real provider redirects to its own origin.
async function followStartAsForm(context, startPath, flow) {
  const page = await context.newPage();
  const callbackRequests = [];
  page.on("request", (request) => {
    if (new URL(request.url()).pathname.includes(`/${flow}/callback`)) callbackRequests.push(request.url());
  });
  await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
  const documentUrl = page.url();
  const violationsBefore = await readViolations(page);
  const navigated = page.waitForURL((url) => url.href !== documentUrl, { timeout: 15_000 }).then(() => true, () => false);
  await page.evaluate((action) => {
    const form = document.createElement("form");
    form.method = "get";
    form.action = action;
    document.body.append(form);
    form.submit();
  }, startPath);
  const left = await navigated;
  await page.waitForLoadState("load").catch(() => {});
  // A blocked submission leaves the document in place; its violation event can arrive after the navigation timeout
  const violationsAfterSubmit = left ? [] : await page.waitForTimeout(2_000).then(() => readViolations(page));
  const finalUrl = page.url();
  await page.close();
  return { startPath, left, finalUrl, callbackRequests, violations: [...violationsBefore, ...violationsAfterSubmit] };
}

async function runFormAction(account) {
  const anonymous = await newContext();
  await anonymous.addCookies([{ name: mockProviderCookie, value: "true", url: baseUrl }]);
  const starts = [];
  for (const { provider, flow } of externalStarts) {
    starts.push({ provider, flow, ...(await followStartAsForm(anonymous, `/api/account/authentication/${provider}/${flow}/start`, flow)) });
  }
  await anonymous.close();

  // Verification starts with a POST that returns the authorization URL, which the page then opens as a navigation, so
  // form-action does not govern it; it is followed to prove the policy leaves it working
  const signedIn = await newContext(account.storageState);
  await signedIn.addCookies([{ name: mockProviderCookie, value: "true", url: baseUrl }]);
  const page = await signedIn.newPage();
  const callbackRequests = [];
  page.on("request", (request) => {
    if (new URL(request.url()).pathname.includes("/verification/callback")) callbackRequests.push(request.url());
  });
  await page.goto(`${baseUrl}${pathBase}/app`, { waitUntil: "load" });
  await waitForInteractive(page);
  const start = await page.evaluate(async () => {
    const bootstrap = await (await fetch("/api/account/bootstrap")).json();
    const response = await fetch("/api/account/authentication/MitId/verification/start", {
      method: "POST",
      headers: { "content-type": "application/json", "x-xsrf-token": bootstrap.antiforgeryToken },
      body: JSON.stringify({ returnPath: "/user/profile" })
    });
    return { status: response.status, body: await response.json().catch(() => null) };
  });
  let verification = { startStatus: start.status, startBody: start.body };
  if (start.body?.authorizationUrl) {
    const violationsBefore = await readViolations(page);
    await page.evaluate((url) => location.assign(url), start.body.authorizationUrl);
    await page.waitForLoadState("load").catch(() => {});
    await page.waitForTimeout(2_000);
    verification = { ...verification, finalUrl: page.url(), callbackRequests, violations: violationsBefore };
  }
  await signedIn.close();

  const failures = [];
  for (const entry of starts) {
    if (entry.violations.some((violation) => violation.effectiveDirective === "form-action")) failures.push(`${entry.provider} ${entry.flow}: form-action violation`);
    if (entry.callbackRequests.length === 0) failures.push(`${entry.provider} ${entry.flow}: callback not reached (${entry.finalUrl})`);
  }
  if (!verification.callbackRequests?.length) failures.push(`MitId verification: callback not reached (status ${verification.startStatus})`);
  return outcome({ starts, verification }, failures);
}

async function runManifest() {
  const context = await newContext();
  const page = await context.newPage();
  await page.goto(`${baseUrl}${pathBase}/`, { waitUntil: "load" });
  const manifestHref = await page.evaluate(() => document.querySelector('link[rel="manifest"]')?.getAttribute("href") ?? null);
  const failures = [];
  const resources = [];
  let manifest = null;
  if (manifestHref === null) {
    failures.push("no manifest link");
  } else {
    // Fetched by the page, because node does not resolve the *.localhost gateway host the browser resolves itself
    const fetched = await page.evaluate(async (href) => {
      const read = async (url) => {
        const response = await fetch(url);
        return { url, status: response.status, contentType: response.headers.get("content-type"), body: await response.text() };
      };
      const manifestResource = await read(href);
      const icons = JSON.parse(manifestResource.body).icons;
      return [manifestResource, ...(await Promise.all(icons.map((icon) => read(icon.src))))];
    }, manifestHref);
    manifest = JSON.parse(fetched[0].body);
    resources.push(...fetched.map(({ url, status, contentType }) => ({ url, status, contentType })));
  }
  for (const resource of resources) {
    if (resource.status !== 200) failures.push(`${resource.url}: status ${resource.status}`);
  }

  let installability = null;
  if (options.browser === "chromium") {
    const session = await context.newCDPSession(page);
    await page.waitForTimeout(1_000);
    const appManifest = await session.send("Page.getAppManifest");
    const { installabilityErrors } = await session.send("Page.getInstallabilityErrors");
    installability = { manifestUrl: appManifest.url, manifestParseErrors: appManifest.errors, installabilityErrors };
    if (installabilityErrors.length > 0) failures.push(`installability errors: ${installabilityErrors.map((error) => error.errorId).join(", ")}`);
    if (appManifest.errors.length > 0) failures.push(`manifest parse errors: ${appManifest.errors.length}`);
  }
  const violations = await readViolations(page);
  if (violations.length > 0) failures.push(`${violations.length} violations`);
  await context.close();
  return outcome({ manifestHref, manifest, resources, installability, violations }, failures);
}

async function runProductionNotFound() {
  const context = await newContext();
  const page = await context.newPage();
  const probe = await page.goto(`${baseUrl}${pathBase}/development/policy-probe`);
  const probeMarkers = await page.evaluate(() => ({ ...document.documentElement.dataset }));
  const landing = await page.goto(`${baseUrl}${pathBase}/`);
  const landingPolicy = (await landing.headerValue("content-security-policy")) ?? "";
  await context.close();
  const failures = [];
  if (probe.status() !== 404) failures.push(`probe status ${probe.status()}`);
  if (probeMarkers.probeInlineWithNonce !== undefined) failures.push("the probe page rendered");
  if (landing.status() !== 200) failures.push(`landing status ${landing.status()}`);
  // The Development host list adds wildcard ports; its absence shows the host really runs outside Development
  if (landingPolicy.includes(":*")) failures.push("the landing policy carries the Development host list");
  return outcome({ probeStatus: probe.status(), probeMarkers, landingStatus: landing.status(), landingPolicy }, failures);
}
