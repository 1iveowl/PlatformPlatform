import { expect, type Page } from "@playwright/test";
import { signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { blazorBackOfficeUrl, openBlazorBackOffice } from "@blazor/e2e/back-office";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

const billingEventsWithRows = {
  totalCount: 1,
  pageSize: 25,
  totalPages: 1,
  currentPageOffset: 0,
  billingEvents: [
    {
      id: "bilevt_01JABCDEFGHJKMNPQRSTVWXYZ0",
      tenantId: "1",
      tenantName: "Test Organization",
      tenantLogoUrl: null,
      country: "DK",
      eventType: "SubscriptionCreated",
      fromPlan: null,
      toPlan: "Standard",
      amountDelta: 29.0,
      previousAmount: null,
      newAmount: 29.0,
      committedMrr: 29.0,
      currency: "DKK",
      occurredAt: "2026-05-11T00:00:00Z"
    }
  ]
};
const billingEventsEmpty = { totalCount: 0, pageSize: 25, totalPages: 0, currentPageOffset: 0, billingEvents: [] };

/**
 * Expect a list of the back office to have finished loading, with rows or empty
 * @param page Playwright page instance on a back-office list
 * @param testId The list's test id
 */
async function expectListLoaded(page: Page, testId: string): Promise<void> {
  await expect(page.getByTestId(testId).and(page.locator(':is([data-list-state="ready"], [data-list-state="empty"])'))).toBeVisible();
}

test.describe("@smoke", () => {
  /**
   * The Blazor back office's billing events list, the React billing-events-flows steps on the back-office host with the
   * account API's billing events answer stubbed as there: the side menu opens the list, the MRR impact and All views and the
   * account search reach the URL under the React names, the empty state shows for a search without matches, the Account
   * column sorts, and the dashboard's recent billing events card links to the list. No step raises a policy violation.
   */
  test("should render billing-events list, filter by event type and account, and navigate via dashboard view-all", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const admin = await openBlazorBackOffice(browser, "admin", "back-office", blazorLocale());
    createTestContext(admin.page);
    await trackPolicyViolations(admin.page);
    let billingEventsResponse: object = billingEventsWithRows;
    await admin.page.route("**/api/back-office/billing-events?**", async (route) => {
      await route.fulfill({ status: 200, contentType: "application/json", json: billingEventsResponse });
    });
    try {
      // === SIDE MENU NAVIGATION ===

      await step("Click side-menu Billing events & verify route lands on the list with heading and table")(async () => {
        await admin.page.getByRole("link", { name: texts.backOfficeBillingEvents, exact: true }).first().click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/billing-events"));
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.backOfficeBillingEvents);
        await expectListLoaded(admin.page, "billing-events-grid");
        const table = admin.page.getByRole("grid", { name: texts.backOfficeBillingEvents }).or(admin.page.getByRole("table", { name: texts.backOfficeBillingEvents }));
        await expect(table).toBeVisible();
        await expect(table.getByRole("columnheader", { name: texts.backOfficeAccount })).toBeVisible();
        await expect(table.getByRole("columnheader", { name: texts.backOfficeEvent })).toBeVisible();
        await expect(table.getByRole("columnheader", { name: texts.backOfficeOccurred })).toBeVisible();
        await expect(admin.page.getByTestId("billing-events-row")).toHaveText("Test Organization");
      })();

      // === FILTERS ===

      await step("Apply MRR impact view & verify URL reflects selection and the list stays loaded")(async () => {
        await admin.page.getByRole("button", { name: texts.backOfficeMrrImpact, exact: true }).click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/billing-events?view=mrr"));
        await expect(admin.page.getByRole("button", { name: texts.backOfficeMrrImpact, exact: true })).toHaveAttribute("aria-pressed", "true");
        await expectListLoaded(admin.page, "billing-events-grid");
      })();

      await step("Reload & verify the MRR impact view is restored")(async () => {
        await admin.page.reload();

        await expect(admin.page.getByRole("button", { name: texts.backOfficeMrrImpact, exact: true })).toHaveAttribute("aria-pressed", "true");
        await expectListLoaded(admin.page, "billing-events-grid");
      })();

      await step("Click the All view & verify URL returns to the base list")(async () => {
        await admin.page.getByRole("button", { name: texts.backOfficeAllView, exact: true }).click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/billing-events"));
      })();

      await step("Type a non-matching account search & verify URL reflects the search and the empty state appears")(async () => {
        billingEventsResponse = billingEventsEmpty;
        await admin.page.getByRole("textbox", { name: texts.search }).fill("zzz-no-match-account-xyz");

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/billing-events?search=zzz-no-match-account-xyz"));
        await expect(admin.page.getByText(texts.backOfficeNoBillingEventsMatchFilters)).toBeVisible();
      })();

      await step("Clear search & verify URL returns to the base list")(async () => {
        billingEventsResponse = billingEventsWithRows;
        await admin.page.getByRole("textbox", { name: texts.search }).fill("");

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/billing-events"));
        await expect(admin.page.getByTestId("billing-events-row")).toHaveText("Test Organization");
      })();

      // === SORT TOGGLE ===

      await step("Click the Account column's sort & verify the sort reaches the URL")(async () => {
        await admin.page.getByTestId("billing-events-grid").locator('[data-list-sort="TenantName"]').click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/billing-events?orderBy=TenantName"));
      })();

      // === DASHBOARD VIEW ALL ===

      await step("Navigate back to the dashboard & verify the recent billing events card is present")(async () => {
        await admin.page.goto(blazorBackOfficeUrl("back-office"));

        await expect(admin.page.getByText(texts.backOfficeRecentBillingEvents, { exact: true })).toBeVisible();
      })();

      await step("Click the recent billing events card's View all link & verify it lands on the list")(async () => {
        await admin.page.getByTestId("recent-stripe-events-view-all").click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/billing-events"));
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.backOfficeBillingEvents);
        await expectNoPolicyViolations(admin.page);
      })();
    } finally {
      await admin.context.close();
    }
  });

  /**
   * The unstubbed invoices list and an account's two billing tabs against the local stack's stored billing data: the
   * invoices list loads, its refunds view reaches the URL and survives a reload, and a new account's Invoices and Billing
   * events tabs read that account's payment history and that account's events only.
   */
  test("should list invoices by view and show an account's invoices and billing events tabs", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const accountName = `Billing ${Math.random().toString(36).slice(2, 10)}`;

    await step("Sign up through Blazor with a unique account name & land in the workspace")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail(), accountName);
    })();

    const admin = await openBlazorBackOffice(browser, "admin", "back-office/invoices", blazorLocale());
    await trackPolicyViolations(admin.page);
    try {
      await step("Open the invoices list & choose the refunds view, which a reload restores")(async () => {
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.backOfficeInvoices);
        await expectListLoaded(admin.page, "invoices-grid");

        await admin.page.getByRole("button", { name: texts.backOfficeRefundsAndCreditNotes, exact: true }).click();
        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/invoices?view=refunds"));
        await admin.page.reload();

        await expect(admin.page.getByRole("button", { name: texts.backOfficeRefundsAndCreditNotes, exact: true })).toHaveAttribute("aria-pressed", "true");
        await expectListLoaded(admin.page, "invoices-grid");
      })();

      await step("Open the new account from the accounts list & see its Invoices tab read that account's payment history")(async () => {
        await admin.page.goto(blazorBackOfficeUrl("back-office/accounts"));
        await admin.page.getByRole("textbox", { name: texts.search }).fill(accountName);
        await admin.page.getByTestId("accounts-grid").getByRole("row").filter({ hasText: accountName }).click();
        await admin.page.getByRole("link", { name: texts.backOfficeOpenAccount }).click();
        await expect(admin.page.getByTestId("back-office-account-detail")).toHaveAttribute("data-state", "loaded");
        const tenantId = admin.page.url().match(/accounts\/(\d+)/)?.[1];
        expect(tenantId).toBeTruthy();

        const paymentHistory = admin.page.waitForRequest((request) => request.url().includes(`/api/back-office/tenants/${tenantId}/payment-history`));
        await admin.page.getByTestId("account-tab-invoices").click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl(`back-office/accounts/${tenantId}?tab=invoices`));
        await paymentHistory;
        await expectListLoaded(admin.page, "account-invoices-grid");

        const accountEvents = admin.page.waitForRequest((request) => request.url().includes(`/api/back-office/billing-events?TenantId=${tenantId}&`));
        await admin.page.getByTestId("account-tab-billing-events").click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl(`back-office/accounts/${tenantId}?tab=billing-events`));
        await accountEvents;
        await expectListLoaded(admin.page, "account-billing-events-grid");
        await expectNoPolicyViolations(admin.page);
      })();
    } finally {
      await admin.context.close();
    }
  });
});
