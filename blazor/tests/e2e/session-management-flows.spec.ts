import { expect, type Page } from "@playwright/test";
import { getCurrentSessionIdThroughAccountApi, getSessionsThroughAccountApi, revokeSessionThroughAccountApi, sendAccountApiRequest } from "@blazor/e2e/account-api";
import { logInThroughBlazor, logOutThroughBlazor, signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { blazorPath, blazorUrl, expectBlazorUrl } from "@blazor/e2e/routes";
import { blazorCultures } from "@blazor/e2e/texts";
import { getBaseUrl } from "@shared/e2e/utils/constants";
import { assertNoUnexpectedErrors, createTestContext, expectNetworkErrors, type TestContext } from "@shared/e2e/utils/test-assertions";
import { uniqueEmail } from "@shared/e2e/utils/test-data";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * The authentication cookies the gateway sets, as named in SharedKernel's AuthenticationTokenHttpKeys
 */
const accessTokenCookieName = "__Host-access-token";
const refreshTokenCookieName = "__Host-refresh-token";
const antiforgeryCookieName = "__Host-xsrf-token";

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

async function getCookieNames(page: Page): Promise<string[]> {
  return (await page.context().cookies(getBaseUrl())).map((cookie) => cookie.name);
}

/**
 * Remove the access token cookie while keeping the refresh token cookie, so the next request through the gateway has to
 * refresh the session before it reaches the API or the host
 * @param page Playwright page instance of a signed-in user
 */
async function deleteAccessTokenCookie(page: Page): Promise<void> {
  await page.context().clearCookies({ name: accessTokenCookieName });

  expect(await getCookieNames(page)).not.toContain(accessTokenCookieName);
  expect(await getCookieNames(page)).toContain(refreshTokenCookieName);
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
  await expect(page.getByTestId("logout")).toBeVisible();
  await expect(page.getByTestId("bootstrap-authenticated")).toHaveText("True");
  await expect(page.getByTestId("bootstrap-email")).toHaveText(email);
}

for (const culture of blazorCultures) {
  test.describe("@comprehensive", () => {
    test.use({ locale: culture.locale });

    /**
     * Logout, its failure handling, a missing access token and a revoked session on the Blazor workspace.
     * - A rejected logout (500) shows the localized retry alert and keeps the user on the authenticated workspace
     * - A logout whose response is lost after the account API logged out shows the localized unconfirmed alert, sends no
     *   second logout on its own and keeps the workspace, because the browser still holds the session cookies
     * - Logging out again lands on the login page, leaves only the antiforgery cookie, the account API rejects the old
     *   session with 401 and the authenticated home then redirects to login
     * - A deleted access token cookie with a valid refresh token cookie is refreshed by the gateway on reload with no
     *   visible effect, and the cookie is set again. This proves the gateway's missing-cookie refresh path only; the
     *   refresh of an actually expired access token is covered by the gateway's middleware tests with signed tokens
     * - An ordinary logout in a second browser revokes that browser's session only
     * - Revoking the first browser's exact session from a third browser through the sessions API, then forcing the gateway
     *   to refresh by deleting the first browser's access token cookie, answers the next API call with 401 Revoked and
     *   lands on the localized "Session ended" page, whose login link reaches the login page without a redirect loop
     */
    test(`should handle logout and its failures, access token refresh and session revocation in ${culture.locale}`, async ({ page, browser }) => {
      const context = createTestContext(page);
      const email = uniqueEmail();
      const secondContext = await browser.newContext({ locale: culture.locale, baseURL: blazorUrl(), ignoreHTTPSErrors: true });
      const secondPage = await secondContext.newPage();
      const secondTestContext = createTestContext(secondPage);
      const thirdContext = await browser.newContext({ locale: culture.locale, baseURL: blazorUrl(), ignoreHTTPSErrors: true });
      const thirdPage = await thirdContext.newPage();
      const thirdTestContext = createTestContext(thirdPage);

      // === LOGOUT ===
      await step("Sign up and log out while the account API rejects the logout & verify the retry state keeps the session")(async () => {
        await signUpThroughBlazor(page, email);
        await page.route(logoutRoute, (route) => route.fulfill({ status: 500, contentType: "application/problem+json", body: "{}" }));

        await page.getByTestId("logout").click();

        await expect(page.getByTestId("logout-failed")).toHaveText(culture.logoutFailed);
        await expect(page.getByRole("alert").filter({ hasText: culture.logoutFailed })).toBeVisible();
        await expectNetworkErrors(context, [500]);
        await expect(page.getByTestId("logout")).toBeEnabled();
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

        await page.getByTestId("logout").click();

        await expect(page.getByTestId("logout-unconfirmed")).toHaveText(culture.logoutUnconfirmed);
        await expect(page.getByTestId("logout-failed")).toHaveCount(0);
        await expect(page.getByTestId("logout")).toBeEnabled();
        expect(logoutRequests.count).toBe(1);
        await expectBlazorUrl(page, "app");
        await page.unroute(logoutRoute);
      })();

      await step("Log out again & verify the login page, only the antiforgery cookie and no usable session remain")(async () => {
        await logOutThroughBlazor(page);

        await expect(page.getByRole("heading", { name: culture.hiWelcomeBack })).toBeVisible();
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

      await step("Revoke the first browser's session from a third browser & verify the session ended page")(async () => {
        const firstSessionId = await getCurrentSessionIdThroughAccountApi(page);
        await logInThroughBlazor(thirdPage, email);
        expect((await getSessionsThroughAccountApi(thirdPage)).map((session) => session.id)).toContain(firstSessionId);
        expect(await getCurrentSessionIdThroughAccountApi(thirdPage)).not.toBe(firstSessionId);
        await revokeSessionThroughAccountApi(thirdPage, firstSessionId);
        await deleteAccessTokenCookie(page);
        const unauthorizedResponse = page.waitForResponse((response) => response.url().endsWith("/api/account/bootstrap") && response.status() === 401);
        await watchErrorBanner(page);

        await page.getByTestId("reload-bootstrap").click();

        expect(await (await unauthorizedResponse).headerValue("x-unauthorized-reason")).toBe("Revoked");
        await expectBlazorUrl(page, "error");
        await expect(page).toHaveURL((url) => url.searchParams.get("error") === "session_revoked");
        await expect(page.getByRole("heading", { name: culture.sessionEnded })).toBeVisible();
        await expectErrorBannerNeverShown(page, context);
        await expect(page.getByTestId("error-message")).toHaveText(culture.sessionRevoked);
        await expect(page.getByText(culture.logInAgainToContinue)).toBeVisible();
        await expectNetworkErrors(context, [401]);
        expect(await getCookieNames(page)).not.toContain(refreshTokenCookieName);
      })();

      await step("Follow the login link on the session ended page & verify the login page")(async () => {
        await expect(page.getByTestId("error-login")).toHaveText(culture.logIn);
        await page.getByTestId("error-login").click();

        await expectBlazorUrl(page, "login");
        await expect(page.getByRole("heading", { name: culture.hiWelcomeBack })).toBeVisible();
        await page.goto(blazorPath("app"));
        await expectBlazorUrl(page, "login");
      })();

      await assertNoUnexpectedErrors(secondTestContext);
      await assertNoUnexpectedErrors(thirdTestContext);
      await thirdContext.close();
      await secondContext.close();
    });
  });
}
