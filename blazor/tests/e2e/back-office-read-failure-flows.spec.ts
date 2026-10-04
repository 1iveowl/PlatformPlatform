import { expect, type Page } from "@playwright/test";
import { signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { blazorBackOfficeUrl, openBlazorBackOffice } from "@blazor/e2e/back-office";
import { administeredFeatureFlagKey } from "@blazor/e2e/back-office-feature-flags";
import { readBootstrapUser } from "@blazor/e2e/external-login";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * The detail the stand-in failure carries, which the failed state shows as the account API returned it
 */
const simulatedFailure = "Simulated back-office read failure.";

/**
 * Answer the next back-office read that matches with a 500 problem, and let every later one reach the account API
 * @param page Playwright page instance signed in to the back office
 * @param matches Whether a request URL is the read to fail
 */
async function failNextRead(page: Page, matches: (url: URL) => boolean): Promise<void> {
  let hasFailed = false;
  await page.route(matches, async (route) => {
    if (hasFailed) {
      await route.continue();
      return;
    }
    hasFailed = true;
    await route.fulfill({ status: 500, contentType: "application/problem+json", json: { title: "Internal Server Error", status: 500, detail: simulatedFailure } });
  });
}

test.describe("@comprehensive", () => {
  /**
   * A back-office read that fails leaves its loading state for an alert with Try again, and Try again reads the same
   * selection again without navigation:
   * - The dashboard's KPI tiles, an MRR trend card and the recent signups card fail on their first read and load on retry;
   *   a 7 day MRR trend that fails is retried for 7 days
   * - The account detail and its Feature flags tab fail on their first read and load on retry
   * - The user detail and its Feature flags tab fail on their first read and load on retry
   * - The feature flag detail fails on its first read and loads the same flag on retry
   */
  test("should show a failed back-office read with Try again and load the same selection on retry", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();
    let tenantId = "";
    let userId = "";

    await step("Sign up through Blazor & read the new account and user ids")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail());
      const bootstrapUser = (await readBootstrapUser(page))!;
      tenantId = bootstrapUser.tenantId;
      userId = bootstrapUser.id;

      expect(tenantId).not.toBe("");
    })();

    const admin = await openBlazorBackOffice(browser, "admin", "back-office", blazorLocale());
    try {
      // === DASHBOARD ===
      await step("Open the dashboard with the KPI, MRR trend and recent signups reads failing once & retry each")(async () => {
        await failNextRead(admin.page, (url) => url.pathname.endsWith("/api/back-office/dashboard/kpis"));
        await failNextRead(admin.page, (url) => url.pathname.endsWith("/api/back-office/dashboard/mrr-trend"));
        await failNextRead(admin.page, (url) => url.pathname.endsWith("/api/back-office/dashboard/recent-signups"));

        await admin.page.goto(blazorBackOfficeUrl("back-office"));

        for (const testId of ["dashboard-tiles", "mrr-trend", "recent-signups"]) {
          const section = admin.page.getByTestId(testId);
          await expect(section).toHaveAttribute("data-state", "failed");
          await expect(section.getByRole("alert")).toContainText(texts.somethingWentWrong);
          await expect(section.getByRole("alert")).toContainText(simulatedFailure);

          await section.getByRole("button", { name: texts.tryAgain }).click();

          await expect(section).toHaveAttribute("data-state", "loaded");
          await expect(section.getByRole("alert")).toHaveCount(0);
        }
        await expect(admin.page.getByTestId("recent-signups-row").first()).toBeVisible();
        await expect(admin.page.getByTestId("mrr-trend-chart-row")).toHaveCount(30);
      })();

      await step("Change the period to 7 days with the MRR trend read failing once & retry it for 7 days")(async () => {
        await failNextRead(admin.page, (url) => url.pathname.endsWith("/api/back-office/dashboard/mrr-trend") && url.searchParams.get("Period") === "Last7Days");

        await admin.page.getByTestId("dashboard-period-7").click();

        const card = admin.page.getByTestId("mrr-trend");
        await expect(card).toHaveAttribute("data-state", "failed");

        await card.getByRole("button", { name: texts.tryAgain }).click();

        await expect(card).toHaveAttribute("data-state", "loaded");
        await expect(admin.page.getByTestId("mrr-trend-chart-row")).toHaveCount(7);
        await expect(admin.page.getByTestId("dashboard-period-7")).toHaveAttribute("aria-pressed", "true");
      })();

      // === ACCOUNT DETAIL ===
      await step("Open the account detail with its read failing once & retry it for the same account")(async () => {
        await failNextRead(admin.page, (url) => url.pathname.endsWith(`/api/back-office/tenants/${tenantId}`));

        await admin.page.goto(blazorBackOfficeUrl(`back-office/accounts/${tenantId}`));

        const detail = admin.page.getByTestId("back-office-account-detail");
        await expect(detail).toHaveAttribute("data-state", "failed");
        await expect(detail.getByRole("alert")).toContainText(texts.somethingWentWrong);

        await detail.getByRole("button", { name: texts.tryAgain }).click();

        await expect(detail).toHaveAttribute("data-state", "loaded");
        await expect(admin.page).toHaveURL(blazorBackOfficeUrl(`back-office/accounts/${tenantId}`));
        await expect(admin.page.getByTestId("account-tabs")).toBeVisible();
      })();

      await step("Open the account's Feature flags tab with its read failing once & retry it")(async () => {
        await failNextRead(admin.page, (url) => url.pathname.endsWith(`/api/back-office/tenants/${tenantId}/feature-flags`));

        await admin.page.goto(blazorBackOfficeUrl(`back-office/accounts/${tenantId}?tab=feature-flags`));

        const panel = admin.page.getByTestId("account-panel-feature-flags");
        await expect(panel.getByRole("alert")).toContainText(texts.somethingWentWrong);

        await panel.getByRole("button", { name: texts.tryAgain }).click();

        await expect(panel.getByRole("alert")).toHaveCount(0);
        await expect(panel.getByTestId("account-account-flags-grid")).toHaveAttribute("data-list-state", "ready");
      })();

      // === USER DETAIL ===
      await step("Open the user detail with its read failing once & retry it for the same user")(async () => {
        await failNextRead(admin.page, (url) => url.pathname.endsWith(`/api/back-office/users/${userId}`));

        await admin.page.goto(blazorBackOfficeUrl(`back-office/users/${userId}`));

        const detail = admin.page.getByTestId("back-office-user-detail");
        await expect(detail).toHaveAttribute("data-state", "failed");
        await expect(detail.getByRole("alert")).toContainText(simulatedFailure);

        await detail.getByRole("button", { name: texts.tryAgain }).click();

        await expect(detail).toHaveAttribute("data-state", "loaded");
        await expect(admin.page.getByTestId("user-tabs")).toBeVisible();
      })();

      await step("Open the user's Feature flags tab with its read failing once & retry it")(async () => {
        await failNextRead(admin.page, (url) => url.pathname.endsWith(`/api/back-office/users/${userId}/feature-flags`));

        await admin.page.goto(blazorBackOfficeUrl(`back-office/users/${userId}?tab=feature-flags`));

        const panel = admin.page.getByTestId("user-panel-feature-flags");
        await expect(panel.getByRole("alert")).toContainText(texts.somethingWentWrong);

        await panel.getByRole("button", { name: texts.tryAgain }).click();

        await expect(panel.getByRole("alert")).toHaveCount(0);
        await expect(panel.getByTestId("user-flags-grid")).toHaveAttribute("data-list-state", "ready");
      })();

      // === FEATURE FLAG DETAIL ===
      await step("Open a feature flag's detail with the flags read failing once & retry it for the same flag")(async () => {
        await failNextRead(admin.page, (url) => url.pathname.endsWith("/api/back-office/feature-flags") && url.searchParams.get("IncludeDeleted") === "true");

        await admin.page.goto(blazorBackOfficeUrl(`back-office/feature-flags/${administeredFeatureFlagKey}`));

        const detail = admin.page.getByTestId("back-office-feature-flag-detail");
        await expect(detail).toHaveAttribute("data-state", "failed");
        await expect(detail.getByRole("alert")).toContainText(texts.somethingWentWrong);

        await detail.getByRole("button", { name: texts.tryAgain }).click();

        await expect(detail).toHaveAttribute("data-state", "loaded");
        await expect(admin.page.getByTestId("feature-flag-header")).toBeVisible();
      })();
    } finally {
      await admin.context.close();
    }
  });
});
