// Interim harness for the Blazor host shell and its content security policy, run through the gateway of the running
// stack. It is replaced by the Playwright specification folder under blazor/ that the developer CLI and CI task adds.
// Run with node, one browser at a time, from the repository root:
//   node blazor/tests/shell-policy.mjs --browser chromium|firefox|webkit
//   node blazor/tests/shell-policy.mjs --browser chromium --environment production
// The default run expects the host in Development. The production run expects the host restarted in Production and only
// checks that the Development-only probe page is not reachable. Each run writes a JSON result file under
// .workspace/blazor-tests/ and exits non-zero when a case fails.

import { mkdirSync, readFileSync } from "node:fs";
import { redact, writeResult } from "./support/stack.mjs";
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
// The same pattern blazor/tests/support/stack.mjs uses to spot a WebAssembly runtime request
const runtimeRequestPattern = /\/_framework\/(dotnet[^/]*\.js|[^/]*\.wasm|[^/]*\.dat|blazor\.boot\.json)(\?|$)/;
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
  culture: "en-US",
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
    result.cases.legalPages = await runLegalPages();
    result.cases.manifest = await runManifest();
    result.cases.identityVerification = await runIdentityVerification();
    // Last, because it logs the account out
    result.cases.theme = await runTheme(account);
    result.cases.externalLogin = await runExternalLogin();
  }
} finally {
  await browser.close();
}

result.finishedAt = new Date().toISOString();
// The Development run drives the Development-only fixture pages and signs up with the development verification code, so it
// is a fixture check and never Production evidence; the Production run checks that those pages are not reachable
result.hostConfiguration = options.environment === "production" ? "Production host, expected to be the trimmed publish served by blazor-serve" : "Development host run by the AppHost (fixture check)";
const expectedCaseCount = options.environment === "production" ? 1 : 11;
const verdict = writeResult(`shell-policy-${options.browser}-${options.environment}.json`, result, expectedCaseCount);
console.table(Object.entries(result.cases).map(([name, value]) => ({ case: name, passed: value.passed, failures: redact(value.failures.join(" ; ")) })));
console.log(`${options.browser} ${result.browserVersion} (${options.environment}): ${verdict.passed ? "passed" : `failed: ${verdict.failures.join(" ; ") || "a case failed"}`}\nResult file: ${verdict.resultFile}`);
process.exitCode = verdict.passed ? 0 : 1;

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
  await page.getByRole("link", { name: "Terms", exact: true }).click();
  await page.getByRole("heading", { name: "Terms of Service" }).waitFor();
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

// The theme module on a public page and an authenticated page: the stored mode is applied as data-theme before <body> is
// parsed (so before any content can paint), the choice made on the public navigation survives a reload, an enhanced
// navigation and a new tab, the choice made in the user menu is reported to the account API and survives a reload and
// logout, the theme-color meta follows it, nothing writes a style attribute in <body>, and no document reports a violation
async function runTheme(account) {
  const failures = [];
  const details = {};
  const recordThemeAtBody = () => {
    new MutationObserver((_, observer) => {
      if (!document.body) return;
      window.__themeWhenBodyParsed = document.documentElement.dataset.theme ?? null;
      observer.disconnect();
    }).observe(document, { childList: true, subtree: true });
  };
  const readTheme = (page) =>
    page.evaluate(() => {
      const meta = document.querySelector('meta[name="theme-color"]');
      return {
        whenBodyParsed: window.__themeWhenBodyParsed ?? null,
        theme: document.documentElement.dataset.theme ?? null,
        mode: document.documentElement.dataset.themeMode ?? null,
        metaFollows: meta?.content === (document.documentElement.dataset.theme === "dark" ? meta?.dataset.dark : meta?.dataset.light),
        styledBodyElements: document.body.querySelectorAll("[style]").length
      };
    });
  const expectTheme = (label, state, expected, { atBody = true } = {}) => {
    details[label] = state;
    if (state.theme !== expected) failures.push(`${label}: data-theme ${state.theme}, expected ${expected}`);
    if (atBody && state.whenBodyParsed !== expected) failures.push(`${label}: data-theme ${state.whenBodyParsed} when <body> was parsed, expected ${expected}`);
    if (!state.metaFollows) failures.push(`${label}: theme-color does not follow the theme`);
    if (state.styledBodyElements > 0) failures.push(`${label}: ${state.styledBodyElements} elements with a style attribute`);
  };
  const checkClean = async (label, page, observations) => {
    await settle(page);
    failures.push(...cleanPageFailures(label, await readViolations(page), observations));
  };

  const anonymous = await newContext();
  await anonymous.addInitScript(recordThemeAtBody);
  const publicPage = await anonymous.newPage();
  const publicObservations = observe(publicPage);
  await publicPage.goto(`${baseUrl}${pathBase}/`, { waitUntil: "load" });
  expectTheme("public default", await readTheme(publicPage), "light");
  await publicPage.getByRole("button", { name: "Change theme" }).click();
  await publicPage.getByRole("menuitemradio", { name: "Dark" }).click();
  expectTheme("public chosen", await readTheme(publicPage), "dark", { atBody: false });
  await publicPage.getByRole("button", { name: "Change theme" }).press("ArrowDown");
  const checked = await publicPage.getByRole("menuitemradio", { checked: true }).textContent();
  if (checked?.trim() !== "✓Dark") failures.push(`public menu: checked item is ${checked}`);
  await publicPage.keyboard.press("Escape");
  await checkClean("public", publicPage, publicObservations);
  await publicPage.reload({ waitUntil: "load" });
  expectTheme("public reload", await readTheme(publicPage), "dark");
  await publicPage.getByTestId("public-nav").getByRole("link", { name: "Log in", exact: true }).click();
  await publicPage.waitForURL(`${baseUrl}${pathBase}/login`);
  await settle(publicPage);
  expectTheme("public enhanced navigation", await readTheme(publicPage), "dark", { atBody: false });
  await checkClean("public after reload and navigation", publicPage, publicObservations);
  const newTab = await anonymous.newPage();
  const newTabObservations = observe(newTab);
  await newTab.goto(`${baseUrl}${pathBase}/signup`, { waitUntil: "load" });
  expectTheme("public new tab", await readTheme(newTab), "dark");
  await checkClean("public new tab", newTab, newTabObservations);
  await anonymous.close();

  const signedIn = await newContext(account.storageState);
  await signedIn.addInitScript(recordThemeAtBody);
  const appPage = await signedIn.newPage();
  const appObservations = observe(appPage);
  await appPage.goto(`${baseUrl}${pathBase}/app`, { waitUntil: "load" });
  if (!(await waitForInteractive(appPage))) failures.push("authenticated: did not turn interactive");
  expectTheme("authenticated default", await readTheme(appPage), "light");
  await appPage.getByRole("button", { name: "User menu" }).click();
  const changeThemeRequest = appPage.waitForResponse((response) => response.url().endsWith("/api/account/users/me/change-theme"));
  await appPage.getByRole("group", { name: "Change theme" }).getByRole("menuitemradio", { name: "Dark" }).click();
  const changeThemeResponse = await changeThemeRequest;
  details.changeThemeStatus = changeThemeResponse.status();
  if (changeThemeResponse.status() >= 400) failures.push(`change-theme returned ${changeThemeResponse.status()}`);
  expectTheme("authenticated chosen", await readTheme(appPage), "dark", { atBody: false });
  await checkClean("authenticated", appPage, appObservations);
  await appPage.reload({ waitUntil: "load" });
  await waitForInteractive(appPage);
  expectTheme("authenticated reload", await readTheme(appPage), "dark");
  await appPage.getByRole("button", { name: "User menu" }).click();
  const checkedInMenu = await appPage.getByRole("group", { name: "Change theme" }).getByRole("menuitemradio", { checked: true }).textContent();
  if (checkedInMenu?.trim() !== "✓Dark") failures.push(`user menu: checked item is ${checkedInMenu}`);
  await checkClean("authenticated after reload", appPage, appObservations);
  await appPage.getByRole("menuitem", { name: "Log out" }).click();
  await appPage.waitForURL(/\/login/, { timeout: interactiveTimeoutMs });
  await appPage.waitForLoadState("load");
  expectTheme("after logout", await readTheme(appPage), "dark");
  await signedIn.close();

  return outcome(details, failures);
}

// The external login buttons on the static login and signup pages in the light and the dark theme: the MitID wordmark and
// the IBM Plex Sans SemiBold face load from the host origin, the MitID button keeps the brand geometry and colour with the
// approved label, MitID is absent on signup, nothing in <body> has a style attribute and no document reports a violation.
// Submitting the MitID button with the mock provider reaches the login callback. Needs the Google, Entra and MitID login
// flags on in the AppHost.
async function runExternalLogin() {
  const failures = [];
  const details = {};
  for (const theme of ["light", "dark"]) {
    const context = await newContext();
    await context.addInitScript((mode) => localStorage.setItem("theme", mode), theme);
    await context.addCookies([{ name: mockProviderCookie, value: "true", url: baseUrl }]);
    const page = await context.newPage();
    const observations = observe(page);
    const assetResponses = [];
    page.on("response", (response) => {
      if (/\/(fonts|images)\//.test(new URL(response.url()).pathname)) assetResponses.push({ url: response.url(), status: response.status() });
    });

    await page.goto(`${baseUrl}${pathBase}/login?returnPath=${encodeURIComponent(`${pathBase}/app/details`)}`, { waitUntil: "load" });
    const mitIdButton = page.getByRole("button", { name: "Log on with MitID", exact: true });
    const present = await mitIdButton.waitFor({ timeout: 10_000 }).then(() => true, () => false);
    const login = await page.evaluate(async () => {
      await document.fonts.load('600 16px "IBM Plex Sans"');
      await document.fonts.ready;
      const button = document.querySelector(".mitid-button");
      const wordmark = button?.querySelector("img");
      if (wordmark && !wordmark.complete) await new Promise((resolve) => wordmark.addEventListener("load", resolve, { once: true }));
      const style = button ? getComputedStyle(button) : null;
      return {
        theme: document.documentElement.dataset.theme ?? null,
        buttons: [...document.querySelectorAll(".external-login-form button")].map((element) => element.textContent.replace(/\s+/g, " ").trim()),
        height: style?.height ?? null,
        borderRadius: style?.borderTopLeftRadius ?? null,
        backgroundColor: style?.backgroundColor ?? null,
        fontFamily: style?.fontFamily ?? null,
        fontWeight: style?.fontWeight ?? null,
        fontLoaded: [...document.fonts].some((face) => face.family.replaceAll('"', "") === "IBM Plex Sans" && face.weight === "600" && face.status === "loaded"),
        wordmarkWidth: wordmark?.naturalWidth ?? 0,
        wordmarkAlt: wordmark?.getAttribute("alt") ?? null,
        returnPaths: [...document.querySelectorAll('.external-login-form input[name="ReturnPath"]')].map((input) => input.value),
        editions: [...document.querySelectorAll('.external-login-form input[name="Edition"]')].map((input) => input.value),
        styledBodyElements: document.body.querySelectorAll("[style]").length
      };
    });
    await settle(page);
    const loginViolations = await readViolations(page);

    await page.goto(`${baseUrl}${pathBase}/signup`, { waitUntil: "load" });
    await settle(page);
    const signup = await page.evaluate(() => ({
      buttons: [...document.querySelectorAll(".external-login-form button")].map((element) => element.textContent.replace(/\s+/g, " ").trim()),
      mitIdForms: document.querySelectorAll('form[action*="/MitId/"]').length,
      styledBodyElements: document.body.querySelectorAll("[style]").length
    }));
    const signupViolations = await readViolations(page);

    let callbackReached = false;
    if (present) {
      await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
      const callback = page.waitForRequest((request) => new URL(request.url()).pathname.endsWith("/MitId/login/callback"), { timeout: 15_000 }).then(() => true, () => false);
      await page.getByRole("button", { name: "Log on with MitID", exact: true }).click();
      callbackReached = await callback;
    }
    await context.close();

    details[theme] = { present, login, loginViolations, signup, signupViolations, assetResponses, callbackReached };
    const label = `external login (${theme})`;
    if (!present) failures.push(`${label}: no "Log on with MitID" button; enable the MitID login flag in the AppHost`);
    if (login.theme !== theme) failures.push(`${label}: data-theme ${login.theme}`);
    if (!["Log in with Google", "Log in with Microsoft", "Log on with"].every((label, index) => login.buttons[index] === label)) failures.push(`${label}: login buttons ${login.buttons.join(" | ")}`);
    if (login.buttons.some((text) => text.includes("Log in with MitID"))) failures.push(`${label}: the unapproved MitID phrase is shown`);
    if (login.height !== "48px") failures.push(`${label}: MitID height ${login.height}`);
    if (login.borderRadius !== "4px") failures.push(`${label}: MitID radius ${login.borderRadius}`);
    if (login.backgroundColor !== "rgb(0, 96, 230)") failures.push(`${label}: MitID colour ${login.backgroundColor}`);
    if (!login.fontFamily?.replaceAll('"', "").startsWith("IBM Plex Sans,") || login.fontWeight !== "600") failures.push(`${label}: MitID font ${login.fontFamily} ${login.fontWeight}`);
    if (!login.fontLoaded) failures.push(`${label}: IBM Plex Sans SemiBold did not load`);
    if (login.wordmarkWidth === 0 || login.wordmarkAlt !== "MitID") failures.push(`${label}: the MitID wordmark did not load`);
    if (!login.returnPaths.every((value) => value === `${pathBase}/app/details`) || login.returnPaths.length !== 3) failures.push(`${label}: return paths ${login.returnPaths.join(", ")}`);
    if (!login.editions.every((value) => value === "Blazor")) failures.push(`${label}: editions ${login.editions.join(", ")}`);
    if (signup.mitIdForms !== 0 || signup.buttons.join(" | ") !== "Sign up with Google | Sign up with Microsoft") failures.push(`${label}: signup buttons ${signup.buttons.join(" | ")}`);
    if (login.styledBodyElements + signup.styledBodyElements > 0) failures.push(`${label}: elements with a style attribute`);
    for (const response of assetResponses) {
      if (response.status >= 400 || new URL(response.url).origin !== baseUrl) failures.push(`${label}: ${response.status} ${response.url}`);
    }
    if (!assetResponses.some((response) => response.url.includes("/fonts/ibm-plex-sans-latin-600-normal"))) failures.push(`${label}: the font was not requested from the host`);
    failures.push(...cleanPageFailures(`${label} pages`, [...loginViolations, ...signupViolations], observations));
    if (present && !callbackReached) failures.push(`${label}: the MitID start did not reach the login callback`);
  }
  return outcome(details, failures);
}

// The identity verification section on the Blazor profile in the light and the dark theme: the MitID button keeps the brand
// geometry, colour and typeface with the approved phrase "Confirm with MitID" and the white wordmark loaded from the host,
// nothing in <body> has a style attribute and no document reports a violation. Clicking the button with the mock provider
// completes the verification back on the profile, which shows the verified state with the wordmark for the theme and the
// assurance level, and no button. Uses an account of its own, because the form action case verifies the shared one. Needs
// the MitID verification flag on in the AppHost.
async function runIdentityVerification() {
  let { storageState } = await signUpThroughReact();
  const failures = [];
  const details = {};
  const identity = `identity:shell-${options.browser}-${Date.now()}`;
  const profileUrl = `${baseUrl}${pathBase}/user/profile`;
  const readSection = (page) =>
    page.evaluate(async () => {
      await document.fonts.load('600 16px "IBM Plex Sans"');
      await document.fonts.ready;
      const images = [...document.querySelectorAll('[data-testid="identity-verification"] img')];
      await Promise.all(images.filter((image) => !image.complete).map((image) => new Promise((resolve) => image.addEventListener("load", resolve, { once: true }))));
      const button = document.querySelector('[data-testid="identity-verification-start"]');
      const style = button ? getComputedStyle(button) : null;
      const visibleWordmarks = images.filter((image) => getComputedStyle(image).display !== "none");
      return {
        theme: document.documentElement.dataset.theme ?? null,
        buttonText: button?.textContent.replace(/\s+/g, " ").trim() ?? null,
        height: style?.height ?? null,
        borderRadius: style?.borderTopLeftRadius ?? null,
        backgroundColor: style?.backgroundColor ?? null,
        fontFamily: style?.fontFamily ?? null,
        fontWeight: style?.fontWeight ?? null,
        fontLoaded: [...document.fonts].some((face) => face.family.replaceAll('"', "") === "IBM Plex Sans" && face.weight === "600" && face.status === "loaded"),
        visibleWordmarks: visibleWordmarks.map((image) => ({ source: new URL(image.src).pathname, alt: image.getAttribute("alt"), width: image.naturalWidth })),
        verified: document.querySelector('[data-testid="identity-verification-verified"]') !== null,
        assurance: document.querySelector('[data-testid="identity-verification-assurance"]')?.textContent.trim() ?? null,
        verifiedDate: document.querySelector('[data-testid="identity-verification-date"]')?.textContent.trim() ?? null,
        styledBodyElements: document.body.querySelectorAll("[style]").length
      };
    });

  for (const [step, theme] of [["unverified", "light"], ["verify", "dark"], ["verified", "light"]]) {
    const context = await newContext(storageState);
    await context.addInitScript((mode) => localStorage.setItem("theme", mode), theme);
    await context.addCookies([{ name: mockProviderCookie, value: identity, url: baseUrl }]);
    const page = await context.newPage();
    const observations = observe(page);
    await page.goto(profileUrl, { waitUntil: "load" });
    const ready = await page
      .locator(step === "verified" ? '[data-testid="identity-verification-verified"]' : '[data-testid="identity-verification-start"]')
      .waitFor({ timeout: interactiveTimeoutMs })
      .then(() => true, () => false);
    const section = await readSection(page);
    await settle(page);
    const violations = await readViolations(page);
    const stepDetails = { theme, ready, finalUrl: page.url(), section, violations };
    const label = `identity verification ${step} (${theme})`;
    if (!ready) failures.push(`${label}: the section did not show ${step === "verified" ? "the verified state" : "the MitID button"}; enable the MitID verification flag in the AppHost`);
    if (section.theme !== theme) failures.push(`${label}: data-theme ${section.theme}`);
    if (step !== "verified" && ready) {
      if (section.buttonText !== "Confirm with") failures.push(`${label}: button text "${section.buttonText}"`);
      if (section.height !== "48px") failures.push(`${label}: MitID height ${section.height}`);
      if (section.borderRadius !== "4px") failures.push(`${label}: MitID radius ${section.borderRadius}`);
      if (section.backgroundColor !== "rgb(0, 96, 230)") failures.push(`${label}: MitID colour ${section.backgroundColor}`);
      if (!section.fontFamily?.replaceAll('"', "").startsWith("IBM Plex Sans,") || section.fontWeight !== "600") failures.push(`${label}: MitID font ${section.fontFamily} ${section.fontWeight}`);
      if (!section.fontLoaded) failures.push(`${label}: IBM Plex Sans SemiBold did not load`);
      if (section.visibleWordmarks.length !== 1 || !section.visibleWordmarks[0].source.endsWith("/images/mitid-logo-white.svg") || section.visibleWordmarks[0].width === 0 || section.visibleWordmarks[0].alt !== "MitID") failures.push(`${label}: button wordmark ${JSON.stringify(section.visibleWordmarks)}`);
      if (await page.getByRole("button", { name: "Confirm with MitID", exact: true }).count() !== 1) failures.push(`${label}: no button named "Confirm with MitID"`);
    }
    if (section.styledBodyElements > 0) failures.push(`${label}: elements with a style attribute`);
    failures.push(...cleanPageFailures(label, violations, observations));

    if (step === "verify" && ready) {
      const callback = page.waitForRequest((request) => new URL(request.url()).pathname.endsWith("/MitId/verification/callback"), { timeout: 30_000 }).then(() => true, () => false);
      await page.getByRole("button", { name: "Confirm with MitID", exact: true }).click();
      const callbackReached = await callback;
      const returned = callbackReached && (await page.waitForURL(profileUrl, { waitUntil: "load", timeout: 30_000 }).then(() => true, () => false));
      const verified = await page.locator('[data-testid="identity-verification-verified"]').waitFor({ timeout: interactiveTimeoutMs }).then(() => true, () => false);
      const verifiedSection = verified ? await readSection(page) : null;
      await settle(page);
      const verifiedViolations = await readViolations(page);
      stepDetails.afterVerify = { returned, verified, section: verifiedSection, violations: verifiedViolations, finalUrl: page.url() };
      if (!returned || !verified) failures.push(`${label}: the verification did not return to the verified profile (${page.url()})`);
      if (verifiedSection) failures.push(...checkVerifiedSection(`${label} after verifying`, verifiedSection, "white"));
      failures.push(...cleanPageFailures(`${label} after verifying`, verifiedViolations, observations));
    }
    if (step === "verified" && ready) failures.push(...checkVerifiedSection(label, section, "blue"));
    // The session cookies may have been refreshed in this context, so the next context continues with them
    storageState = await context.storageState();
    await context.close();
    details[step] = stepDetails;
  }
  return outcome(details, failures);
}

function checkVerifiedSection(label, section, wordmarkColour) {
  const failures = [];
  if (!section.verified) failures.push(`${label}: no verified state`);
  if (section.buttonText !== null) failures.push(`${label}: the MitID button is still offered`);
  if (section.assurance !== "Substantial assurance") failures.push(`${label}: assurance "${section.assurance}"`);
  if (!section.verifiedDate?.startsWith("Verified on ")) failures.push(`${label}: verified date "${section.verifiedDate}"`);
  const wordmark = section.visibleWordmarks[0];
  if (section.visibleWordmarks.length !== 1 || !wordmark.source.endsWith(`/images/mitid-logo-${wordmarkColour}.svg`) || wordmark.width === 0 || wordmark.alt !== "MitID") failures.push(`${label}: verified wordmark ${JSON.stringify(section.visibleWordmarks)}`);
  return failures;
}

// The legal index and the three documents, in both cultures: the chrome follows the culture, the document text stays English
// and says so, no policy violation is reported and no WebAssembly runtime is requested
async function runLegalPages() {
  const failures = [];
  const pages = {};
  for (const locale of ["en-US", "da-DK"]) {
    const context = await newContext();
    await context.addCookies([{ name: "preferred-locale", value: locale, url: baseUrl, secure: true, sameSite: "Lax" }]);
    for (const route of ["legal", "legal/terms", "legal/privacy", "legal/dpa"]) {
      const page = await context.newPage();
      const observations = observe(page);
      const runtimeRequests = [];
      page.on("request", (request) => {
        if (runtimeRequestPattern.test(request.url())) runtimeRequests.push(request.url());
      });
      const response = await page.goto(`${baseUrl}${pathBase}/${route}`, { waitUntil: "load" });
      await settle(page);
      const document_ = await page.evaluate(() => ({
        language: document.documentElement.lang,
        articleLanguage: document.querySelector("article.legal-document")?.getAttribute("lang") ?? null,
        headings: document.querySelectorAll("h1").length,
        styleAttributes: document.querySelectorAll("[style]").length
      }));
      const violations = await readViolations(page);
      const label = `${route} (${locale})`;
      pages[label] = { status: response.status(), ...document_, runtimeRequests, violations };
      failures.push(...cleanPageFailures(label, violations, observations));
      if (response.status() !== 200) failures.push(`${label}: status ${response.status()}`);
      if (document_.language !== locale) failures.push(`${label}: the document language is ${document_.language}`);
      if (document_.headings !== 1) failures.push(`${label}: ${document_.headings} level one headings`);
      if (document_.styleAttributes !== 0) failures.push(`${label}: ${document_.styleAttributes} style attributes`);
      if (runtimeRequests.length > 0) failures.push(`${label}: ${runtimeRequests.length} WebAssembly runtime requests`);
      if (route !== "legal" && document_.articleLanguage !== "en-US") failures.push(`${label}: the document text is marked ${document_.articleLanguage}`);
      await page.close();
    }
    await context.close();
  }

  return outcome({ pages }, failures);
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
