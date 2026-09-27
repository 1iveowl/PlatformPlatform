import { expect } from "@playwright/test";
import { signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { openBlazorBackOffice, readTenantAbInclusionPin } from "@blazor/e2e/back-office";
import { readBootstrapUser } from "@blazor/e2e/external-login";
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
});
