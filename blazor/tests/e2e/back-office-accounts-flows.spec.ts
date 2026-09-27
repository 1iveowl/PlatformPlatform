import { expect, type Locator, type Page } from "@playwright/test";
import { signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { blazorBackOfficeUrl, openBlazorBackOffice } from "@blazor/e2e/back-office";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * The accounts list, whose data-list-* attributes carry its loading state and total count
 * @param page Playwright page instance on the accounts page
 */
function accountsGrid(page: Page): Locator {
  return page.getByTestId("accounts-grid");
}

/**
 * Expect the accounts list to have finished loading, with the given total when one is passed
 * @param page Playwright page instance on the accounts page
 * @param totalCount The expected number of accounts across all pages
 */
async function expectAccountsLoaded(page: Page, totalCount?: number): Promise<void> {
  await expect(accountsGrid(page).and(page.locator(':is([data-list-state="ready"], [data-list-state="empty"])'))).toBeVisible();
  if (totalCount !== undefined) await expect(accountsGrid(page)).toHaveAttribute("data-list-total-count", String(totalCount));
}

/**
 * The list row of the account with the given name on the loaded page
 * @param page Playwright page instance on the accounts page
 * @param accountName The account's name
 */
function accountRow(page: Page, accountName: string): Locator {
  return accountsGrid(page)
    .getByRole("row")
    .filter({ has: page.getByRole("cell") })
    .filter({ hasText: accountName });
}

/**
 * A toggle of the accounts toolbar's plan or status filter
 * @param page Playwright page instance on the accounts page
 * @param group The accessible name of the filter's group
 * @param name The accessible name of the toggle
 */
function filterToggle(page: Page, group: string, name: string): Locator {
  return page.getByRole("group", { name: group, exact: true }).getByRole("button", { name, exact: true });
}

test.describe("@smoke", () => {
  /**
   * The Blazor back office's accounts list on the back-office host, against the local stack's tenants and the mock Stripe
   * client's billing data, with the local subscription setting on:
   * - The admin searches for a new account and filters it by plan and status; each change reaches the URL under the React
   *   back office's parameter names and changes the rows, and a reload restores the filters
   * - Clear filters empties the URL; sorting by name starts descending and toggles to ascending, as in the React back office
   * - A row opens the side pane, which previews the account and links to its detail page
   * - With the drift summary reporting one account, the drift banner shows and its link opens the list filtered to drift
   * - Neither the list, the side pane nor the banner raises a policy violation or writes a style attribute
   * - A back-office user who is not an admin sees the same list, read-only
   */
  test("should filter, sort and preview accounts and follow the drift banner into the filtered list", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const accountName = `Accounts ${Math.random().toString(36).slice(2, 10)}`;

    await step("Sign up through Blazor with a unique account name & land in the workspace")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail(), accountName);
    })();

    const admin = await openBlazorBackOffice(browser, "admin", "back-office/accounts", blazorLocale());
    await trackPolicyViolations(admin.page);
    try {
      // === FILTERS ===

      await step("Open the accounts list as admin & see the heading and the loaded list")(async () => {
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.backOfficeAccounts);

        await expectAccountsLoaded(admin.page);
      })();

      await step("Search for the new account & see it as the only row with the search in the URL")(async () => {
        await admin.page.getByRole("textbox", { name: texts.search }).fill(accountName);

        await expect(admin.page).toHaveURL((url) => url.searchParams.get("search") === accountName);
        await expectAccountsLoaded(admin.page, 1);
        await expect(accountRow(admin.page, accountName)).toBeVisible();
      })();

      await step("Filter by the Basis plan and the Free status & keep the row with both filters in the URL")(async () => {
        await filterToggle(admin.page, texts.backOfficePlan, texts.backOfficePlanBasis).click();
        await expect(admin.page).toHaveURL((url) => url.searchParams.get("plans") === '["Basis"]');
        await filterToggle(admin.page, texts.backOfficeStatus, texts.backOfficeStatusFree).click();

        await expect(admin.page).toHaveURL((url) => url.searchParams.get("statuses") === '["Free"]');
        await expectAccountsLoaded(admin.page, 1);
        await expect(filterToggle(admin.page, texts.backOfficePlan, texts.backOfficePlanBasis)).toHaveAttribute("aria-pressed", "true");
      })();

      await step("Switch the status filter from Free to Active & see the empty state for the filters")(async () => {
        await filterToggle(admin.page, texts.backOfficeStatus, texts.backOfficeStatusActive).click();
        await expect(admin.page).toHaveURL((url) => url.searchParams.get("statuses") === '["Active","Free"]');
        await filterToggle(admin.page, texts.backOfficeStatus, texts.backOfficeStatusFree).click();

        await expect(admin.page).toHaveURL((url) => url.searchParams.get("statuses") === '["Active"]');
        await expectAccountsLoaded(admin.page, 0);
        await expect(admin.page.getByTestId("accounts-empty")).toContainText(texts.backOfficeNoAccountsMatchFilters);
      })();

      await step("Reload the page & see the search, plan and status filters restored from the URL")(async () => {
        await admin.page.reload();

        await expect(admin.page.getByTestId("back-office-shell")).toHaveAttribute("data-identity-state", "loaded");
        await expectAccountsLoaded(admin.page, 0);
        await expect(filterToggle(admin.page, texts.backOfficeStatus, texts.backOfficeStatusActive)).toHaveAttribute("aria-pressed", "true");
        await expect(filterToggle(admin.page, texts.backOfficePlan, texts.backOfficePlanBasis)).toHaveAttribute("aria-pressed", "true");
        await expect(admin.page.getByRole("textbox", { name: texts.search })).toHaveValue(accountName);
      })();

      await step("Clear the filters from the empty state & see the unfiltered list at the bare URL")(async () => {
        await admin.page.getByRole("button", { name: texts.backOfficeClearFilters }).click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/accounts"));
        await expectAccountsLoaded(admin.page);
        await expect(filterToggle(admin.page, texts.backOfficeStatus, texts.backOfficeStatusActive)).toHaveAttribute("aria-pressed", "false");
      })();

      // === SORT ===

      await step("Sort by name twice & see descending first and then ascending in the URL")(async () => {
        await accountsGrid(admin.page).locator('[data-list-sort="Name"]').click();
        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/accounts?orderBy=Name"));
        await accountsGrid(admin.page).locator('[data-list-sort="Name"]').click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/accounts?orderBy=Name&sortOrder=Ascending"));
        await expectAccountsLoaded(admin.page);
      })();

      // === PREVIEW ===

      await step("Search for the account and open its row & see the side pane preview it with the open account link")(async () => {
        await admin.page.getByRole("textbox", { name: texts.search }).fill(accountName);
        await expectAccountsLoaded(admin.page, 1);
        await accountRow(admin.page, accountName).getByRole("cell").first().click();

        await expect(admin.page.getByRole("heading", { level: 2, name: accountName })).toBeVisible();
        await expect(admin.page).toHaveURL((url) => url.searchParams.get("tenantId") !== null);
        const tenantId = new URL(admin.page.url()).searchParams.get("tenantId");
        await expect(admin.page.getByRole("link", { name: texts.backOfficeOpenAccount })).toHaveAttribute("href", `/blazor/back-office/accounts/${tenantId}`);
        await expectNoPolicyViolations(admin.page);
      })();

      await step("Close the preview & see the pane closed and the row key gone from the URL")(async () => {
        await admin.page.getByRole("button", { name: texts.backOfficeCloseAccountPreview }).click();

        await expect(admin.page.getByRole("heading", { level: 2, name: accountName })).toHaveCount(0);
        await expect(admin.page).toHaveURL((url) => url.searchParams.get("tenantId") === null);
      })();

      // === DRIFT BANNER ===

      await step("Load the dashboard while the drift summary reports one account & see the drift banner")(async () => {
        await admin.page.route("**/api/back-office/billing-drift/summary", (route) => route.fulfill({ status: 200, contentType: "application/json", json: { subscriptionsWithDriftCount: 1 } }));
        await admin.page.goto(blazorBackOfficeUrl("back-office"));

        await expect(admin.page.getByRole("alert").filter({ hasText: texts.backOfficeOneAccountWithDrift })).toBeVisible();
      })();

      await step("Follow the drift banner's link & land on the accounts list filtered to drift")(async () => {
        await admin.page.getByRole("alert").getByRole("link", { name: texts.backOfficeViewAccounts }).click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/accounts?driftDetected=true"));
        await expect(admin.page.getByTestId("accounts-drift-filter")).toContainText(texts.backOfficeDriftDetected);
        await expectAccountsLoaded(admin.page);
        await expectNoPolicyViolations(admin.page);
      })();

      await step("Clear the drift filter & see the list without it")(async () => {
        await admin.page.getByRole("button", { name: texts.backOfficeClearDriftFilter }).click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/accounts"));
        await expect(admin.page.getByTestId("accounts-drift-filter")).toHaveCount(0);
      })();
    } finally {
      await admin.context.close();
    }

    const user = await openBlazorBackOffice(browser, "user", "back-office/accounts", blazorLocale());
    try {
      await step("Open the accounts list as a back-office user & find the new account")(async () => {
        await user.page.getByRole("textbox", { name: texts.search }).fill(accountName);

        await expectAccountsLoaded(user.page, 1);
        await expect(accountRow(user.page, accountName)).toBeVisible();
      })();
    } finally {
      await user.context.close();
    }
  });
});
