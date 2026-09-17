import { expect, type Page } from "@playwright/test";
import { getCurrentSessionIdThroughAccountApi, getSessionsThroughAccountApi, sendAccountApiRequest } from "@blazor/e2e/account-api";
import { logInThroughBlazor, logOutButton, logOutThroughBlazor, openUserMenu, userMenuButton, signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import {
  antiforgeryCookieName,
  accessTokenCookieName,
  authSyncDialog,
  copyRefreshTokenCookie,
  currentSessionCard,
  deleteAccessTokenCookie,
  expectSessionsListed,
  getCookieNames,
  newBlazorContext,
  otherSessionCards,
  refreshTokenCookieName,
  reloadBootstrap,
  revokeOtherSessionThroughSessionsPage,
  revokeSessionDialog,
  sessionCards,
  setVisibilityState,
  trackWrites
} from "@blazor/e2e/sessions";
import { mainNavigation } from "@blazor/e2e/shell";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts } from "@blazor/e2e/texts";
import { blazorToast } from "@blazor/e2e/toast";
import { getBaseUrl } from "@shared/e2e/utils/constants";
import { assertNoUnexpectedErrors, createTestContext, expectNetworkErrors, type TestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * The account API's logout endpoint as a route pattern, for failure injection
 */
const logoutRoute = "**/api/account/authentication/logout";

/**
 * Marks the copy of a logout request the test sends itself to let the account API commit a logout whose response is lost
 */
const serverSideLogoutHeader = "x-test-server-side-logout";

/**
 * Count the requests the application sends to a route from now on, leaving out the test's own server-side logout copy
 * @param page Playwright page instance
 * @param method The HTTP method to count
 * @param routePattern A glob ending in the path to count
 * @returns A live counter
 */
function countRequests(page: Page, method: string, routePattern: string): { count: number } {
  const counter = { count: 0 };
  const path = routePattern.replace("**", "");
  page.on("request", (request) => {
    if (request.method() === method && new URL(request.url()).pathname === path && !request.headers()[serverSideLogoutHeader]) counter.count++;
  });
  return counter;
}

/**
 * The session storage key under which the error banner watch records that the framework showed its error banner
 */
const errorBannerShownKey = "e2e-blazor-error-ui-shown";

/**
 * Record in session storage whether the framework's unhandled error banner is displayed on the current document, so the
 * record survives the full document navigation that follows a lost session
 * @param page Playwright page instance on an interactive Blazor page
 */
async function watchErrorBanner(page: Page): Promise<void> {
  await page.evaluate((storageKey) => {
    sessionStorage.removeItem(storageKey);
    const banner = document.getElementById("blazor-error-ui");
    if (!banner) throw new Error("The page has no #blazor-error-ui element.");

    const record = () => {
      if (getComputedStyle(banner).display !== "none") sessionStorage.setItem(storageKey, "shown");
    };
    record();
    new MutationObserver(record).observe(banner, { attributes: true });
  }, errorBannerShownKey);
}

/**
 * Expect that the watched document never displayed the framework's error banner and wrote no unhandled exception to the
 * console
 * @param page Playwright page instance after the navigation that followed the watch
 * @param context Test context monitoring the page's console
 */
async function expectErrorBannerNeverShown(page: Page, context: TestContext): Promise<void> {
  expect(await page.evaluate((storageKey) => sessionStorage.getItem(storageKey), errorBannerShownKey)).toBeNull();
  expect(context.monitoring.consoleMessages.map((message) => message.text()).filter((text) => text.includes("Unhandled exception"))).toEqual([]);
}

async function expectAuthenticatedWorkspace(page: Page, email: string): Promise<void> {
  await expectBlazorUrl(page, "app");
  await expect(userMenuButton(page)).toBeVisible();
  await expect(page.getByTestId("bootstrap-authenticated")).toHaveText("True");
  await expect(page.getByTestId("bootstrap-email")).toHaveText(email);
}

test.describe("@smoke", () => {
  /**
   * The sessions page, the React edition's session management smoke journey:
   * - The sessions page opens from the main navigation and lists the current session as "This device", with its login
   *   method and no Revoke button
   * - A login from a second browser adds a second session after a reload, listed after the current one
   * - Cancel in the revoke confirmation keeps both sessions
   * - Revoke in the confirmation shows the toast and the 5-minute notice and leaves only the current session
   * - The page causes no policy violation and renders no style attribute
   */
  test("should list sessions and revoke another device's session through the sessions page", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const email = uniqueBlazorEmail();
    const secondContext = await newBlazorContext(browser);
    const secondPage = await secondContext.newPage();
    const secondTestContext = createTestContext(secondPage);

    await step("Sign up and open Sessions from the main navigation & verify the current device without Revoke")(async () => {
      await signUpThroughBlazor(page, email);

      await mainNavigation(page).getByRole("link", { name: texts.sessions, exact: true }).click();

      await expectBlazorUrl(page, "user/sessions");
      await expectSessionsListed(page, 1);
      await expect(currentSessionCard(page)).toContainText(texts.loginMethodOneTimePassword);
      await expect(currentSessionCard(page).getByRole("button", { name: texts.revoke, exact: true })).toHaveCount(0);
    })();

    await step("Log in from a second browser and reload the sessions page & verify two sessions with the current first")(async () => {
      await logInThroughBlazor(secondPage, email);

      await page.reload();

      await expectSessionsListed(page, 2);
      await expect(otherSessionCards(page)).toHaveCount(1);
      await expect(otherSessionCards(page).getByRole("button", { name: texts.revoke, exact: true })).toBeVisible();
    })();

    await step("Open the revoke confirmation and cancel & verify both sessions remain")(async () => {
      await otherSessionCards(page).getByRole("button", { name: texts.revoke, exact: true }).click();
      await expect(revokeSessionDialog(page)).toBeVisible();

      await revokeSessionDialog(page).getByRole("button", { name: texts.cancel, exact: true }).click();

      await expect(revokeSessionDialog(page)).toHaveCount(0);
      await expect(sessionCards(page)).toHaveCount(2);
    })();

    await step("Revoke the second browser's session & verify the toast, the delay notice and one remaining session")(async () => {
      await revokeOtherSessionThroughSessionsPage(page);

      await expect(blazorToast(page, texts.sessionRevokedSuccessfully)).toBeVisible();
      await expect(page.getByText(texts.sessionRevokeDelayNotice, { exact: true })).toBeVisible();
      await expectSessionsListed(page, 1);
      await expect(otherSessionCards(page)).toHaveCount(0);
      await expectNoPolicyViolations(page);
    })();

    await assertNoUnexpectedErrors(secondTestContext);
    await secondContext.close();
  });
});

test.describe("@comprehensive", () => {
  /**
   * Logout, its failure handling, a missing access token, session revocation, refresh token replay and the logout seen by
   * other tabs on the Blazor workspace.
   * - A rejected logout (500) shows the localized retry alert and keeps the user on the authenticated workspace
   * - A logout whose response is lost after the account API logged out shows the localized unconfirmed alert, sends no
   *   second logout on its own and keeps the workspace, because the browser still holds the session cookies
   * - Logging out again lands on the login page, leaves only the antiforgery cookie, the account API rejects the old
   *   session with 401 and the authenticated home then redirects to login
   * - A deleted access token cookie with a valid refresh token cookie is refreshed by the gateway on reload with no
   *   visible effect, and the cookie is set again. This simulates expiry by removing the cookie; the refresh of an actually
   *   expired access token is covered by the gateway's middleware tests and the browser harness
   * - An ordinary logout in a second browser revokes that browser's session only
   * - Revoking the first browser's session from a third browser through the sessions page, then forcing the gateway to
   *   refresh by deleting the first browser's access token cookie, answers the next API call with 401 Revoked and lands on
   *   the localized "Session ended" page, whose login link reaches the login page without a redirect loop
   * - A refresh token copied into another browser and used there twice makes the original token a replay: the original
   *   browser's next refresh answers 401 ReplayAttackDetected and lands on login, and so does the other browser's next one
   * - A tab hidden while its session is logged out elsewhere without a message shows nothing and no write, and shows
   *   "Logged out" once it becomes visible; Reload lands on login
   * - Without BroadcastChannel a logout in one tab ends the other tab when it gains focus
   * - Going back after that logout never restores the previous identity
   */
  test("should handle logout and its failures, access token refresh, session revocation, token replay and logout across tabs", async ({ page, browser }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    const email = uniqueBlazorEmail();
    const secondContext = await newBlazorContext(browser);
    const secondPage = await secondContext.newPage();
    const secondTestContext = createTestContext(secondPage);
    const thirdContext = await newBlazorContext(browser);
    const thirdPage = await thirdContext.newPage();
    const thirdTestContext = createTestContext(thirdPage);
    const attackerContext = await newBlazorContext(browser);
    const attackerPage = await attackerContext.newPage();
    const attackerTestContext = createTestContext(attackerPage);

    // === LOGOUT ===
    await step("Sign up and log out while the account API rejects the logout & verify the retry state keeps the session")(async () => {
      await signUpThroughBlazor(page, email);
      await page.route(logoutRoute, (route) => route.fulfill({ status: 500, contentType: "application/problem+json", body: "{}" }));

      await openUserMenu(page);
      await logOutButton(page).click();

      await expect(page.getByRole("alert").filter({ hasText: texts.logoutFailed })).toBeVisible();
      await expectNetworkErrors(context, [500]);
      await expect(logOutButton(page)).toBeEnabled();
      await expectAuthenticatedWorkspace(page, email);
      await page.unroute(logoutRoute);
    })();

    await step("Log out while the response is lost after the server logged out & verify the unconfirmed state without a second logout")(async () => {
      const logoutRequests = countRequests(page, "POST", logoutRoute);
      const sessionCookies = await page.context().cookies(getBaseUrl());
      await page.route(logoutRoute, async (route) => {
        const headers = route.request().headers();
        if (headers[serverSideLogoutHeader]) return route.continue();

        // The account API logs out through a copy of the request, then the browser's cookies are put back and the
        // original request loses its connection, as when a response is lost after the server committed
        await page.evaluate(
          async ({ path, antiforgeryToken, header }) => {
            await fetch(path, { method: "POST", credentials: "same-origin", headers: { "x-xsrf-token": antiforgeryToken, [header]: "1" } });
          },
          { path: new URL(route.request().url()).pathname, antiforgeryToken: headers["x-xsrf-token"], header: serverSideLogoutHeader }
        );
        await page.context().addCookies(sessionCookies);
        await route.abort("connectionreset");
      });

      await openUserMenu(page);
      await logOutButton(page).click();

      await expect(page.getByRole("alert").filter({ hasText: texts.logoutUnconfirmed })).toBeVisible();
      await expect(page.getByRole("alert").filter({ hasText: texts.logoutFailed })).toHaveCount(0);
      await expect(logOutButton(page)).toBeEnabled();
      expect(logoutRequests.count).toBe(1);
      await expectBlazorUrl(page, "app");
      await page.unroute(logoutRoute);
    })();

    await step("Log out again & verify the login page, only the antiforgery cookie and no usable session remain")(async () => {
      await logOutThroughBlazor(page);

      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
      const cookieNames = await getCookieNames(page);
      expect(cookieNames).toContain(antiforgeryCookieName);
      expect(cookieNames).not.toContain(accessTokenCookieName);
      expect(cookieNames).not.toContain(refreshTokenCookieName);
      expect((await sendAccountApiRequest(page, "GET", "/api/account/users/me")).status).toBe(401);
      await expectNetworkErrors(context, [401]);
    })();

    await step("Open the authenticated home after logout & verify redirect to login")(async () => {
      await page.goto(blazorPath("app"));

      await expectBlazorUrl(page, "login");
    })();

    // === ACCESS TOKEN REFRESH ===
    await step("Log in, delete the access token cookie and reload & verify the session continues unnoticed")(async () => {
      await logInThroughBlazor(page, email);
      await deleteAccessTokenCookie(page);

      await page.reload();

      await expectAuthenticatedWorkspace(page, email);
      expect(await getCookieNames(page)).toContain(accessTokenCookieName);
    })();

    // === SESSION REVOCATION ===
    await step("Log in and out in a second browser & verify the first browser's session is not revoked")(async () => {
      await logInThroughBlazor(secondPage, email);
      await logOutThroughBlazor(secondPage);

      await deleteAccessTokenCookie(page);
      await page.reload();

      await expectAuthenticatedWorkspace(page, email);
    })();

    await step("Revoke the first browser's session from a third browser's sessions page & verify the session ended page")(async () => {
      const firstSessionId = await getCurrentSessionIdThroughAccountApi(page);
      await logInThroughBlazor(thirdPage, email);
      await gotoBlazor(thirdPage, "user/sessions");
      await expectSessionsListed(thirdPage, 2);
      await revokeOtherSessionThroughSessionsPage(thirdPage);
      await expect(blazorToast(thirdPage, texts.sessionRevokedSuccessfully)).toBeVisible();
      await expectSessionsListed(thirdPage, 1);
      expect((await getSessionsThroughAccountApi(thirdPage)).map((session) => session.id)).not.toContain(firstSessionId);
      await deleteAccessTokenCookie(page);
      await watchErrorBanner(page);

      const unauthorizedResponse = await reloadBootstrap(page);

      expect(unauthorizedResponse.status()).toBe(401);
      expect(await unauthorizedResponse.headerValue("x-unauthorized-reason")).toBe("Revoked");
      await expectBlazorUrl(page, "error");
      await expect(page).toHaveURL((url) => url.searchParams.get("error") === "session_revoked");
      await expect(page.getByRole("heading", { name: texts.sessionEnded })).toBeVisible();
      await expectErrorBannerNeverShown(page, context);
      await expect(page.getByText(texts.sessionRevoked, { exact: true })).toBeVisible();
      await expect(page.getByText(texts.logInAgainToContinue)).toBeVisible();
      await expectNetworkErrors(context, [401]);
      expect(await getCookieNames(page)).not.toContain(refreshTokenCookieName);
    })();

    await step("Follow the login link on the session ended page & verify the login page")(async () => {
      await page.getByRole("main").getByRole("link", { name: texts.logIn, exact: true }).click();

      await expectBlazorUrl(page, "login");
      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
      await page.goto(blazorPath("app"));
      await expectBlazorUrl(page, "login");
    })();

    // === REFRESH TOKEN REPLAY ===
    await step("Copy a refresh token into another browser and refresh with it twice & verify that browser is signed in")(async () => {
      await logInThroughBlazor(secondPage, email);
      await copyRefreshTokenCookie(secondContext, attackerContext);

      await attackerPage.goto(blazorPath("app"));
      await expectAuthenticatedWorkspace(attackerPage, email);
      await deleteAccessTokenCookie(attackerPage);
      await attackerPage.reload();

      await expectAuthenticatedWorkspace(attackerPage, email);
    })();

    await step("Refresh with the original token in the original browser & verify 401 ReplayAttackDetected and the login page")(async () => {
      await deleteAccessTokenCookie(secondPage);

      const replayResponse = await reloadBootstrap(secondPage);

      expect(replayResponse.status()).toBe(401);
      expect(await replayResponse.headerValue("x-unauthorized-reason")).toBe("ReplayAttackDetected");
      await expectBlazorUrl(secondPage, "login");
      await expect(secondPage.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
      await expectNetworkErrors(secondTestContext, [401]);
    })();

    await step("Refresh with the copied token in the other browser & verify 401 ReplayAttackDetected and the login page")(async () => {
      await deleteAccessTokenCookie(attackerPage);

      const replayResponse = await reloadBootstrap(attackerPage);

      expect(replayResponse.status()).toBe(401);
      expect(await replayResponse.headerValue("x-unauthorized-reason")).toBe("ReplayAttackDetected");
      await expectBlazorUrl(attackerPage, "login");
      await expect(attackerPage.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
      await expectNetworkErrors(attackerTestContext, [401]);
    })();

    // === LOGOUT ACROSS TABS ===
    await step("Log out through the account API while a second tab is hidden & verify the tab shows Logged out only once visible")(async () => {
      const hiddenPage = await thirdContext.newPage();
      const hiddenTestContext = createTestContext(hiddenPage);
      await gotoBlazor(hiddenPage, "app");
      await expectAuthenticatedWorkspace(hiddenPage, email);
      await setVisibilityState(hiddenPage, "hidden");
      const hiddenWrites = trackWrites(hiddenPage);

      expect((await sendAccountApiRequest(thirdPage, "POST", "/api/account/authentication/logout")).status).toBeLessThan(300);
      await expect(authSyncDialog(hiddenPage, texts.loggedOut)).toHaveCount(0);
      await setVisibilityState(hiddenPage, "visible");

      await expect(authSyncDialog(hiddenPage, texts.loggedOut)).toBeVisible();
      await expect(authSyncDialog(hiddenPage, texts.loggedOut)).toContainText(texts.loggedOutInAnotherTab);
      await expect(hiddenPage.getByTestId("bootstrap-email")).toHaveCount(0);
      expect(hiddenWrites).toEqual([]);
      await authSyncDialog(hiddenPage, texts.loggedOut).getByRole("button", { name: texts.reload, exact: true }).click();
      await expectBlazorUrl(hiddenPage, "login");
      await assertNoUnexpectedErrors(hiddenTestContext);
    })();

    await step("Log out in one tab of a browser without BroadcastChannel & verify the other tab ends when it gains focus")(async () => {
      const channelContext = await newBlazorContext(browser);
      await channelContext.addInitScript(() => {
        delete (window as { BroadcastChannel?: unknown }).BroadcastChannel;
      });
      const mainTab = await channelContext.newPage();
      const mainTestContext = createTestContext(mainTab);
      await trackPolicyViolations(mainTab);
      await logInThroughBlazor(mainTab, email);
      const otherTab = await channelContext.newPage();
      const otherTestContext = createTestContext(otherTab);
      await trackPolicyViolations(otherTab);
      await gotoBlazor(otherTab, "app");
      await expectAuthenticatedWorkspace(otherTab, email);
      expect(await otherTab.evaluate(() => typeof window.BroadcastChannel)).toBe("undefined");
      const otherWrites = trackWrites(otherTab);

      await logOutThroughBlazor(mainTab);
      await expect(authSyncDialog(otherTab, texts.loggedOut)).toHaveCount(0);
      await otherTab.evaluate(() => window.dispatchEvent(new Event("focus")));

      await expect(authSyncDialog(otherTab, texts.loggedOut)).toBeVisible();
      await expect(otherTab.getByTestId("bootstrap-email")).toHaveCount(0);
      expect(otherWrites).toEqual([]);
      await expectNoPolicyViolations(otherTab);

      await mainTab.goBack();

      await expectBlazorUrl(mainTab, "login");
      await expect(mainTab.getByTestId("bootstrap-email")).toHaveCount(0);
      await expect(userMenuButton(mainTab)).toHaveCount(0);
      await assertNoUnexpectedErrors(mainTestContext);
      await assertNoUnexpectedErrors(otherTestContext);
      await channelContext.close();
    })();

    await assertNoUnexpectedErrors(secondTestContext);
    await assertNoUnexpectedErrors(thirdTestContext);
    await assertNoUnexpectedErrors(attackerTestContext);
    await attackerContext.close();
    await thirdContext.close();
    await secondContext.close();
  });
});
