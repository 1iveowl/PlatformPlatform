// Authentication state of the Blazor edition through the gateway, against the running stack, in one browser.
//
// 1. Expiry: with the access cookie gone and a valid refresh cookie, the gateway refreshes the session, the account API
//    bootstrap is authenticated and the access cookie is set again.
// 2. Revocation: a session revoked from another browser keeps working only until its access token expires (the real
//    token lifetime is waited out, not simulated). The next bootstrap through the gateway is a 401 that keeps its
//    x-unauthorized-reason, the WebAssembly client lands on the error page with a full document load, and the browser is
//    then anonymous.
// 3. Tenant switch: the client posts the switch, reads bootstrap again and loads the authenticated home as a new
//    document; the bootstrap tenant changes and survives a reload.
// 4. Logout: the client posts the logout and loads the login page as a new document; only the antiforgery cookie is left.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started.
// Run: dotnet run --project developer-cli -- blazor-harness authentication-state --browser chromium

import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { baseUrl, launchBrowser, newContext, parseArguments, pathBase, readOneTimePassword, resultsFolder, signUpThroughBlazor } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
const accessCookieName = "__Host-access-token";
const refreshCookieName = "__Host-refresh-token";
const antiforgeryCookieName = "__Host-xsrf-token";
const bootstrapPath = "/api/account/bootstrap";
const interactiveTimeoutMs = 60_000;
// The account API accepts a token up to 5 seconds past its expiry; the margin covers that and clock differences
const expiryMarginMs = 15_000;

const browser = await launchBrowser(options.browser);
const results = [];
const stamp = `${options.browser}-${Date.now()}`;

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

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

// Node cannot resolve app.dev.localhost, so API calls run as fetch inside a page on the gateway origin
function inPageFetch(page, url, init = {}) {
  return page.evaluate(
    async ({ url, init }) => {
      const response = await fetch(url, { credentials: "same-origin", redirect: "manual", ...init });
      const text = await response.text();
      let json = null;
      try {
        json = JSON.parse(text);
      } catch {}
      return { status: response.status, reason: response.headers.get("x-unauthorized-reason"), cacheControl: response.headers.get("cache-control"), text, json };
    },
    { url, init }
  );
}

async function apiWithAntiforgery(page, method, url, data) {
  const bootstrap = await inPageFetch(page, bootstrapPath);
  return inPageFetch(page, url, {
    method,
    body: data === undefined ? undefined : JSON.stringify(data),
    headers: { "content-type": "application/json", "x-xsrf-token": bootstrap.json.antiforgeryToken }
  });
}

async function cookieNames(context) {
  return (await context.cookies(baseUrl)).map((cookie) => cookie.name).sort();
}

async function waitInteractive(page) {
  await page.locator('[data-testid="render-mode"]', { hasText: "Interactive: True" }).waitFor({ timeout: interactiveTimeoutMs });
}

function documentOrigin(page) {
  return page.evaluate(() => performance.timeOrigin);
}

function uniqueEmail(label) {
  return `auth-state-${label}-${stamp}@example.com`;
}

async function logIn(context, email) {
  const page = await context.newPage();
  await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
  await page.locator('[data-testid="email"]').fill(email);
  const sentAfter = Date.now();
  await page.locator('[data-testid="submit"]').click();
  await page.waitForURL(/\/blazor\/login\/verify\?/);
  await page.locator('[data-testid="code"]').fill(await readOneTimePassword(email, sentAfter));
  await page.locator('[data-testid="submit"]').click();
  await page.waitForURL(`${baseUrl}${pathBase}/app`);
  await waitInteractive(page);
  return page;
}

async function accessTokenExpiresAt(context) {
  const cookie = (await context.cookies(baseUrl)).find((candidate) => candidate.name === accessCookieName);
  assert(cookie !== undefined, "No access cookie.");
  const payload = JSON.parse(Buffer.from(cookie.value.split(".")[1], "base64url").toString("utf8"));
  return payload.exp * 1000;
}

const userA = uniqueEmail("a");
const userB = uniqueEmail("b");
const signedUpA = await signUpThroughBlazor(browser, options.browser, userA);
const signedUpB = await signUpThroughBlazor(browser, options.browser, userB);

// Revocation is started first so the access-token lifetime runs while the other journeys execute
const revocation = {};
await check("session revoked from another browser before its access token expires", async () => {
  revocation.context = await newContext(browser, options.browser, signedUpA.storageState);
  revocation.page = await revocation.context.newPage();
  await revocation.page.goto(`${baseUrl}${pathBase}/app`, { waitUntil: "load" });
  await waitInteractive(revocation.page);
  const revokedSessionBootstrap = await inPageFetch(revocation.page, bootstrapPath);
  assert(revokedSessionBootstrap.json?.isAuthenticated === true, "The session to revoke is not authenticated.");
  revocation.expiresAt = await accessTokenExpiresAt(revocation.context);

  const otherContext = await newContext(browser, options.browser);
  const otherPage = await logIn(otherContext, userA);
  const sessions = await inPageFetch(otherPage, "/api/account/authentication/sessions");
  const target = sessions.json.sessions.find((session) => !session.isCurrent);
  assert(target !== undefined, `No other session to revoke among ${sessions.json.sessions.length}.`);
  const revoke = await apiWithAntiforgery(otherPage, "DELETE", `/api/account/authentication/sessions/${target.id}`);
  assert(revoke.status === 200, `Revoking returned ${revoke.status}.`);
  await otherContext.close();

  const stillValid = await inPageFetch(revocation.page, bootstrapPath);
  assert(stillValid.status === 200 && stillValid.json.isAuthenticated === true, "The revoked session stopped working before its access token expired.");
  return { revokedSession: target.id, secondsUntilAccessTokenExpiry: Math.round((revocation.expiresAt - Date.now()) / 1000) };
});

// A fresh session of user B: user A's sign-up session is the one the revocation journey revokes, and refreshing a session
// rotates its refresh token, which would make user B's stored sign-up state a replay later in the run
await check("access cookie removed with a valid refresh cookie gives an authenticated bootstrap and the cookie back", async () => {
  const context = await newContext(browser, options.browser);
  const page = await logIn(context, userB);
  await context.clearCookies({ name: accessCookieName });
  assert(!(await cookieNames(context)).includes(accessCookieName), "The access cookie was not removed.");

  const bootstrap = await inPageFetch(page, bootstrapPath);
  const namesAfter = await cookieNames(context);
  await context.close();
  assert(bootstrap.status === 200, `Bootstrap returned ${bootstrap.status}.`);
  assert(bootstrap.json.isAuthenticated === true && bootstrap.json.user.email === userB, "Bootstrap is not authenticated as the user.");
  assert(bootstrap.cacheControl?.includes("no-store"), `Bootstrap Cache-Control is ${bootstrap.cacheControl}.`);
  assert(!/accessToken|refreshToken|sessionId/.test(bootstrap.text), "Bootstrap body names a credential.");
  assert(namesAfter.includes(accessCookieName) && namesAfter.includes(refreshCookieName), `Cookies after refresh: ${namesAfter.join(", ")}.`);
  return { cookies: namesAfter };
});

const tenantSwitch = {};
await check("tenant switch changes the bootstrap tenant with a new document and survives a reload", async () => {
  const contextB = await newContext(browser, options.browser, signedUpB.storageState);
  const pageB = await contextB.newPage();
  await pageB.goto(`${baseUrl}${pathBase}/app`, { waitUntil: "load" });
  const rename = await apiWithAntiforgery(pageB, "PUT", "/api/account/tenants/current", { name: `Tenant B ${Date.now() % 100_000}` });
  assert(rename.status < 300, `Naming tenant B returned ${rename.status}: ${rename.text.slice(0, 200)}`);
  const invite = await apiWithAntiforgery(pageB, "POST", "/api/account/users/invite", { email: userA });
  assert(invite.status < 300, `Inviting user A returned ${invite.status}: ${invite.text.slice(0, 200)}`);
  await contextB.close();

  tenantSwitch.context = await newContext(browser, options.browser);
  const page = await logIn(tenantSwitch.context, userA);
  tenantSwitch.page = page;
  await page.locator('[data-testid="switch-tenant"]').nth(1).waitFor({ timeout: interactiveTimeoutMs });
  const before = (await inPageFetch(page, bootstrapPath)).json.user;
  const target = page.locator('[data-testid="switch-tenant"]:not([disabled])').first();
  const targetTenantId = await target.getAttribute("data-tenant-id");
  const documentBefore = await documentOrigin(page);

  await target.click();
  await page.waitForFunction((origin) => performance.timeOrigin !== origin, documentBefore, { timeout: interactiveTimeoutMs });
  await page.waitForURL(`${baseUrl}${pathBase}/app`);
  await waitInteractive(page);
  const after = (await inPageFetch(page, bootstrapPath)).json.user;
  assert(after.tenantId === targetTenantId && after.tenantId !== before.tenantId, `Tenant before ${before.tenantId}, after ${after.tenantId}, target ${targetTenantId}.`);

  await page.reload({ waitUntil: "load" });
  await waitInteractive(page);
  const shownAfterReload = await page.locator('[data-testid="bootstrap-tenant-id"]').textContent();
  const afterReload = (await inPageFetch(page, bootstrapPath)).json.user;
  assert(afterReload.tenantId === targetTenantId && shownAfterReload === targetTenantId, `After reload the tenant is ${afterReload.tenantId}, shown ${shownAfterReload}.`);
  return { from: before.tenantId, to: after.tenantId, fullDocumentNavigation: true };
});

await check("logout loads the login page as a new document and leaves only the antiforgery cookie", async () => {
  assert(tenantSwitch.page !== undefined, "The tenant switch journey did not leave a signed-in page.");
  const page = tenantSwitch.page;
  const documentBefore = await documentOrigin(page);
  await page.locator('[data-testid="logout"]').click();
  await page.waitForURL(`${baseUrl}${pathBase}/login`, { timeout: interactiveTimeoutMs });
  const fullDocumentNavigation = (await documentOrigin(page)) !== documentBefore;
  const names = await cookieNames(tenantSwitch.context);
  const bootstrap = await inPageFetch(page, bootstrapPath);
  await tenantSwitch.context.close();
  assert(fullDocumentNavigation, "Logout did not load a new document.");
  assert(names.length === 1 && names[0] === antiforgeryCookieName, `Cookies after logout: ${names.join(", ")}.`);
  assert(bootstrap.status === 200 && bootstrap.json.isAuthenticated === false, `Bootstrap after logout: ${bootstrap.status} ${bootstrap.json?.isAuthenticated}.`);
  return { cookies: names };
});

await check("revoked session after access token expiry: 401 with reason, error page, then anonymous", async () => {
  assert(revocation.page !== undefined, "The revocation journey did not start.");
  const page = revocation.page;
  const waitMs = Math.max(0, revocation.expiresAt + expiryMarginMs - Date.now());
  console.log(`Waiting ${Math.round(waitMs / 1000)} s for the revoked session's access token to expire`);
  await new Promise((resolve) => setTimeout(resolve, waitMs));

  const documentBefore = await documentOrigin(page);
  const bootstrapResponse = page.waitForResponse((response) => new URL(response.url()).pathname === bootstrapPath);
  await page.locator('[data-testid="reload-bootstrap"]').click();
  const response = await bootstrapResponse;
  const status = response.status();
  const reason = (await response.allHeaders())["x-unauthorized-reason"] ?? null;
  await page.waitForURL(/\/blazor\/error\?error=/, { timeout: interactiveTimeoutMs });
  const landedOn = new URL(page.url());
  const fullDocumentNavigation = (await documentOrigin(page)) !== documentBefore;
  const message = await page.locator('[data-testid="error-message"]').getAttribute("data-error-code");
  const names = await cookieNames(revocation.context);
  const anonymous = await inPageFetch(page, bootstrapPath);
  await page.goto(`${baseUrl}${pathBase}/app`, { waitUntil: "load" });
  const deepLink = new URL(page.url());
  await revocation.context.close();

  assert(status === 401, `Bootstrap after expiry returned ${status}, not 401.`);
  assert(reason === "Revoked", `x-unauthorized-reason was ${reason}.`);
  assert(landedOn.pathname === `${pathBase}/error` && landedOn.searchParams.get("error") === "session_revoked", `Landed on ${landedOn.pathname}${landedOn.search}.`);
  assert(fullDocumentNavigation, "The client did not leave with a full document load.");
  assert(message === "session_revoked", `The error page shows ${message}.`);
  assert(!names.includes(accessCookieName) && !names.includes(refreshCookieName), `Cookies after revocation: ${names.join(", ")}.`);
  assert(anonymous.status === 200 && anonymous.json.isAuthenticated === false, `Bootstrap after landing: ${anonymous.status} ${anonymous.json?.isAuthenticated}.`);
  assert(deepLink.pathname === `${pathBase}/login`, `A deep link after revocation went to ${deepLink.pathname}.`);
  return { status, reason, landedOn: `${landedOn.pathname}${landedOn.search}`, cookies: names, deepLink: `${deepLink.pathname}${deepLink.search}` };
});

await browser.close();

mkdirSync(resultsFolder, { recursive: true });
writeFileSync(path.join(resultsFolder, `authentication-state-${options.browser}.json`), JSON.stringify({ browser: options.browser, results }, null, 2));
if (results.some((result) => !result.passed)) process.exit(1);
