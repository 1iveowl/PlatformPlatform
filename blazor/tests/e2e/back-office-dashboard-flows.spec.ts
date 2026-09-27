import { expect } from "@playwright/test";
import { signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { blazorBackOfficeUrl, openBlazorBackOffice } from "@blazor/e2e/back-office";
import { blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

test.describe("@smoke", () => {
  /**
   * The Blazor back office's dashboard on the back-office host, with the local stack's subscription setting on:
   * - The admin sees the dashboard in the project's culture, with the KPI tiles and the four recent activity cards
   * - The menu shows the Billing group, and the billing tiles and cards are present
   * - A view-all link whose list is not built yet lands on the back office's not-found page
   * - The access-denied page renders inside the back office
   * - Log out in the user menu leaves through the platform's logout, after which the dashboard asks for a login again
   */
  test("should show the dashboard with its tiles and recent activity and sign out through the platform", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();

    await step("Sign up through Blazor so the local stack has a recent signup and a recent login")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail());
    })();

    const admin = await openBlazorBackOffice(browser, "admin", "back-office", blazorLocale());
    try {
      await step("Open the dashboard as admin & verify the heading, the tiles and the menu in the project's culture")(async () => {
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.backOfficeDashboard);
        await expect(admin.page.getByTestId("dashboard-tiles")).toHaveAttribute("data-state", "loaded");
        await expect(admin.page.getByTestId("kpi-total-accounts")).toContainText(texts.backOfficeTotalAccounts);
        await expect(admin.page.getByTestId("kpi-total-accounts-value")).toHaveText(/^\d[\d.,]*$/);
        await expect(admin.page.getByTestId("kpi-blended-mrr")).toBeVisible();
        await expect(admin.page.getByTestId("kpi-total-revenue")).toBeVisible();
        await expect(admin.page.getByTestId("back-office-user-name")).toHaveText("Admin");
        await expect(admin.page.getByTestId("sidebar-nav-invoices")).toBeVisible();
        await expect(admin.page.locator(".app-sidebar")).toContainText(texts.backOfficeBilling);
      })();

      await step("Verify the recent signups and logins list the local stack's data & the billing cards have loaded")(async () => {
        await expect(admin.page.getByTestId("recent-signups")).toContainText(texts.backOfficeRecentSignups);
        await expect(admin.page.getByTestId("recent-signups")).toHaveAttribute("data-state", "loaded");
        await expect(admin.page.getByTestId("recent-signups-row").first()).toBeVisible();
        await expect(admin.page.getByTestId("recent-logins")).toHaveAttribute("data-state", "loaded");
        await expect(admin.page.getByTestId("recent-logins-row").first()).toBeVisible();
        await expect(admin.page.getByTestId("recent-payments")).toHaveAttribute("data-state", /^(loaded|empty)$/);
        await expect(admin.page.getByTestId("recent-stripe-events")).toHaveAttribute("data-state", /^(loaded|empty)$/);
      })();

      await step("Change the period to 7 days & verify the tiles reload for it")(async () => {
        await admin.page.getByTestId("dashboard-period-7").click();

        await expect(admin.page.getByTestId("dashboard-period-7")).toHaveAttribute("aria-pressed", "true");
        await expect(admin.page.getByTestId("dashboard-tiles")).toHaveAttribute("data-state", "loaded");
      })();

      await step("Follow View all on recent payments & verify the back office's not-found page")(async () => {
        await admin.page.getByTestId("recent-payments-view-all").click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/invoices"));
        await expect(admin.page.getByTestId("back-office-not-found")).toContainText(texts.pageNotFound);
      })();

      await step("Open the access-denied page & verify it renders inside the back office")(async () => {
        await admin.page.goto(blazorBackOfficeUrl("back-office/access-denied"));

        await expect(admin.page.getByTestId("back-office-access-denied")).toContainText(texts.backOfficeNoAccess);
      })();

      await step("Log out from the user menu & verify the dashboard asks for a login again")(async () => {
        await admin.page.goto(blazorBackOfficeUrl("back-office"));
        await expect(admin.page.getByTestId("back-office-shell")).toHaveAttribute("data-identity-state", "loaded");
        await admin.page.getByTestId("back-office-user-menu").click();
        await expect(admin.page.getByTestId("back-office-identity-admin")).toBeVisible();
        await admin.page.getByTestId("back-office-log-out").click();
        await admin.page.waitForURL((url) => !url.pathname.startsWith("/.auth/logout"));

        await admin.page.goto(blazorBackOfficeUrl("back-office"));
        await expect(admin.page).toHaveURL(/\/login\?returnPath=/);
      })();
    } finally {
      await admin.context.close();
    }
  });
});
