// Spike code (Blazor edition, stage B2): Playwright harness for the render mode split. Not production code.
// Run with node (never the Playwright test runner), one browser at a time:
//   node blazor/spike/b2-render-split/journey.mjs payload --browser chromium
//   node blazor/spike/b2-render-split/journey.mjs journey --browser chromium
// payload: cold loads of every public page, recording every request including background downloads after load.
// journey: signup, login, one-time password, antiforgery, navigation, session expiry and revocation, tenant switch and
// logout through the gateway, all against the Blazor pages. Writes a JSON result file and prints it.

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";

const repositoryRoot = path.resolve(import.meta.dirname, "../../..");
const playwright = createRequire(path.join(repositoryRoot, "application/"))("playwright");
const basePort = readFileSync(path.join(repositoryRoot, ".workspace/port.txt"), "utf8").trim();
const origin = `https://app.dev.localhost:${basePort}`;
const blazor = `${origin}/blazor`;
const verificationCode = "UNLOCK";
const wrongCode = "WRONG1";
const backgroundWaitMs = 5_000;

const mode = process.argv[2];
const browserName = process.argv.includes("--browser") ? process.argv[process.argv.indexOf("--browser") + 1] : "chromium";
const resultsFolder = path.join(repositoryRoot, ".workspace/experiment-06-blazor/b2-results");
mkdirSync(resultsFolder, { recursive: true });

var RUNTIME_PATTERN = /\/_framework\/(dotnet[^/]*\.js|[^/]*\.wasm|[^/]*\.dat|blazor\.boot\.json)|\.wasm(\?|$)/;
const browser = await playwright[browserName].launch();
const result = { mode, browser: browserName, browserVersion: browser.version(), origin, startedAt: new Date().toISOString() };

const steps = [];
try {
  if (mode === "payload") result.payload = await measurePublicPages();
  else if (mode === "journey") result.journey = await runJourney();
  else throw new Error(`Unknown mode '${mode}'.`);
} catch (error) {
  result.error = String(error.stack ?? error).slice(0, 2_000);
  result.partialSteps = steps;
  process.exitCode = 1;
} finally {
  await browser.close();
}

result.finishedAt = new Date().toISOString();
const resultFile = path.join(resultsFolder, `${mode}-${browserName}.json`);
writeFileSync(resultFile, JSON.stringify(result, null, 2));
console.log(JSON.stringify(result, null, 2));
console.log(`\nResult file: ${resultFile}`);

async function newContext(locale = "en-US", storageState = undefined) {
  const context = await browser.newContext({ ignoreHTTPSErrors: true, locale, storageState });
  await context.addInitScript(() => {
    window.__b2Violations = [];
    document.addEventListener("securitypolicyviolation", (event) =>
      window.__b2Violations.push({ directive: event.effectiveDirective, blockedURI: event.blockedURI })
    );
  });
  return context;
}

function observe(page) {
  const log = { requests: [], console: [], pageErrors: [], websockets: [], posts: [] };
  page.on("console", (message) => {
    if (message.type() === "error" || message.type() === "warning") log.console.push({ type: message.type(), text: message.text().slice(0, 400) });
  });
  page.on("pageerror", (error) => log.pageErrors.push(String(error.message).slice(0, 400)));
  page.on("websocket", (socket) => log.websockets.push(socket.url()));
  page.on("requestfinished", async (request) => {
    const response = await request.response();
    const sizes = await request.sizes().catch(() => null);
    log.requests.push({
      url: request.url(),
      method: request.method(),
      type: request.resourceType(),
      status: response?.status() ?? null,
      encoding: response ? ((await response.allHeaders().catch(() => ({})))["content-encoding"] ?? null) : null,
      transferBytes: sizes ? sizes.responseBodySize + sizes.responseHeadersSize : null,
      bodyBytes: sizes?.responseBodySize ?? null
    });
  });
  page.on("response", async (response) => {
    if (response.request().method() !== "POST") return;
    const headers = await response.allHeaders().catch(() => ({}));
    log.posts.push({
      url: response.url(),
      status: response.status(),
      location: headers.location ?? null,
      setCookieNames: (headers["set-cookie"] ?? "").split("\n").filter(Boolean).map((cookie) => cookie.split("=")[0]),
      leakedTokenHeaders: Object.keys(headers).filter((name) => name === "x-access-token" || name === "x-refresh-token")
    });
  });
  return log;
}

function documentId(page) {
  return page.evaluate(() => performance.timeOrigin);
}

function runtimeRequests(log) {
  return log.requests.filter((request) => RUNTIME_PATTERN.test(request.url)).map((request) => request.url);
}

async function text(page, testId) {
  return (await page.locator(`[data-testid="${testId}"]`).first().textContent())?.trim() ?? null;
}

function waitInteractive(page) {
  return page.waitForFunction(() => typeof window.__b1InteractiveAt === "number", null, { timeout: 60_000 });
}

async function measurePublicPages() {
  const pages = ["/", "/login", "/signup", "/login/verify?id=eml_x&email=a%40example.com", "/signup/verify?id=eml_x&email=a%40example.com", "/legal/terms"];
  const measurements = [];
  for (const pagePath of pages) {
    const context = await newContext();
    const page = await context.newPage();
    const log = observe(page);
    const response = await page.goto(`${blazor}${pagePath}`, { waitUntil: "load" });
    await page.waitForTimeout(backgroundWaitMs);
    const state = await page.evaluate(() => ({
      surface: document.documentElement.dataset.surface,
      preloadLinks: document.querySelectorAll('link[rel="preload"], link[rel="modulepreload"]').length,
      resourceEntries: performance.getEntriesByType("resource").map((entry) => entry.name),
      serviceWorker: navigator.serviceWorker?.controller !== null && navigator.serviceWorker?.controller !== undefined,
      violations: window.__b2Violations
    }));
    const runtimeFromTimeline = state.resourceEntries.filter((url) => RUNTIME_PATTERN.test(url));
    measurements.push({
      path: pagePath,
      status: response.status(),
      surface: state.surface,
      preloadLinks: state.preloadLinks,
      requestCount: log.requests.length,
      transferBytes: log.requests.reduce((sum, request) => sum + (request.transferBytes ?? 0), 0),
      bodyBytes: log.requests.reduce((sum, request) => sum + (request.bodyBytes ?? 0), 0),
      runtimeRequests: [...new Set([...runtimeRequests(log), ...runtimeFromTimeline])],
      websockets: log.websockets,
      serviceWorkerControlled: state.serviceWorker,
      requests: log.requests.map(({ url, type, status, encoding, bodyBytes }) => ({ url: url.replace(origin, ""), type, status, encoding, bodyBytes })),
      console: log.console,
      pageErrors: log.pageErrors,
      violations: state.violations
    });
    await context.close();
  }
  return measurements;
}

async function signUp(page, email, steps) {
  await page.goto(`${blazor}/`);
  const landingDocument = await documentId(page);
  await page.locator('[data-testid="nav-signup"]').click();
  await page.waitForURL(`${blazor}/signup`);
  steps.push({ step: "landing to signup link", enhancedNavigation: (await documentId(page)) === landingDocument });

  const postsBefore = page.__log.posts.length;
  await page.locator('[data-testid="email"]').fill("not-an-email");
  await page.locator('[data-testid="submit"]').click();
  await page.locator(".validation-message").first().waitFor({ timeout: 10_000 });
  steps.push({
    step: "signup start rejected by validation",
    message: await page.locator(".validation-message").first().textContent(),
    postSent: page.__log.posts.length > postsBefore,
    url: page.url()
  });

  await page.locator('[data-testid="email"]').fill(email);
  const startDocument = await documentId(page);
  await page.locator('[data-testid="submit"]').click();
  await page.waitForURL(/\/blazor\/signup\/verify\?/);
  steps.push({ step: "signup start accepted", url: page.url().replace(origin, ""), enhancedForm: (await documentId(page)) === startDocument });

  await page.reload();
  steps.push({ step: "refresh verify page", verifyText: await text(page, "verify-email") });

  await page.locator('[data-testid="code"]').fill(wrongCode);
  await page.locator('[data-testid="submit"]').click();
  await page.locator('[data-testid="form-error"]').filter({ hasText: /./ }).waitFor({ timeout: 10_000 });
  steps.push({ step: "signup code rejected by account API", error: await text(page, "form-error"), url: page.url().replace(origin, ""), post: page.__log.posts.at(-1) });

  const verifyDocument = await documentId(page);
  await page.locator('[data-testid="code"]').fill(verificationCode);
  await page.locator('[data-testid="submit"]').click();
  await page.waitForURL(`${blazor}/app`);
  await waitInteractive(page);
  steps.push({
    step: "signup code accepted, boundary crossed",
    fullDocumentNavigation: (await documentId(page)) !== verifyDocument,
    verifyPost: page.__log.posts.at(-1),
    bootstrapSource: await text(page, "bootstrap-source"),
    bootstrapEmail: await text(page, "bootstrap-email")
  });
}

async function login(page, email, steps, label) {
  await page.goto(`${blazor}/app`);
  await page.waitForURL(/\/blazor\/login\?returnPath=/);
  steps.push({ step: `${label}: anonymous deep link to /blazor/app`, url: page.url().replace(origin, "") });

  await page.locator('[data-testid="email"]').fill(email);
  await page.locator('[data-testid="submit"]').click();
  await page.waitForURL(/\/blazor\/login\/verify\?/);
  await page.locator('[data-testid="code"]').fill(wrongCode);
  await page.locator('[data-testid="submit"]').click();
  await page.locator('[data-testid="form-error"]').filter({ hasText: /./ }).waitFor({ timeout: 10_000 });
  steps.push({ step: `${label}: login code rejected`, error: await text(page, "form-error"), post: page.__log.posts.at(-1) });

  await page.locator('[data-testid="code"]').fill(verificationCode);
  await page.locator('[data-testid="submit"]').click();
  await page.waitForURL(`${blazor}/app`);
  await waitInteractive(page);
  steps.push({ step: `${label}: login accepted, returned to return path`, url: page.url().replace(origin, ""), post: page.__log.posts.at(-1) });
}

async function openPage(context) {
  const page = await context.newPage();
  page.__log = observe(page);
  return page;
}

async function bootstrapFromPage(page) {
  return page.evaluate(async () => {
    const response = await fetch("/blazor/api/bootstrap", { credentials: "same-origin" });
    const body = await response.json();
    return { status: response.status, isAuthenticated: body.isAuthenticated, email: body.user?.email ?? null, tenantId: body.user?.tenantId ?? null, locale: body.locale, flags: body.systemFeatureFlags, runtimeKeys: Object.keys(body.runtimeConfiguration) };
  });
}

// Node cannot resolve app.dev.localhost, so API calls run as fetch inside a page on the gateway origin
async function inPageFetch(page, url, init) {
  return page.evaluate(
    async ({ url, init }) => {
      const response = await fetch(url, { credentials: "same-origin", redirect: "manual", ...init });
      return { status: response.status, type: response.type, body: (await response.text()).slice(0, 300) };
    },
    { url, init }
  );
}

async function apiWithAntiforgery(page, method, url, data) {
  const bootstrap = await page.evaluate(async () => (await fetch("/blazor/api/bootstrap")).json());
  return inPageFetch(page, url, { method, body: JSON.stringify(data), headers: { "content-type": "application/json", "x-xsrf-token": bootstrap.antiforgeryToken } });
}

async function postForm(page, url, fields) {
  return inPageFetch(page, url, { method: "POST", body: new URLSearchParams(fields).toString(), headers: { "content-type": "application/x-www-form-urlencoded" } });
}

async function runJourney() {
  const stamp = Date.now();
  const emailA = `b2-a-${browserName}-${stamp}@example.com`;
  const emailB = `b2-b-${browserName}-${stamp}@example.com`;

  // Antiforgery on the host's form endpoint and on the account API
  {
    const context = await newContext();
    const page = await openPage(context);
    await page.goto(`${blazor}/login`);
    const token = await page.locator('input[name="__RequestVerificationToken"]').first().getAttribute("value");
    const withoutToken = await postForm(page, "/blazor/login", { _handler: "login-start", "Input.Email": emailA });
    const withToken = await postForm(page, "/blazor/login", { _handler: "login-start", "Input.Email": emailA, __RequestVerificationToken: token });
    const apiWithoutToken = await inPageFetch(page, "/api/account/authentication/email/login/start", {
      method: "POST",
      body: JSON.stringify({ email: emailA }),
      headers: { "content-type": "application/json" }
    });
    const otherContext = await newContext();
    const otherPage = await openPage(otherContext);
    await otherPage.goto(`${blazor}/login`);
    const tokenFromAnotherBrowser = await postForm(otherPage, "/blazor/login", { _handler: "login-start", "Input.Email": emailA, __RequestVerificationToken: token });
    steps.push({
      step: "antiforgery",
      hostFormWithoutToken: withoutToken,
      hostFormWithToken: { status: withToken.status, type: withToken.type },
      hostFormTokenFromAnotherBrowser: tokenFromAnotherBrowser,
      accountApiDirectWithoutToken: apiWithoutToken
    });
    await otherContext.close();
    await context.close();
  }

  // User B signs up first and prepares a second tenant for user A
  {
    const context = await newContext();
    const page = await openPage(context);
    const bSteps = [];
    await signUp(page, emailB, bSteps);
    steps.push({ step: "user B signup", details: bSteps.map(({ step, fullDocumentNavigation, bootstrapEmail }) => ({ step, fullDocumentNavigation, bootstrapEmail })) });
    steps.push({ step: "user B names tenant", ...(await apiWithAntiforgery(page, "PUT", "/api/account/tenants/current", { name: "B2 Tenant B" })) });
    await context.close();
  }

  // User A: signup, navigation inside and across the boundary
  const contextA = await newContext();
  const pageA = await openPage(contextA);
  await signUp(pageA, emailA, steps);
  const cookies = (await contextA.cookies()).map((cookie) => cookie.name);
  steps.push({ step: "cookies after signup", cookies });

  const appDocument = await documentId(pageA);
  const runtimeBefore = runtimeRequests(pageA.__log).length;
  await pageA.locator('[data-testid="nav-details"]').click();
  await pageA.waitForURL(`${blazor}/app/details`);
  await pageA.locator("h1", { hasText: "Account details" }).waitFor();
  await pageA.locator('[data-testid="render-mode"]', { hasText: "True" }).waitFor();
  steps.push({
    step: "app to details link",
    enhancedNavigation: (await documentId(pageA)) === appDocument,
    newRuntimeRequests: runtimeRequests(pageA.__log).length - runtimeBefore,
    bootstrapEmail: await text(pageA, "bootstrap-email")
  });

  await pageA.goBack();
  await pageA.waitForURL(`${blazor}/app`);
  await pageA.locator("h1", { hasText: "Your workspace" }).waitFor();
  steps.push({ step: "back to app", sameDocument: (await documentId(pageA)) === appDocument, interactive: await text(pageA, "render-mode") });

  await pageA.goto(`${blazor}/app/details`);
  await waitInteractive(pageA);
  steps.push({ step: "refresh-style deep link to /blazor/app/details", interactive: await text(pageA, "render-mode"), email: await text(pageA, "bootstrap-email") });

  const detailsDocument = await documentId(pageA);
  const runtimeBeforeTerms = pageA.__log.requests.length;
  await pageA.locator('[data-testid="nav-terms"]').click();
  await pageA.waitForURL(`${blazor}/legal/terms`);
  await pageA.waitForTimeout(backgroundWaitMs);
  steps.push({
    step: "app to public terms link",
    fullDocumentNavigation: (await documentId(pageA)) !== detailsDocument,
    runtimeRequestsOnTermsDocument: pageA.__log.requests.slice(runtimeBeforeTerms).filter((request) => RUNTIME_PATTERN.test(request.url)).length,
    surface: await pageA.evaluate(() => document.documentElement.dataset.surface)
  });

  await pageA.goBack();
  await pageA.waitForURL(`${blazor}/app/details`);
  await waitInteractive(pageA);
  steps.push({ step: "back from terms to details", interactive: await text(pageA, "render-mode") });

  await pageA.locator('[data-testid="nav-app"]').click();
  await pageA.waitForURL(`${blazor}/app`);
  await pageA.locator('[data-testid="render-mode"]', { hasText: "True" }).waitFor();
  steps.push({ step: "user A bootstrap in app", bootstrap: await bootstrapFromPage(pageA) });

  // Session expiry: the access token cookie disappears, the refresh token is still valid
  await contextA.clearCookies({ name: "__Host-access-token" });
  await pageA.locator('[data-testid="reload-bootstrap"]').click();
  await pageA.locator('[data-testid="status"]', { hasText: "Bootstrap reloaded" }).waitFor();
  steps.push({
    step: "access token expired, refresh token valid",
    authenticated: await text(pageA, "bootstrap-authenticated"),
    url: pageA.url().replace(origin, ""),
    cookies: (await contextA.cookies()).map((cookie) => cookie.name)
  });

  // Session revoked elsewhere: the same session logs out from a copy of the cookies
  const copy = await newContext("en-US", await contextA.storageState());
  const copyPage = await openPage(copy);
  await copyPage.goto(`${blazor}/legal/terms`);
  const revoke = await apiWithAntiforgery(copyPage, "POST", "/api/account/authentication/logout", {});
  await copy.close();
  await pageA.locator('[data-testid="reload-bootstrap"]').click();
  await pageA.waitForTimeout(1_000);
  const beforeExpiry = { authenticated: await text(pageA, "bootstrap-authenticated"), url: pageA.url().replace(origin, "") };
  await contextA.clearCookies({ name: "__Host-access-token" });
  await pageA.locator('[data-testid="reload-bootstrap"]').click();
  await pageA.waitForURL(/\/blazor\/login\?returnPath=/, { timeout: 15_000 });
  steps.push({
    step: "session revoked elsewhere",
    logoutStatus: revoke.status,
    withUnexpiredAccessToken: beforeExpiry,
    afterAccessTokenExpiry: { url: pageA.url().replace(origin, ""), cookies: (await contextA.cookies()).map((cookie) => cookie.name) }
  });
  await contextA.close();

  // User B invites user A, user A logs in and switches tenant
  {
    const context = await newContext();
    const page = await openPage(context);
    await login(page, emailB, steps, "user B");
    steps.push({ step: "user B invites user A", ...(await apiWithAntiforgery(page, "POST", "/api/account/users/invite", { email: emailA })) });
    await context.close();
  }

  const contextSwitch = await newContext("da-DK");
  const pageSwitch = await openPage(contextSwitch);
  await login(pageSwitch, emailA, steps, "user A");
  await pageSwitch.locator('[data-testid="switch-tenant"]').nth(1).waitFor();
  const beforeSwitch = await bootstrapFromPage(pageSwitch);
  const switchDocument = await documentId(pageSwitch);
  await pageSwitch.locator('[data-testid="switch-tenant"]:not([disabled])').click();
  await pageSwitch.locator('[data-testid="status"]', { hasText: "Switched" }).waitFor({ timeout: 15_000 });
  const afterSwitch = await bootstrapFromPage(pageSwitch);
  const switchPost = pageSwitch.__log.posts.at(-1);
  await pageSwitch.reload();
  await waitInteractive(pageSwitch);
  steps.push({
    step: "tenant switch",
    before: beforeSwitch,
    after: afterSwitch,
    sameDocument: (await pageSwitch.evaluate(() => performance.timeOrigin)) === switchDocument,
    switchPost,
    componentTenantAfterReload: await text(pageSwitch, "bootstrap-tenant-id")
  });

  const logoutDocument = await documentId(pageSwitch);
  await pageSwitch.locator('[data-testid="logout"]').click();
  await pageSwitch.waitForURL(/\/blazor\/login\?loggedOut=/);
  const afterLogout = await bootstrapFromPage(pageSwitch);
  await pageSwitch.goto(`${blazor}/app`);
  steps.push({
    step: "logout",
    fullDocumentNavigation: (await pageSwitch.evaluate(() => performance.timeOrigin)) !== logoutDocument,
    bootstrapAfterLogout: afterLogout,
    deepLinkAfterLogout: pageSwitch.url().replace(origin, ""),
    cookies: (await contextSwitch.cookies()).map((cookie) => cookie.name),
    logoutPost: pageSwitch.__log.posts.find((post) => post.url.endsWith("/logout")) ?? null
  });

  const diagnostics = [pageA, pageSwitch].map((page) => ({ console: page.__log.console, pageErrors: page.__log.pageErrors, websockets: page.__log.websockets }));
  await contextSwitch.close();
  return { emailA, emailB, steps, diagnostics };
}
