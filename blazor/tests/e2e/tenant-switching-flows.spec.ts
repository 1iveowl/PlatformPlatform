import { expect, type Locator, type Page } from "@playwright/test";
import { deleteUserThroughAccountApi, findUserThroughAccountApi, inviteUsersThroughAccountApi } from "@blazor/e2e/account-api";
import { logInThroughBlazor, logOutButton, logOutThroughBlazor, signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, blazorUrl, expectBlazorUrl } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { expectUsersListLoaded, gotoUsersPage, openUserActions, profilePane, userRow } from "@blazor/e2e/users";
import { getBaseUrl } from "@shared/e2e/utils/constants";
import { assertNoUnexpectedErrors, createTestContext, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

declare global {
  interface Window {
    __documentBeforeSwitch?: boolean;
  }
}

/**
 * The cookie the Blazor client writes after a tenant switch and the login verification page sends as the preferred tenant
 */
const preferredTenantCookieName = "preferred-tenant";

/**
 * A tenant id no user of the test has a membership in
 */
const inaccessibleTenantId = "999999999999";

/**
 * Expect the header to have finished loading the user's tenants and to show the given current tenant
 * @param page Playwright page instance on an interactive authenticated Blazor page
 * @param tenantName The name of the current tenant
 */
async function expectTenantsLoaded(page: Page, tenantName: string): Promise<void> {
  await expect(page.getByTestId("account-header")).toHaveAttribute("data-tenants-state", "loaded");
  await expect(page.getByTestId("header-tenant-name")).toHaveText(tenantName);
}

/**
 * Route patterns and paths of the requests the switch failure steps inject failures into
 */
const preferredTenantModuleRoute = (url: URL) => /\/js\/preferred-tenant(\.[a-z0-9]+)?\.js$/.test(url.pathname);
const bootstrapRoute = "**/api/account/bootstrap";
const switchTenantPath = "/api/account/authentication/switch-tenant";
const logoutPath = "/api/account/authentication/logout";
const userByIdPathPattern = /^\/api\/account\/users\/(?!me$)[^/]+$/;
const staleTitle = "Stale title from the previous tenant";

/**
 * Mark the current document, so a later check can tell whether a full document navigation replaced it
 */
async function markDocument(page: Page): Promise<void> {
  await page.evaluate(() => {
    window.__documentBeforeSwitch = true;
  });
}

/**
 * Track whether the tenant switch request has been answered, from now on
 */
function trackSwitchResponse(page: Page): { value: boolean } {
  const switchResponded = { value: false };
  page.on("response", (response) => {
    if (new URL(response.url()).pathname === switchTenantPath) switchResponded.value = true;
  });
  return switchResponded;
}

/**
 * Record every state-changing account API request the page sends after the tenant switch was answered
 */
function trackWritesAfterSwitch(page: Page): { requests: string[] } {
  const writes = { requests: [] as string[] };
  const switchResponded = trackSwitchResponse(page);
  page.on("request", (request) => {
    const path = new URL(request.url()).pathname;
    if (switchResponded.value && request.method() !== "GET" && path.startsWith("/api/")) writes.requests.push(`${request.method()} ${path}`);
  });
  return writes;
}

/**
 * The tenant switcher, shown to a user with more than one tenant
 */
function tenantSwitcher(page: Page): Locator {
  return page.getByRole("navigation", { name: blazorTexts().switchAccount, exact: true });
}

/**
 * The switcher's button for a tenant, named by the tenant's name and, for the current tenant, a hidden current marker
 */
function switchTenantButton(page: Page, tenantName: string): Locator {
  return tenantSwitcher(page).getByRole("button", { name: tenantName });
}

async function getPreferredTenantCookie(page: Page): Promise<string | undefined> {
  const cookies = await page.context().cookies(getBaseUrl());
  return cookies.find((cookie) => cookie.name === preferredTenantCookieName)?.value;
}

test.describe("@comprehensive", () => {
  /**
   * The single-tab tenant journey of the React tenant switching specification on the Blazor header. The second tenant's
   * owner invites the user through the account API, because the Blazor edition has no invite dialog yet.
   * - A user with one tenant sees the tenant name and no tenant switcher
   * - Logout lands on the login page, and the authenticated home then redirects to login
   * - A user with two tenants sees "Switch account" with both tenants and the current one marked
   * - Switching accepts the invitation, loads the authenticated home as a new document under the new tenant and
   *   remembers the tenant as the next login's preference
   * - The users page after a switch lists only the new tenant's users, and the header stays on the new tenant across pages
   * - A switch still loads the new tenant as a new document when the preference module fails to load (the preference
   *   stays unchanged), and when the new document's bootstrap read answers 500 or loses its connection
   * - While the preference module is held after a successful switch, a delayed profile read of the previous tenant is
   *   discarded instead of shown, logout and the switcher are disabled and no state-changing request is sent; releasing
   *   the module loads the new tenant and remembers it
   * - Logout and switch clicked in the same task send only the logout and land on the login page
   * - The preferred tenant is used after logout and login; an inaccessible preference and a preference for a tenant the
   *   user was removed from both fall back to the remaining tenant
   */
  test("should handle tenant switching, logout and the tenant preference", async ({ page, browser }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const suffix = Date.now().toString().slice(-6);
    const primaryTenantName = `Primary ${suffix}`;
    const secondaryTenantName = `Secondary ${suffix}`;
    const userEmail = uniqueBlazorEmail();
    const secondaryOwnerEmail = `owner-${uniqueBlazorEmail()}`;
    const ownerContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const ownerPage = await ownerContext.newPage();
    const ownerTestContext = createTestContext(ownerPage);
    let primaryTenantId = "";
    let secondaryTenantId = "";

    // === SINGLE TENANT ===
    await step("Create single tenant & verify dropdown is hidden")(async () => {
      await signUpThroughBlazor(page, userEmail, primaryTenantName);

      await expectTenantsLoaded(page, primaryTenantName);
      await expect(tenantSwitcher(page)).toHaveCount(0);
      primaryTenantId = (await page.getByTestId("bootstrap-tenant-id").textContent())!.trim();
      expect(primaryTenantId).not.toBe("");
    })();

    await step("Logout from primary tenant & verify redirect to login page")(async () => {
      await logOutThroughBlazor(page);

      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
      await page.goto(blazorPath("app"));
      await expectBlazorUrl(page, "login");
    })();

    // === SECOND TENANT ===
    await step("Create second tenant and invite the user through the account API & verify the invitation")(async () => {
      await signUpThroughBlazor(ownerPage, secondaryOwnerEmail, secondaryTenantName);
      await inviteUsersThroughAccountApi(ownerPage, [userEmail]);

      secondaryTenantId = (await ownerPage.getByTestId("bootstrap-tenant-id").textContent())!.trim();
      expect(secondaryTenantId).not.toBe(primaryTenantId);
      expect((await findUserThroughAccountApi(ownerPage, userEmail)).email).toBe(userEmail);
    })();

    // === TENANT SWITCHING ===
    await step("Login with multiple tenants & verify tenant switching UI displays correctly")(async () => {
      await logInThroughBlazor(page, userEmail);

      await expectTenantsLoaded(page, primaryTenantName);
      const switcher = tenantSwitcher(page);
      await expect(switcher).toContainText(texts.switchAccount);
      await expect(switcher.getByRole("button")).toHaveCount(2);
      await expect(switchTenantButton(page, primaryTenantName)).toHaveAttribute("aria-current", "true");
      await expect(switchTenantButton(page, primaryTenantName)).toBeDisabled();
      await expect(switchTenantButton(page, primaryTenantName)).toContainText(primaryTenantName);
      await expect(switchTenantButton(page, secondaryTenantName)).toBeEnabled();
      await expect(switchTenantButton(page, secondaryTenantName)).toContainText(secondaryTenantName);
      await expect(switchTenantButton(page, secondaryTenantName)).not.toHaveAttribute("aria-current", "true");
    })();

    await step("Accept invitation by switching account & verify a new document on the invited tenant")(async () => {
      await page.evaluate(() => {
        window.__documentBeforeSwitch = true;
      });

      await switchTenantButton(page, secondaryTenantName).click();

      await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(secondaryTenantId);
      await expectBlazorUrl(page, "app");
      await expectTenantsLoaded(page, secondaryTenantName);
      await expect(switchTenantButton(page, secondaryTenantName)).toHaveAttribute("aria-current", "true");
      expect(await page.evaluate(() => window.__documentBeforeSwitch)).toBeUndefined();
      expect(await getPreferredTenantCookie(page)).toBe(secondaryTenantId);
    })();

    await step("Navigate across pages & verify tenant context remains consistent")(async () => {
      await gotoUsersPage(page);

      await expectUsersListLoaded(page, 2);
      await expect(userRow(page, secondaryOwnerEmail)).toBeVisible();
      await expect(userRow(page, userEmail)).toBeVisible();
      await expectTenantsLoaded(page, secondaryTenantName);

      await page.getByRole("link", { name: texts.profile, exact: true }).click();
      await expectBlazorUrl(page, "user/profile");
      await expect(page.getByRole("heading", { name: texts.profile, exact: true })).toBeVisible();
      await expectTenantsLoaded(page, secondaryTenantName);

      await page.getByRole("link", { name: texts.workspace, exact: true }).click();
      await expectBlazorUrl(page, "app");
      await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(secondaryTenantId);
    })();

    // === SWITCH FAILURE HANDLING ===
    await step("Switch to the primary tenant while the preference module fails to load & verify a new document on it")(async () => {
      await page.route(preferredTenantModuleRoute, (route) => route.abort("failed"));
      await markDocument(page);

      await switchTenantButton(page, primaryTenantName).click();

      await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(primaryTenantId);
      await expectBlazorUrl(page, "app");
      expect(await page.evaluate(() => window.__documentBeforeSwitch)).toBeUndefined();
      expect(await getPreferredTenantCookie(page)).toBe(secondaryTenantId);
      await page.unroute(preferredTenantModuleRoute);
      await expectTenantsLoaded(page, primaryTenantName);
    })();

    await step("Switch to the secondary tenant while the next bootstrap read fails & verify the previous surface is replaced")(async () => {
      const switchResponded = trackSwitchResponse(page);
      await page.route(bootstrapRoute, (route) =>
        switchResponded.value ? route.fulfill({ status: 500, contentType: "application/problem+json", body: "{}" }) : route.continue()
      );
      await markDocument(page);

      await switchTenantButton(page, secondaryTenantName).click();

      await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(secondaryTenantId);
      expect(await page.evaluate(() => window.__documentBeforeSwitch)).toBeUndefined();
      await expectNetworkErrors(context, [500]);
      await page.unroute(bootstrapRoute);
      await page.reload();
      await expectTenantsLoaded(page, secondaryTenantName);
    })();

    await step("Switch to the primary tenant while the connection drops on the next bootstrap read & verify the previous surface is replaced")(async () => {
      const switchResponded = trackSwitchResponse(page);
      await page.route(bootstrapRoute, (route) => (switchResponded.value ? route.abort("internetdisconnected") : route.continue()));
      await markDocument(page);

      await switchTenantButton(page, primaryTenantName).click();

      await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(primaryTenantId);
      expect(await page.evaluate(() => window.__documentBeforeSwitch)).toBeUndefined();
      await page.unroute(bootstrapRoute);
      await page.reload();
      await expectTenantsLoaded(page, primaryTenantName);
      expect(await getPreferredTenantCookie(page)).toBe(primaryTenantId);
    })();

    await step("Switch tenant while an old tenant's profile read and the preference module are held & verify the old surface neither repopulates nor mutates")(async () => {
      await gotoUsersPage(page);
      await expectUsersListLoaded(page, 1);
      let releaseModule: () => void = () => {};
      const moduleReleased = new Promise<void>((resolve) => {
        releaseModule = resolve;
      });
      await page.route(preferredTenantModuleRoute, async (route) => {
        await moduleReleased;
        await route.continue();
      });
      const staleUser = { ...(await findUserThroughAccountApi(page, userEmail)), title: staleTitle };
      let heldProfileRead: { fulfill: () => Promise<void> } | undefined;
      await page.route(
        (url) => userByIdPathPattern.test(url.pathname),
        (route) => {
          heldProfileRead = { fulfill: () => route.fulfill({ status: 200, json: staleUser }) };
        }
      );
      const menu = await openUserActions(page, userEmail);
      await menu.getByRole("menuitem", { name: texts.viewProfile }).dispatchEvent("click");
      const pane = profilePane(page);
      await expect(pane).toHaveAttribute("data-status", "ready");
      await expect.poll(() => heldProfileRead).toBeDefined();
      await markDocument(page);
      const writesAfterSwitch = trackWritesAfterSwitch(page);

      const switchResponse = page.waitForResponse((response) => response.url().endsWith(switchTenantPath));
      await switchTenantButton(page, secondaryTenantName).click();
      expect((await switchResponse).status()).toBe(200);
      await expect(page.getByTestId("account-header")).toHaveAttribute("data-transition-status", "leaving");
      await heldProfileRead!.fulfill();

      await expect(pane).not.toHaveAttribute("data-status", "ready");
      await expect(page.getByText(staleTitle)).toHaveCount(0);
      await expect(logOutButton(page)).toBeDisabled();
      await expect(switchTenantButton(page, primaryTenantName)).toBeDisabled();
      expect(await page.evaluate(() => window.__documentBeforeSwitch)).toBe(true);
      expect(writesAfterSwitch.requests).toEqual([]);
      releaseModule();
      await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(secondaryTenantId);
      await expectBlazorUrl(page, "app");
      expect(await page.evaluate(() => window.__documentBeforeSwitch)).toBeUndefined();
      expect(await getPreferredTenantCookie(page)).toBe(secondaryTenantId);
      await page.unrouteAll({ behavior: "ignoreErrors" });
    })();

    await step("Click logout and switch account in the same task & verify only the logout is sent and no session remains")(async () => {
      await expectTenantsLoaded(page, secondaryTenantName);
      const transitionRequests: string[] = [];
      page.on("request", (request) => {
        const path = new URL(request.url()).pathname;
        if (request.method() === "POST" && (path === logoutPath || path === switchTenantPath)) transitionRequests.push(path);
      });

      const logout = await logOutButton(page).elementHandle();
      const switchTenant = await switchTenantButton(page, primaryTenantName).elementHandle();

      await page.evaluate(
        ([logoutButton, switchButton]) => {
          (logoutButton as HTMLButtonElement).click();
          (switchButton as HTMLButtonElement).click();
        },
        [logout, switchTenant]
      );

      await expectBlazorUrl(page, "login");
      expect(transitionRequests).toEqual([logoutPath]);
      await logInThroughBlazor(page, userEmail);
      await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(secondaryTenantId);
    })();

    // === TENANT PREFERENCE ===
    await step("Logout and login again & verify tenant preference persists")(async () => {
      await logOutThroughBlazor(page);

      await logInThroughBlazor(page, userEmail);

      await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(secondaryTenantId);
      await expectTenantsLoaded(page, secondaryTenantName);
    })();

    await step("Login with a preference for an inaccessible tenant & verify login falls back to the first tenant")(async () => {
      await logOutThroughBlazor(page);
      await page.context().addCookies([{ name: preferredTenantCookieName, value: inaccessibleTenantId, url: getBaseUrl() }]);

      await logInThroughBlazor(page, userEmail);

      await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(primaryTenantId);
      await expectTenantsLoaded(page, primaryTenantName);
    })();

    await step("Remove the user from the preferred tenant and login & verify login lands on the remaining tenant")(async () => {
      await switchTenantButton(page, secondaryTenantName).click();
      await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(secondaryTenantId);
      await logOutThroughBlazor(page);
      const invitedUser = await findUserThroughAccountApi(ownerPage, userEmail);
      await deleteUserThroughAccountApi(ownerPage, invitedUser.id);

      await logInThroughBlazor(page, userEmail);

      await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(primaryTenantId);
      await expectTenantsLoaded(page, primaryTenantName);
      await expect(tenantSwitcher(page)).toHaveCount(0);
    })();

    await assertNoUnexpectedErrors(ownerTestContext);
    await ownerContext.close();
  });
});
