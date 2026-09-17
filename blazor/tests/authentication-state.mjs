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
// 4. Logout: the client posts the logout and loads the login page as a new document; of the authentication cookies only
//    the antiforgery cookie is left.
//
// 5. Sessions page: the current session is listed first, another session is revoked through the confirmation with the
//    toast and the delay notice, a refusal is shown as the account API returned it, the page renders in both cultures
//    with 0 policy violations, and the revoked browser lands on the "Session ended" page.
// 6. Replay: a refresh cookie copied to a second browser and used there, then reused by the original browser after the
//    30 second grace period, revokes the session with ReplayAttackDetected and sends both browsers to login, never to the
//    error page.
// 7. Multi-tab identity synchronization: a tenant switch in one tab shows the non-dismissable "Account switched" dialog in
//    another tab of the same browser, which reloads to the new tenant; a logout ends a tab with a dirty profile form and
//    a tab with a pending list request without submitting anything or repopulating data; a different user signing in to
//    the same tenant ends the other tab; stale, duplicate and out-of-order messages change nothing; a tab that missed the
//    message (hidden, without BroadcastChannel, or restored through back and forward) reconciles with the server.
//
// 8. Invalid credentials: a malformed access cookie gives a 401 bootstrap through the gateway, never the anonymous one.
// 9. Isolation: two users loading their authenticated page and bootstrap at the same time each receive only their own
//    identity, the documents and bootstrap are not stored, and no token header reaches the browser.
//
// Every run writes its host configuration (environment and build) into the result; the Production evidence comes from the
// trimmed publish served by blazor-serve, and a run against the AppHost's Development host is a separate check.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started.
// Run: dotnet run --project developer-cli -- blazor-harness authentication-state --browser chromium

import {
  baseUrl,
  launchBrowser,
  newContext,
  parseArguments,
  pathBase,
  policyViolationsOf,
  probeHostConfiguration,
  readOneTimePassword,
  redact,
  signUpThroughBlazor,
  submitOneTimePasswordThroughBlazor,
  writeResult
} from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
const accessCookieName = "__Host-access-token";
const refreshCookieName = "__Host-refresh-token";
const antiforgeryCookieName = "__Host-xsrf-token";
const bootstrapPath = "/api/account/bootstrap";
const interactiveTimeoutMs = 60_000;
// The account API accepts a token up to 5 seconds past its expiry; the margin covers that and clock differences
const expiryMarginMs = 15_000;
// Headers the gateway and the account API use to pass tokens between themselves; none may reach a browser
const tokenHeaderNames = ["x-access-token", "x-refresh-token"];
const expectedCaseCount = 16;
const replayGraceMs = 30_000;
const replayMarginMs = 5_000;

const browser = await launchBrowser(options.browser);
const results = [];
const hostConfiguration = await probeHostConfiguration(browser, options.browser);
const stamp = `${options.browser}-${Date.now()}`;

async function check(name, action) {
  try {
    const detail = await action();
    results.push({ name, passed: true, detail });
    console.log(redact(`PASS ${name}${detail ? `: ${JSON.stringify(detail)}` : ""}`));
  } catch (error) {
    results.push({ name, passed: false, detail: error.message });
    console.log(redact(`FAIL ${name}: ${error.message}`));
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
      return { status: response.status, headerNames: [...response.headers.keys()], reason: response.headers.get("x-unauthorized-reason"), cacheControl: response.headers.get("cache-control"), text, json };
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
  await submitOneTimePasswordThroughBlazor(page, await readOneTimePassword(email, sentAfter));
  await page.waitForURL(`${baseUrl}${pathBase}/app`);
  await waitInteractive(page);
  return page;
}

function syncDialog(page) {
  return page.locator('[data-testid="auth-sync-dialog"][data-open="true"]');
}

async function syncOutcome(page) {
  await syncDialog(page).waitFor({ timeout: interactiveTimeoutMs });
  return page.locator("#auth-sync-title").getAttribute("data-outcome");
}

async function openInteractive(context, path) {
  const page = await context.newPage();
  await page.goto(`${baseUrl}${pathBase}${path}`, { waitUntil: "load" });
  await page.locator('[data-testid="app-shell"][data-tenants-state="loaded"]').waitFor({ timeout: interactiveTimeoutMs });
  return page;
}

// Every request a page sends from now on that is not a safe read
function recordWrites(page) {
  const writes = [];
  page.on("request", (request) => {
    if (!["GET", "HEAD", "OPTIONS"].includes(request.method()) && new URL(request.url()).pathname.startsWith("/api/")) writes.push(`${request.method()} ${new URL(request.url()).pathname}`);
  });
  return writes;
}

async function logOutThroughMenu(page) {
  await page.locator("#user-menu-trigger").click({ timeout: interactiveTimeoutMs });
  await page.locator('[role="menu"] [role="menuitem"]').last().click();
  await page.waitForURL(new RegExp(`${pathBase}/login`), { timeout: interactiveTimeoutMs });
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

await check("malformed access token with a valid refresh cookie gives a 401 bootstrap through the gateway, not the anonymous bootstrap", async () => {
  const anonymousContext = await newContext(browser, options.browser);
  const anonymousPage = await anonymousContext.newPage();
  await anonymousPage.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
  const anonymous = await inPageFetch(anonymousPage, bootstrapPath);
  await anonymousContext.close();

  // The gateway reads the access cookie only beside a refresh cookie, so the malformed token rides on a real session
  const context = await newContext(browser, options.browser);
  const page = await logIn(context, userB);
  // Probed from a static page: an interactive page reconciles its identity on focus and would leave on the 401 mid-probe
  await page.goto(`${baseUrl}${pathBase}/legal/terms`, { waitUntil: "load" });
  await context.addCookies([{ name: accessCookieName, value: "eyJhbGciOiJSUzI1NiJ9.eyJleHAiOjk5OTk5OTk5OTl9.bm90LWEtc2lnbmF0dXJl", url: baseUrl, secure: true, httpOnly: true }]);
  const malformed = await inPageFetch(page, bootstrapPath);
  await context.close();
  assert(anonymous.status === 200 && anonymous.json?.isAuthenticated === false, `Anonymous bootstrap: ${anonymous.status} ${anonymous.json?.isAuthenticated}.`);
  assert(malformed.status === 401, `Bootstrap with a malformed access token returned ${malformed.status}, not 401.`);
  assert(malformed.json?.isAuthenticated === undefined, "Bootstrap with a malformed access token returned a bootstrap body.");
  return { anonymous: anonymous.status, malformed: malformed.status, reason: malformed.reason };
});

await check("two users at the same time get only their own identity, no stored document or bootstrap and no token header", async () => {
  const users = [];
  for (const email of [userA, userB]) {
    const context = await newContext(browser, options.browser);
    users.push({ email, context, page: await logIn(context, email) });
  }
  const loads = await Promise.all(
    users.map(async (user) => {
      const documentPage = await user.context.newPage();
      const [documentResponse, bootstrap] = await Promise.all([documentPage.goto(`${baseUrl}${pathBase}/app`, { waitUntil: "commit" }), inPageFetch(user.page, bootstrapPath)]);
      return { user, documentStatus: documentResponse.status(), documentHeaders: await documentResponse.allHeaders(), documentText: await documentResponse.text(), bootstrap };
    })
  );
  for (const user of users) await user.context.close();

  for (const { user, documentStatus, documentHeaders, documentText, bootstrap } of loads) {
    const other = users.find((candidate) => candidate !== user).email;
    assert(documentStatus === 200, `The authenticated page for ${user.email} returned ${documentStatus}.`);
    assert(documentHeaders["cache-control"]?.includes("no-store"), `The authenticated page Cache-Control is ${documentHeaders["cache-control"]}.`);
    assert(bootstrap.status === 200 && bootstrap.json.user?.email === user.email, `Bootstrap for ${user.email} named ${bootstrap.json?.user?.email}.`);
    assert(bootstrap.cacheControl?.includes("no-store"), `Bootstrap Cache-Control is ${bootstrap.cacheControl}.`);
    assert(!documentText.includes(other) && !bootstrap.text.includes(other), `The response for ${user.email} contains the other user.`);
    const leaked = tokenHeaderNames.filter((name) => name in documentHeaders || bootstrap.headerNames.includes(name));
    assert(leaked.length === 0, `Token headers reached the browser: ${leaked.join(", ")}.`);
  }
  return { users: loads.length, checkedHeaders: tokenHeaderNames };
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
  await page.locator("#user-menu-trigger").click({ timeout: interactiveTimeoutMs });
  await page.locator('[role="menuitem"][data-tenant-id]').nth(1).waitFor({ timeout: interactiveTimeoutMs });
  const before = (await inPageFetch(page, bootstrapPath)).json.user;
  const target = page.locator('[role="menuitem"][data-tenant-id]:not([disabled])').first();
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

await check("tenant switch in one tab shows the Account switched dialog in another tab, which reloads to the new tenant", async () => {
  assert(tenantSwitch.page !== undefined, "The tenant switch journey did not leave a signed-in page.");
  const page = tenantSwitch.page;
  const other = await openInteractive(tenantSwitch.context, "/app");
  const otherWrites = recordWrites(other);
  const before = (await inPageFetch(other, bootstrapPath)).json.user;
  await page.locator("#user-menu-trigger").click({ timeout: interactiveTimeoutMs });
  const target = page.locator('[role="menuitem"][data-tenant-id]:not([disabled])').first();
  await target.waitFor({ timeout: interactiveTimeoutMs });
  const targetTenantId = await target.getAttribute("data-tenant-id");
  await target.click();
  await page.waitForURL(`${baseUrl}${pathBase}/app`);
  await waitInteractive(page);

  const outcome = await syncOutcome(other);
  const message = await other.locator('[data-testid="auth-sync-message"]').textContent();
  const staleIdentityShown = await other.locator('[data-testid="bootstrap-email"]').count();
  await other.keyboard.press("Escape");
  const stillOpen = await syncDialog(other).isVisible();
  const documentBefore = await documentOrigin(other);
  await other.locator('[data-testid="auth-sync-reload"]').click();
  await other.waitForFunction((origin) => performance.timeOrigin !== origin, documentBefore, { timeout: interactiveTimeoutMs });
  await waitInteractive(other);
  const shownAfterReload = await other.locator('[data-testid="bootstrap-tenant-id"]').textContent();
  const violations = policyViolationsOf(tenantSwitch.context).length;
  await other.close();

  assert(targetTenantId !== before.tenantId, `The switch target ${targetTenantId} is the current tenant.`);
  assert(outcome === "TenantSwitched", `The other tab's dialog outcome is ${outcome}.`);
  assert(/Your account was switched to .+ in another browser tab\./.test(message ?? ""), `The dialog says ${message}.`);
  assert(staleIdentityShown === 0, "The other tab still shows the previous identity behind the dialog.");
  assert(stillOpen, "Escape dismissed the dialog.");
  assert(otherWrites.length === 0, `The other tab sent ${otherWrites.join(", ")}.`);
  assert(shownAfterReload === targetTenantId, `After Reload the other tab shows tenant ${shownAfterReload}, not ${targetTenantId}.`);
  assert(violations === 0, `${violations} policy violations.`);
  return { from: before.tenantId, to: targetTenantId, message };
});

// The preferred-tenant cookie a tenant switch writes is a login hint, not an authentication cookie, and outlives logout on
// purpose so the next login prefers that tenant
await check("logout loads the login page as a new document and leaves only the antiforgery cookie of the authentication cookies", async () => {
  assert(tenantSwitch.page !== undefined, "The tenant switch journey did not leave a signed-in page.");
  const page = tenantSwitch.page;
  const documentBefore = await documentOrigin(page);
  await page.locator("#user-menu-trigger").click({ timeout: interactiveTimeoutMs });
  await page.locator('[role="menu"] [role="menuitem"]').last().click();
  await page.waitForURL(`${baseUrl}${pathBase}/login`, { timeout: interactiveTimeoutMs });
  const fullDocumentNavigation = (await documentOrigin(page)) !== documentBefore;
  const names = (await cookieNames(tenantSwitch.context)).filter((name) => name !== "preferred-tenant");
  const bootstrap = await inPageFetch(page, bootstrapPath);
  await tenantSwitch.context.close();
  assert(fullDocumentNavigation, "Logout did not load a new document.");
  assert(names.length === 1 && names[0] === antiforgeryCookieName, `Cookies after logout: ${names.join(", ")}.`);
  assert(bootstrap.status === 200 && bootstrap.json.isAuthenticated === false, `Bootstrap after logout: ${bootstrap.status} ${bootstrap.json?.isAuthenticated}.`);
  return { cookies: names };
});

const sessionsPath = "/api/account/authentication/sessions";

async function currentSessionId(page) {
  return (await inPageFetch(page, sessionsPath)).json.sessions.find((session) => session.isCurrent).id;
}

await check("sessions page lists the current session first, revokes another through the confirmation, shows a refusal as returned, in both cultures", async () => {
  const context = await newContext(browser, options.browser);
  const page = await logIn(context, userB);
  const revokedContext = await newContext(browser, options.browser);
  const revokedPage = await logIn(revokedContext, userB);
  const refusedContext = await newContext(browser, options.browser);
  const refusedPage = await logIn(refusedContext, userB);
  const revokedSessionId = await currentSessionId(revokedPage);
  const refusedSessionId = await currentSessionId(refusedPage);
  await refusedContext.close();

  await page.goto(`${baseUrl}${pathBase}/user/sessions`, { waitUntil: "load" });
  await page.locator('[data-testid="sessions"][data-state="loaded"]').waitFor({ timeout: interactiveTimeoutMs });
  const heading = await page.getByRole("heading", { level: 1 }).textContent();
  const firstIsCurrent = await page.locator('[data-testid="session-card"]').first().getAttribute("data-current");
  const cards = await page.locator('[data-testid="session-card"]').count();
  const currentLabel = await page.locator('[data-current="true"] [data-testid="session-this-device"]').textContent();

  await page.locator(`[data-session-id="${revokedSessionId}"] [data-testid="session-revoke"]`).click();
  await page.locator('[data-testid="revoke-session-dialog"][data-open="true"]').waitFor();
  await page.locator('[data-testid="revoke-session-confirm"]').click();
  await page.locator('[data-testid="session-revoked-toast"]').waitFor({ timeout: interactiveTimeoutMs });
  await page.locator('[data-testid="sessions-revoke-notice"]').waitFor();
  await page.locator(`[data-session-id="${revokedSessionId}"]`).waitFor({ state: "detached", timeout: interactiveTimeoutMs });

  await page.locator(`[data-session-id="${refusedSessionId}"] [data-testid="session-revoke"]`).click();
  await page.locator('[data-testid="revoke-session-dialog"][data-open="true"]').waitFor();
  const revokedFirst = await apiWithAntiforgery(page, "DELETE", `${sessionsPath}/${refusedSessionId}`);
  await page.locator('[data-testid="revoke-session-confirm"]').click();
  const refusal = await page.locator('[data-testid="revoke-session-dialog"] [data-testid="form-error"]', { hasText: "revoked" }).textContent({ timeout: interactiveTimeoutMs });
  await page.locator('[data-testid="revoke-session-cancel"]').click();

  // The revoked browser: its access token is dropped so the next call refreshes against the revoked session
  await revokedContext.clearCookies({ name: accessCookieName });
  await revokedPage.locator('[data-testid="reload-bootstrap"]').click();
  await revokedPage.waitForURL(/\/blazor\/error\?error=/, { timeout: interactiveTimeoutMs });
  const revokedLanding = new URL(revokedPage.url());
  const revokedTitle = await revokedPage.getByRole("heading", { level: 1 }).textContent();
  await revokedContext.close();

  const toDanish = await apiWithAntiforgery(page, "PUT", "/api/account/users/me/change-locale", { locale: "da-DK" });
  await page.reload({ waitUntil: "load" });
  await page.locator('[data-testid="sessions"][data-state="loaded"]').waitFor({ timeout: interactiveTimeoutMs });
  const danishHeading = await page.getByRole("heading", { level: 1 }).textContent();
  const danishCurrentLabel = await page.locator('[data-current="true"] [data-testid="session-this-device"]').textContent();
  const toEnglish = await apiWithAntiforgery(page, "PUT", "/api/account/users/me/change-locale", { locale: "en-US" });
  const styleAttributes = await page.locator("body [style]").count();
  const violations = policyViolationsOf(context).length;
  await context.close();

  assert(heading === "User sessions" && danishHeading === "Brugersessioner", `Headings: ${heading}, ${danishHeading}.`);
  assert(firstIsCurrent === "true" && cards >= 3, `First card current: ${firstIsCurrent}, cards: ${cards}.`);
  assert(currentLabel === "This device" && danishCurrentLabel === "Denne enhed", `Current labels: ${currentLabel}, ${danishCurrentLabel}.`);
  assert(revokedFirst.status < 300, `Revoking the refused session through the API returned ${revokedFirst.status}.`);
  assert(refusal === `Session with id '${refusedSessionId}' is already revoked.`, `The refusal shown is ${refusal}.`);
  assert(revokedLanding.searchParams.get("error") === "session_revoked" && revokedTitle === "Session ended", `The revoked browser landed on ${revokedLanding.search} titled ${revokedTitle}.`);
  assert(toDanish.status < 300 && toEnglish.status < 300, `Locale changes returned ${toDanish.status} and ${toEnglish.status}.`);
  assert(styleAttributes === 0 && violations === 0, `${styleAttributes} style attributes, ${violations} policy violations.`);
  return { cards, refusal, revokedLanding: `${revokedLanding.pathname}${revokedLanding.search}` };
});

await check("a refresh token reused after the grace period sends both browsers to login, not to the error page", async () => {
  const original = await newContext(browser, options.browser);
  const originalPage = await logIn(original, userB);
  const stolenRefresh = (await original.cookies(baseUrl)).find((cookie) => cookie.name === refreshCookieName);
  assert(stolenRefresh !== undefined, "No refresh cookie to copy.");

  const thief = await newContext(browser, options.browser);
  await thief.addCookies([{ name: refreshCookieName, value: stolenRefresh.value, url: baseUrl, secure: true, httpOnly: true, sameSite: stolenRefresh.sameSite }]);
  const thiefPage = await thief.newPage();
  await thiefPage.goto(`${baseUrl}${pathBase}/app`, { waitUntil: "load" });
  await waitInteractive(thiefPage);
  const thiefBootstrap = await inPageFetch(thiefPage, bootstrapPath);
  assert(thiefBootstrap.json?.isAuthenticated === true, "The copied refresh cookie did not sign the second browser in.");

  console.log(`Waiting ${Math.round((replayGraceMs + replayMarginMs) / 1000)} s for the refresh token grace period to pass`);
  await new Promise((resolve) => setTimeout(resolve, replayGraceMs + replayMarginMs));

  const leave = async (context, page) => {
    await context.clearCookies({ name: accessCookieName });
    const bootstrapResponse = page.waitForResponse((response) => new URL(response.url()).pathname === bootstrapPath);
    await page.locator('[data-testid="reload-bootstrap"]').click();
    const response = await bootstrapResponse;
    const reason = (await response.allHeaders())["x-unauthorized-reason"] ?? null;
    await page.waitForURL(new RegExp(`${pathBase}/(login|error)`), { timeout: interactiveTimeoutMs });
    return { status: response.status(), reason, landedOn: new URL(page.url()).pathname };
  };
  const reused = await leave(original, originalPage);
  const thiefResult = await leave(thief, thiefPage);
  await original.close();
  await thief.close();

  assert(reused.status === 401 && reused.reason === "ReplayAttackDetected", `The reused token answered ${reused.status} ${reused.reason}.`);
  assert(thiefResult.status === 401 && thiefResult.reason === "ReplayAttackDetected", `The rotated token answered ${thiefResult.status} ${thiefResult.reason}.`);
  assert(reused.landedOn === `${pathBase}/login` && thiefResult.landedOn === `${pathBase}/login`, `Landed on ${reused.landedOn} and ${thiefResult.landedOn}.`);
  return { reused, thief: thiefResult };
});

await check("logout in one tab ends a dirty profile tab and a tab with a pending list request without a write or repopulated data", async () => {
  const context = await newContext(browser, options.browser);
  const main = await logIn(context, userB);
  const profile = await openInteractive(context, "/user/profile");
  await profile.locator('[data-testid="first-name"]').fill(`Unsaved ${Date.now() % 1000}`);
  // The list request is held in the browser and answered after the logout with the body the session could read before it
  const listBody = (await inPageFetch(main, "/api/account/users?OrderBy=Name&SortOrder=Ascending&PageSize=25")).text;
  const users = await context.newPage();
  let heldRoute;
  const held = new Promise((resolve) => {
    users.route(
      (url) => url.pathname === "/api/account/users",
      (route) => {
        heldRoute = route;
        resolve();
      }
    );
  });
  await users.goto(`${baseUrl}${pathBase}/account/users`, { waitUntil: "load" });
  await held;
  const profileWrites = recordWrites(profile);
  const usersWrites = recordWrites(users);
  const dialogs = [];
  profile.on("dialog", (dialog) => dialogs.push(dialog.type()));

  await logOutThroughMenu(main);
  const profileOutcome = await syncOutcome(profile);
  const usersOutcome = await syncOutcome(users);
  const formsBehindDialog = await profile.locator('[data-testid="profile-form"]').count();
  await heldRoute.fulfill({ status: 200, contentType: "application/json", body: listBody });
  await users.waitForTimeout(1500);
  const usersMain = await users.locator("#main-content").textContent();
  const usersDialogOpen = await syncDialog(users).isVisible();

  await profile.locator('[data-testid="auth-sync-reload"]').click();
  await profile.waitForURL(new RegExp(`${pathBase}/login`), { timeout: interactiveTimeoutMs });
  const violations = policyViolationsOf(context).length;
  await context.close();

  assert(listBody.includes(userA), "The held list body does not contain the member it should have repopulated.");
  assert(profileOutcome === "LoggedOut" && usersOutcome === "LoggedOut", `Outcomes: profile ${profileOutcome}, users ${usersOutcome}.`);
  assert(formsBehindDialog === 0, "The dirty profile form is still rendered behind the dialog.");
  assert(!usersMain.includes(userA) && !usersMain.includes(userB) && usersDialogOpen, "The late list response repopulated the users tab.");
  assert(profileWrites.length === 0 && usersWrites.length === 0, `Writes after the logout: ${[...profileWrites, ...usersWrites].join(", ")}.`);
  assert(dialogs.length === 0, `Reload raised ${dialogs.join(", ")}.`);
  assert(violations === 0, `${violations} policy violations.`);
  return { profileOutcome, usersOutcome };
});

await check("a different user signing in to the same tenant in another tab ends this tab's identity", async () => {
  const context = await newContext(browser, options.browser);
  const signIn = await logIn(context, userB);
  const tenantB = (await inPageFetch(signIn, bootstrapPath)).json.user.tenantId;
  const other = await openInteractive(context, "/app");
  const logout = await apiWithAntiforgery(signIn, "POST", "/api/account/authentication/logout");
  await context.addCookies([{ name: "preferred-tenant", value: tenantB, url: baseUrl, secure: true }]);
  await signIn.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
  await signIn.locator('[data-testid="email"]').fill(userA);
  const sentAfter = Date.now();
  await signIn.locator('[data-testid="submit"]').click();
  await signIn.waitForURL(/\/blazor\/login\/verify\?/);
  await submitOneTimePasswordThroughBlazor(signIn, await readOneTimePassword(userA, sentAfter));
  await signIn.waitForURL(`${baseUrl}${pathBase}/app`);
  await waitInteractive(signIn);
  const signedIn = (await inPageFetch(signIn, bootstrapPath)).json.user;

  const outcome = await syncOutcome(other);
  await other.locator('[data-testid="auth-sync-reload"]').click();
  await other.waitForURL(`${baseUrl}${pathBase}/app`);
  await waitInteractive(other);
  const shownAfterReload = await other.locator('[data-testid="bootstrap-email"]').textContent();
  await context.close();

  assert(logout.status < 300, `Logout returned ${logout.status}.`);
  assert(signedIn.email === userA && signedIn.tenantId === tenantB, `Signed in as ${signedIn.email} in tenant ${signedIn.tenantId}, expected tenant ${tenantB}.`);
  assert(outcome === "DifferentUser", `The other tab's dialog outcome is ${outcome}.`);
  assert(shownAfterReload === userA, `After Reload the other tab shows ${shownAfterReload}.`);
  return { tenant: tenantB, outcome };
});

await check("stale, duplicate and out-of-order channel messages change nothing, and a newer message is only a hint the reload does not adopt", async () => {
  const context = await newContext(browser, options.browser);
  const page = await logIn(context, userB);
  await page.locator('[data-testid="app-shell"][data-tenants-state="loaded"]').waitFor({ timeout: interactiveTimeoutMs });
  const identity = (await inPageFetch(page, bootstrapPath)).json.user;
  const poster = await context.newPage();
  await poster.goto(`${baseUrl}${pathBase}/legal/terms`, { waitUntil: "load" });
  const post = (messages) =>
    poster.evaluate((items) => {
      const channel = new BroadcastChannel("auth-sync");
      for (const item of items) channel.postMessage(item);
      channel.close();
    }, messages);

  const now = Date.now();
  const sameIdentity = { type: "USER_LOGGED_IN", userId: identity.id, tenantId: identity.tenantId, email: identity.email, timestamp: now + 1000 };
  await post([
    sameIdentity,
    sameIdentity,
    { type: "TENANT_SWITCHED", userId: identity.id, newTenantId: "999999", previousTenantId: identity.tenantId, tenantName: "Stale", timestamp: now + 500 },
    { type: "USER_LOGGED_OUT", userId: identity.id, timestamp: now }
  ]);
  await page.waitForTimeout(1500);
  const dialogAfterStale = await syncDialog(page).isVisible();
  const emailAfterStale = await page.locator('[data-testid="bootstrap-email"]').textContent();

  await post([{ type: "TENANT_SWITCHED", userId: identity.id, newTenantId: "999999", previousTenantId: identity.tenantId, tenantName: "Forged", timestamp: now + 2000 }]);
  const outcome = await syncOutcome(page);
  await page.locator('[data-testid="auth-sync-reload"]').click();
  await page.waitForURL(`${baseUrl}${pathBase}/app`);
  await waitInteractive(page);
  const tenantAfterReload = await page.locator('[data-testid="bootstrap-tenant-id"]').textContent();
  await context.close();

  assert(!dialogAfterStale && emailAfterStale === userB, `After stale messages the dialog is ${dialogAfterStale} and the page shows ${emailAfterStale}.`);
  assert(outcome === "TenantSwitched", `The newer message's outcome is ${outcome}.`);
  assert(tenantAfterReload === identity.tenantId, `After Reload the tenant is ${tenantAfterReload}, not the server's ${identity.tenantId}.`);
  return { ignored: 4, outcome };
});

await check("a hidden tab that missed the logout reconciles with the server when it becomes visible", async () => {
  const context = await newContext(browser, options.browser);
  const page = await logIn(context, userB);
  const hidden = await openInteractive(context, "/app");
  await hidden.evaluate(() => {
    Object.defineProperty(document, "visibilityState", { configurable: true, get: () => "hidden" });
    document.dispatchEvent(new Event("visibilitychange"));
  });
  const logout = await apiWithAntiforgery(page, "POST", "/api/account/authentication/logout");
  await hidden.waitForTimeout(1000);
  const dialogWhileHidden = await syncDialog(hidden).isVisible();
  await hidden.evaluate(() => {
    Object.defineProperty(document, "visibilityState", { configurable: true, get: () => "visible" });
    document.dispatchEvent(new Event("visibilitychange"));
  });
  const outcome = await syncOutcome(hidden);
  await context.close();

  assert(logout.status < 300, `Logout returned ${logout.status}.`);
  assert(!dialogWhileHidden, "The hidden tab showed a dialog without a message or reconciliation.");
  assert(outcome === "LoggedOut", `The tab's outcome on becoming visible is ${outcome}.`);
  return { outcome };
});

await check("without BroadcastChannel a logout in one tab ends the other tab when it gains focus", async () => {
  const context = await newContext(browser, options.browser);
  await context.addInitScript(() => {
    delete window.BroadcastChannel;
  });
  const main = await logIn(context, userB);
  const other = await openInteractive(context, "/app");
  const channelMissing = await other.evaluate(() => typeof window.BroadcastChannel === "undefined");
  await logOutThroughMenu(main);
  await other.waitForTimeout(1000);
  const dialogBeforeFocus = await syncDialog(other).isVisible();
  await other.evaluate(() => window.dispatchEvent(new Event("focus")));
  const outcome = await syncOutcome(other);
  const violations = policyViolationsOf(context).length;
  await context.close();

  assert(channelMissing, "BroadcastChannel was not removed.");
  assert(!dialogBeforeFocus, "The tab ended without a channel or reconciliation.");
  assert(outcome === "LoggedOut", `The tab's outcome after focus is ${outcome}.`);
  assert(violations === 0, `${violations} policy violations.`);
  return { outcome };
});

await check("a page restored through back and forward after a logout never resumes the previous identity", async () => {
  const context = await newContext(browser, options.browser);
  const page = await logIn(context, userB);
  await page.locator('[data-testid="app-shell"][data-tenants-state="loaded"]').waitFor({ timeout: interactiveTimeoutMs });
  await page.locator('[data-testid="nav-terms"]').click();
  await page.waitForURL(new RegExp(`${pathBase}/legal/terms`));
  const logout = await apiWithAntiforgery(page, "POST", "/api/account/authentication/logout");
  await page.goBack({ waitUntil: "load" });
  const restored = await Promise.race([
    syncDialog(page)
      .waitFor({ timeout: interactiveTimeoutMs })
      .then(() => "dialog"),
    page.waitForURL(new RegExp(`${pathBase}/login`), { timeout: interactiveTimeoutMs }).then(() => "login")
  ]);
  const identityWithoutDialog = restored === "dialog" ? 0 : await page.locator('[data-testid="bootstrap-email"]').count();
  const outcome = restored === "dialog" ? await page.locator("#auth-sync-title").getAttribute("data-outcome") : null;
  await context.close();

  assert(logout.status < 300, `Logout returned ${logout.status}.`);
  assert(identityWithoutDialog === 0, "The restored page shows the previous identity.");
  assert(restored === "login" || outcome === "LoggedOut", `Restored as ${restored} with outcome ${outcome}.`);
  return { restored, outcome };
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

const verdict = writeResult(`authentication-state-${options.browser}.json`, { browser: options.browser, culture: "en-US", ...hostConfiguration, results }, expectedCaseCount);
console.log(`Result file: ${verdict.resultFile}`);
if (!verdict.passed) process.exit(1);
