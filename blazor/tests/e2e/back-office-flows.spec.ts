import { expect } from "@playwright/test";
import { signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { blazorBackOfficeUrl, openBlazorBackOffice, readTenantAbInclusionPin } from "@blazor/e2e/back-office";
import { mockProviderCookieName, readBootstrapUser } from "@blazor/e2e/external-login";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { getBackOfficeBaseUrl, getBaseUrl } from "@shared/e2e/utils/constants";
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

test.describe("@comprehensive", () => {
  /**
   * The Blazor back office's broader surface, the React back office's comprehensive case with its admin actions run:
   * - A back-office API call on the app host answers 404, and an app API call on the back-office host is not authenticated
   * - The drift banner shows while the drift summary reports drift; the accounts list filters and sorts through the URL
   * - The account opens from the side pane and its tabs switch; the admin sees the account actions
   * - Reconcile and disaster recovery each ask first and run against the local stack with the mock Stripe client, which
   *   refuses both for an account without a Stripe customer; the refusal is presented and the page stays loaded
   * - Reconcile that reports archived events continues into the disaster recovery confirmation (the account API's answers
   *   stubbed, since the local stack cannot produce archived events) and reports the recovery in a toast
   * - Feature flag rollouts pins the account first in rollouts, the header shows the pin after a reload, and Default clears it
   */
  test("should run the account admin actions, filter and sort accounts, show the drift banner and refuse cross-host requests", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const accountName = `Actions ${Math.random().toString(36).slice(2, 10)}`;
    let tenantId = "";

    await step("Sign up through Blazor with a unique account name & read the new account's id")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail(), accountName);
      tenantId = (await readBootstrapUser(page))!.tenantId;

      expect(tenantId).toMatch(/^\d+$/);
    })();

    await step("Call the back-office API on the app host with the app session & get 404")(async () => {
      const response = await page.request.get(`${getBaseUrl()}/api/back-office/me`, { maxRedirects: 0 });

      expect(response.status()).toBe(404);
    })();

    const admin = await openBlazorBackOffice(browser, "admin", "back-office", blazorLocale());
    await trackPolicyViolations(admin.page);
    try {
      await step("Call the app API on the back-office host with the back-office session & get 401")(async () => {
        const response = await admin.page.request.get(`${getBackOfficeBaseUrl()}/api/account/users/me`, { maxRedirects: 0 });

        expect(response.status()).toBe(401);
      })();

      await step("Load the dashboard while the drift summary reports one account & see the drift banner")(async () => {
        await admin.page.route("**/api/back-office/billing-drift/summary", (route) => route.fulfill({ status: 200, contentType: "application/json", json: { subscriptionsWithDriftCount: 1 } }));
        await admin.page.reload();

        await expect(admin.page.getByText(texts.backOfficeOneAccountWithDrift)).toBeVisible();
      })();

      await step("Open the accounts list, search for the new account and sort by name & see both in the URL")(async () => {
        await admin.page.getByTestId("sidebar-nav-accounts").click();
        await admin.page.getByRole("textbox", { name: texts.search }).fill(accountName);

        await expect(admin.page).toHaveURL((url) => url.searchParams.get("search") === accountName);
        await expect(admin.page.getByTestId("accounts-grid")).toHaveAttribute("data-list-total-count", "1");
        await admin.page.getByTestId("accounts-grid").locator('[data-list-sort="Name"]').click();
        await expect(admin.page).toHaveURL((url) => url.searchParams.get("orderBy") === "Name" && url.searchParams.get("search") === accountName);
      })();

      await step("Open the account from the side pane, switch tabs & see the account actions")(async () => {
        await admin.page.getByTestId("accounts-grid").getByRole("row").filter({ hasText: accountName }).getByRole("cell").first().click();
        await admin.page.getByRole("link", { name: texts.backOfficeOpenAccount }).click();

        const tabs = admin.page.getByRole("navigation", { name: texts.backOfficeAccountSections });
        await tabs.getByRole("link", { name: texts.users }).click();
        await expect(admin.page.getByTestId("account-users-grid")).toHaveAttribute("data-list-total-count", "1");
        await tabs.getByRole("link", { name: texts.backOfficeOverview }).click();
        await expect(tabs.getByRole("link", { name: texts.backOfficeOverview })).toHaveAttribute("aria-current", "page");
        await expect(admin.page.getByRole("group", { name: texts.backOfficeAccountActions })).toBeVisible();
      })();

      await step("Reconcile with Stripe against the mock client & see the confirmation and the presented refusal")(async () => {
        await admin.context.addCookies([{ name: mockProviderCookieName, value: "stripe", url: getBackOfficeBaseUrl() }]);
        await admin.page.getByTestId("account-reconcile").click();
        await expect(admin.page.getByRole("alertdialog", { name: texts.backOfficeReconcileWithStripeQuestion })).toBeVisible();

        const reconcileResponse = admin.page.waitForResponse((response) => response.url().endsWith(`/api/back-office/tenants/${tenantId}/reconcile-with-stripe`));
        await admin.page.getByTestId("reconcile-dialog-confirm").click();

        expect((await reconcileResponse).status()).toBe(400);
        await expect(admin.page.getByTestId("api-failure-toast")).toBeVisible();
        await expect(admin.page.getByTestId("reconcile-dialog")).toHaveAttribute("data-open", "false");
        await expect(admin.page.getByTestId("back-office-account-detail")).toHaveAttribute("data-state", "loaded");
      })();

      await step("Run disaster recovery against the mock client & see the confirmation and the presented refusal")(async () => {
        await admin.page.getByTestId("account-disaster-recovery").click();
        await expect(admin.page.getByRole("alertdialog", { name: texts.backOfficeDisasterRecoveryQuestion })).toBeVisible();

        const replayResponse = admin.page.waitForResponse((response) => response.url().endsWith(`/api/back-office/tenants/${tenantId}/replay-archived-stripe-events`));
        await admin.page.getByTestId("disaster-recovery-dialog-confirm").click();

        expect((await replayResponse).status()).toBe(400);
        await expect(admin.page.getByTestId("disaster-recovery-dialog")).toHaveAttribute("data-open", "false");
        await expect(admin.page.getByTestId("back-office-account-detail")).toHaveAttribute("data-state", "loaded");
      })();

      await step("Reconcile while the account API reports archived events & continue into disaster recovery")(async () => {
        const now = new Date().toISOString();
        await admin.page.route(`**/api/back-office/tenants/${tenantId}/reconcile-with-stripe`, (route) =>
          route.fulfill({
            status: 200,
            contentType: "application/json",
            json: { billingEventsAppended: 0, hasDriftDetected: false, driftDiscrepancyCount: 0, reconciledAt: now, archivedEventsAwaitingConfirmation: { count: 2, oldestOccurredAt: now, newestOccurredAt: now } }
          })
        );
        await admin.page.route(`**/api/back-office/tenants/${tenantId}/replay-archived-stripe-events`, (route) =>
          route.fulfill({ status: 200, contentType: "application/json", json: { billingEventsAppended: 2, replayedAt: now } })
        );
        await admin.page.getByTestId("account-reconcile").click();
        await admin.page.getByTestId("reconcile-dialog-confirm").click();

        await expect(admin.page.getByRole("alertdialog", { name: texts.backOfficeDisasterRecoveryQuestion })).toContainText("2");
        await admin.page.getByTestId("disaster-recovery-dialog-confirm").click();
        await expect(admin.page.getByTestId("disaster-recovery-toast")).toContainText(texts.backOfficeDisasterRecoveryComplete);
        await admin.page.unroute(`**/api/back-office/tenants/${tenantId}/reconcile-with-stripe`);
        await admin.page.unroute(`**/api/back-office/tenants/${tenantId}/replay-archived-stripe-events`);
      })();

      await step("Pin the account first in feature flag rollouts & see the pin in the header after a reload")(async () => {
        await admin.page.getByTestId("account-ab-inclusion-pin").click();
        await admin.page.getByTestId("ab-inclusion-pin-alwayson").check();
        const pinResponse = admin.page.waitForResponse((response) => response.url().endsWith(`/api/back-office/tenants/${tenantId}/ab-inclusion-pin`));
        await admin.page.getByTestId("ab-inclusion-pin-save").click();

        expect((await pinResponse).ok()).toBe(true);
        await expect(admin.page.getByTestId("ab-inclusion-pin-saved-toast")).toContainText(accountName);
        await expect(admin.page.getByTestId("account-detail-pin")).toHaveText(texts.backOfficeFirstInRollouts);
        await admin.page.reload();
        await expect(admin.page.getByTestId("account-detail-pin")).toHaveText(texts.backOfficeFirstInRollouts);
      })();

      await step("Reset feature flag rollouts to Default & see the pin cleared")(async () => {
        await admin.page.getByTestId("account-ab-inclusion-pin").click();
        await admin.page.getByTestId("ab-inclusion-pin-default").check();
        await admin.page.getByTestId("ab-inclusion-pin-save").click();

        await expect(admin.page.getByTestId("account-detail-pin")).toHaveCount(0);
        expect(await readTenantAbInclusionPin(admin.page, tenantId)).toBeNull();
        await expectNoPolicyViolations(admin.page);
      })();
    } finally {
      await admin.context.close();
    }
  });

  /**
   * A back-office identity outside the admins group on the account detail:
   * - The account opens, but no account action is offered
   * - A direct call to each admin action from that identity is refused by the account API with 403
   */
  test("should offer no account action to a back-office user and refuse the user's direct calls", async ({ page, browser }) => {
    createTestContext(page);
    let tenantId = "";

    await step("Sign up through Blazor & read the new account's id")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail());
      tenantId = (await readBootstrapUser(page))!.tenantId;
    })();

    const user = await openBlazorBackOffice(browser, "user", `back-office/accounts/${tenantId}`, blazorLocale());
    try {
      await step("Open the account as user & see no account action")(async () => {
        await expect(user.page.getByTestId("back-office-account-detail")).toHaveAttribute("data-state", "loaded");

        await expect(user.page.getByTestId("account-admin-actions")).toHaveCount(0);
      })();

      await step("Call each admin action directly as user & get 403 from the account API")(async () => {
        const statuses = await user.page.evaluate(async (id) => {
          const send = async (method: string, path: string, body?: string) =>
            (await fetch(`/api/back-office/tenants/${id}/${path}`, { method, body, headers: { "content-type": "application/json" } })).status;
          return [await send("POST", "reconcile-with-stripe"), await send("POST", "replay-archived-stripe-events"), await send("PUT", "ab-inclusion-pin", '{"abInclusionPin":"AlwaysOn"}')];
        }, tenantId);

        expect(statuses).toEqual([403, 403, 403]);
      })();
    } finally {
      await user.context.close();
    }
  });
});
