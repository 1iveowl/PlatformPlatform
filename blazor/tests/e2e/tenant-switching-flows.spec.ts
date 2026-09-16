import { expect, type Page } from "@playwright/test";
import { deleteUserThroughAccountApi, findUserThroughAccountApi, inviteUsersThroughAccountApi } from "@blazor/e2e/account-api";
import { logInThroughBlazor, logOutThroughBlazor, signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, blazorUrl, expectBlazorUrl } from "@blazor/e2e/routes";
import { blazorCultures } from "@blazor/e2e/texts";
import { expectUsersListLoaded, gotoUsersPage, userRow } from "@blazor/e2e/users";
import { getBaseUrl } from "@shared/e2e/utils/constants";
import { assertNoUnexpectedErrors, createTestContext } from "@shared/e2e/utils/test-assertions";
import { uniqueEmail } from "@shared/e2e/utils/test-data";
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

function switchTenantButton(page: Page, tenantId: string) {
  return page.getByTestId("tenant-switcher").locator(`[data-testid="switch-tenant"][data-tenant-id="${tenantId}"]`);
}

async function getPreferredTenantCookie(page: Page): Promise<string | undefined> {
  const cookies = await page.context().cookies(getBaseUrl());
  return cookies.find((cookie) => cookie.name === preferredTenantCookieName)?.value;
}

for (const culture of blazorCultures) {
  test.describe("@comprehensive", () => {
    test.use({ locale: culture.locale });

    /**
     * The single-tab tenant journey of the React tenant switching specification on the Blazor header. The second tenant's
     * owner invites the user through the account API, because the Blazor edition has no invite dialog yet.
     * - A user with one tenant sees the tenant name and no tenant switcher
     * - Logout lands on the login page, and the authenticated home then redirects to login
     * - A user with two tenants sees "Switch account" with both tenants and the current one marked
     * - Switching accepts the invitation, loads the authenticated home as a new document under the new tenant and
     *   remembers the tenant as the next login's preference
     * - The users page after a switch lists only the new tenant's users, and the header stays on the new tenant across pages
     * - The preferred tenant is used after logout and login; an inaccessible preference and a preference for a tenant the
     *   user was removed from both fall back to the remaining tenant
     */
    test(`should handle tenant switching, logout and the tenant preference in ${culture.locale}`, async ({ page, browser }) => {
      createTestContext(page);
      await trackPolicyViolations(page);
      const suffix = Date.now().toString().slice(-6);
      const primaryTenantName = `Primary ${suffix}`;
      const secondaryTenantName = `Secondary ${suffix}`;
      const userEmail = uniqueEmail();
      const secondaryOwnerEmail = `owner-${uniqueEmail()}`;
      const ownerContext = await browser.newContext({ locale: culture.locale, baseURL: blazorUrl(), ignoreHTTPSErrors: true });
      const ownerPage = await ownerContext.newPage();
      const ownerTestContext = createTestContext(ownerPage);
      let primaryTenantId = "";
      let secondaryTenantId = "";

      // === SINGLE TENANT ===
      await step("Create single tenant & verify dropdown is hidden")(async () => {
        await signUpThroughBlazor(page, userEmail, primaryTenantName);

        await expectTenantsLoaded(page, primaryTenantName);
        await expect(page.getByTestId("tenant-switcher")).toHaveCount(0);
        primaryTenantId = (await page.getByTestId("bootstrap-tenant-id").textContent())!.trim();
        expect(primaryTenantId).not.toBe("");
      })();

      await step("Logout from primary tenant & verify redirect to login page")(async () => {
        await logOutThroughBlazor(page);

        await expect(page.getByRole("heading", { name: culture.hiWelcomeBack })).toBeVisible();
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
        const switcher = page.getByTestId("tenant-switcher");
        await expect(switcher).toContainText(culture.switchAccount);
        await expect(switcher.getByTestId("switch-tenant")).toHaveCount(2);
        await expect(switchTenantButton(page, primaryTenantId)).toHaveAttribute("aria-current", "true");
        await expect(switchTenantButton(page, primaryTenantId)).toBeDisabled();
        await expect(switchTenantButton(page, primaryTenantId)).toContainText(primaryTenantName);
        await expect(switchTenantButton(page, secondaryTenantId)).toBeEnabled();
        await expect(switchTenantButton(page, secondaryTenantId)).toContainText(secondaryTenantName);
        await expect(switchTenantButton(page, secondaryTenantId)).not.toHaveAttribute("aria-current", "true");
      })();

      await step("Accept invitation by switching account & verify a new document on the invited tenant")(async () => {
        await page.evaluate(() => {
          window.__documentBeforeSwitch = true;
        });

        await switchTenantButton(page, secondaryTenantId).click();

        await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(secondaryTenantId);
        await expectBlazorUrl(page, "app");
        await expectTenantsLoaded(page, secondaryTenantName);
        await expect(switchTenantButton(page, secondaryTenantId)).toHaveAttribute("aria-current", "true");
        expect(await page.evaluate(() => window.__documentBeforeSwitch)).toBeUndefined();
        expect(await getPreferredTenantCookie(page)).toBe(secondaryTenantId);
      })();

      await step("Navigate across pages & verify tenant context remains consistent")(async () => {
        await gotoUsersPage(page);

        await expectUsersListLoaded(page, 2);
        await expect(userRow(page, secondaryOwnerEmail)).toBeVisible();
        await expect(userRow(page, userEmail)).toBeVisible();
        await expectTenantsLoaded(page, secondaryTenantName);

        await page.getByTestId("nav-profile").click();
        await expectBlazorUrl(page, "user/profile");
        await expect(page.getByRole("heading", { name: culture.profile, exact: true })).toBeVisible();
        await expectTenantsLoaded(page, secondaryTenantName);

        await page.getByTestId("nav-app").click();
        await expectBlazorUrl(page, "app");
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
        await switchTenantButton(page, secondaryTenantId).click();
        await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(secondaryTenantId);
        await logOutThroughBlazor(page);
        const invitedUser = await findUserThroughAccountApi(ownerPage, userEmail);
        await deleteUserThroughAccountApi(ownerPage, invitedUser.id);

        await logInThroughBlazor(page, userEmail);

        await expect(page.getByTestId("bootstrap-tenant-id")).toHaveText(primaryTenantId);
        await expectTenantsLoaded(page, primaryTenantName);
        await expect(page.getByTestId("tenant-switcher")).toHaveCount(0);
      })();

      await assertNoUnexpectedErrors(ownerTestContext);
      await ownerContext.close();
    });
  });
}
