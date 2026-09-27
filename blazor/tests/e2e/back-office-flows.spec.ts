import { expect } from "@playwright/test";
import { signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { blazorBackOfficeUrl, openBlazorBackOffice, readTenantAbInclusionPin } from "@blazor/e2e/back-office";
import { readBootstrapUser } from "@blazor/e2e/external-login";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

test.describe("@smoke", () => {
  /**
   * The Blazor back-office placeholder on the back-office host:
   * - Without a back-office session the page lands on the platform's mock login
   * - As the admin identity the page shows the name and the admin marker, from the host and from the account API
   * - The admin sets and clears an account's A/B inclusion pin through the typed client, with the antiforgery check on
   * - As the user identity the marker is off and the account API refuses the same write
   */
  test("should serve the back-office placeholder to back-office identities only and let only an admin write", async ({ page, browser }) => {
    createTestContext(page);
    let tenantId = "";

    await step("Sign up through Blazor & read the new account's id")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail());
      tenantId = (await readBootstrapUser(page))!.tenantId;

      expect(tenantId).toMatch(/^\d+$/);
    })();

    const admin = await openBlazorBackOffice(browser, "admin");
    try {
      await step("Sign in as admin & verify the name and the admin marker from the host and the account API")(async () => {
        await expect(admin.page.getByTestId("back-office-name")).toHaveText("Admin");
        await expect(admin.page.getByTestId("back-office-admin-marker")).toHaveText("Admin");
        await expect(admin.page.getByTestId("back-office-api-name")).toHaveText("Admin");
        await expect(admin.page.getByTestId("back-office-api-admin")).toHaveText("Admin");
      })();

      await step("Pin the account to always on & verify the account API stored the pin")(async () => {
        await admin.page.getByRole("textbox", { name: "Account ID" }).fill(tenantId);
        const pinResponse = admin.page.waitForResponse((response) => response.url().endsWith(`/api/back-office/tenants/${tenantId}/ab-inclusion-pin`));
        await admin.page.getByRole("button", { name: "Pin the A/B inclusion to always on" }).click();

        expect((await pinResponse).ok()).toBe(true);
        await expect(admin.page.getByTestId("back-office-pin-result")).toHaveText("The A/B inclusion pin is saved.");
        expect(await readTenantAbInclusionPin(admin.page, tenantId)).toBe("AlwaysOn");
      })();

      await step("Clear the pin & verify the account API cleared it")(async () => {
        const clearResponse = admin.page.waitForResponse((response) => response.url().endsWith(`/api/back-office/tenants/${tenantId}/ab-inclusion-pin`));
        await admin.page.getByRole("button", { name: "Clear the A/B inclusion pin" }).click();

        expect((await clearResponse).ok()).toBe(true);
        await expect(admin.page.getByTestId("back-office-pin-result")).toHaveText("The A/B inclusion pin is cleared.");
        expect(await readTenantAbInclusionPin(admin.page, tenantId)).toBeNull();
      })();
    } finally {
      await admin.context.close();
    }

    const user = await openBlazorBackOffice(browser, "user");
    try {
      await step("Sign in as user & verify the admin marker is off")(async () => {
        await expect(user.page.getByTestId("back-office-name")).toHaveText("User");
        await expect(user.page.getByTestId("back-office-admin-marker")).toHaveText("Not admin");
        await expect(user.page.getByTestId("back-office-api-admin")).toHaveText("Not admin");
      })();

      await step("Try the same pin as user & verify the account API refuses it")(async () => {
        await user.page.getByRole("textbox", { name: "Account ID" }).fill(tenantId);
        const pinResponse = user.page.waitForResponse((response) => response.url().endsWith(`/api/back-office/tenants/${tenantId}/ab-inclusion-pin`));
        await user.page.getByRole("button", { name: "Pin the A/B inclusion to always on" }).click();

        expect((await pinResponse).status()).toBe(403);
        await expect(user.page.getByTestId("api-failure-toast")).toBeVisible();
        await expect(user.page.getByTestId("back-office-pin-result")).toHaveText("");
      })();
    } finally {
      await user.context.close();
    }
  });

  /**
   * The Blazor back office's golden path, the React back office's smoke case on the Blazor edition:
   * - Sign in to the back-office host through the mock login as admin and see the dashboard with its KPI tiles
   * - Open the accounts list from the side menu, find a new account and open its detail from the side pane
   * - The detail shows the account's name and its Overview tab with the owner who signed it up
   * - The Users and Feature flags tabs load their lists, the tab is in the URL and survives a reload
   * - A tenant id the account API does not know shows the back office's not-found state inside the shell
   * - Nothing on the way raises a policy violation or writes a style attribute
   */
  test("should sign in to the back office, render the dashboard and read an account's detail", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const email = uniqueBlazorEmail();
    const accountName = `Detail ${Math.random().toString(36).slice(2, 10)}`;

    await step("Sign up through Blazor with a unique account name & land in the workspace")(async () => {
      await signUpThroughBlazor(page, email, accountName);
    })();

    const admin = await openBlazorBackOffice(browser, "admin", "back-office", blazorLocale());
    await trackPolicyViolations(admin.page);
    try {
      await step("Sign in as admin & see the dashboard with its KPI tiles")(async () => {
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.backOfficeDashboard);
        await expect(admin.page.getByTestId("kpi-total-accounts")).toContainText(texts.backOfficeTotalAccounts);
      })();

      await step("Open the accounts list from the side menu & find the new account")(async () => {
        await admin.page.getByTestId("sidebar-nav-accounts").click();
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.backOfficeAccounts);

        await admin.page.getByRole("textbox", { name: texts.search }).fill(accountName);
        await expect(admin.page.getByTestId("accounts-grid")).toHaveAttribute("data-list-total-count", "1");
      })();

      await step("Open the account from the side pane & see its name, the Overview tab and its owner")(async () => {
        await admin.page.getByTestId("accounts-grid").getByRole("row").filter({ hasText: accountName }).getByRole("cell").first().click();
        await admin.page.getByRole("link", { name: texts.backOfficeOpenAccount }).click();

        await expect(admin.page).toHaveURL(/\/blazor\/back-office\/accounts\/\d+$/);
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(accountName);
        const tabs = admin.page.getByRole("navigation", { name: texts.backOfficeAccountSections });
        await expect(tabs.getByRole("link", { name: texts.backOfficeOverview })).toHaveAttribute("aria-current", "page");
        await expect(admin.page.getByRole("heading", { level: 2, name: texts.backOfficeOwners })).toBeVisible();
        await expect(admin.page.getByTestId("account-owner")).toContainText(email);
      })();

      await step("Open the Users tab & see the tab in the URL and the owner in the account's user list")(async () => {
        await admin.page.getByRole("navigation", { name: texts.backOfficeAccountSections }).getByRole("link", { name: texts.users }).click();

        await expect(admin.page).toHaveURL((url) => url.searchParams.get("tab") === "users");
        await expect(admin.page.getByRole("textbox", { name: texts.backOfficeSearchUsers })).toBeVisible();
        await expect(admin.page.getByTestId("account-users-grid")).toHaveAttribute("data-list-total-count", "1");
        await expect(admin.page.getByTestId("account-users-grid")).toContainText(email);
      })();

      await step("Reload the page & see the Users tab restored from the URL")(async () => {
        await admin.page.reload();

        await expect(admin.page.getByTestId("back-office-shell")).toHaveAttribute("data-identity-state", "loaded");
        const tabs = admin.page.getByRole("navigation", { name: texts.backOfficeAccountSections });
        await expect(tabs.getByRole("link", { name: texts.users })).toHaveAttribute("aria-current", "page");
        await expect(admin.page.getByTestId("account-users-grid")).toHaveAttribute("data-list-total-count", "1");
      })();

      await step("Open the Feature flags tab & see the account flags in the URL's tab")(async () => {
        await admin.page.getByRole("navigation", { name: texts.backOfficeAccountSections }).getByRole("link", { name: texts.backOfficeFeatureFlags }).click();

        await expect(admin.page).toHaveURL((url) => url.searchParams.get("tab") === "feature-flags");
        await expect(admin.page.getByRole("heading", { level: 2, name: texts.backOfficeAccountFlags })).toBeVisible();
        await expect(admin.page.getByTestId("account-account-flags-grid")).toHaveAttribute("data-list-state", "ready");
        await expectNoPolicyViolations(admin.page);
      })();

      await step("Open a tenant id the account API does not know & see the not-found state inside the back office")(async () => {
        await admin.page.goto(blazorBackOfficeUrl("back-office/accounts/1"));

        await expect(admin.page.getByTestId("back-office-shell")).toBeVisible();
        await expect(admin.page.getByTestId("back-office-account-detail")).toHaveAttribute("data-state", "notfound");
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.pageNotFound);
        await expectNoPolicyViolations(admin.page);
      })();
    } finally {
      await admin.context.close();
    }
  });
});
