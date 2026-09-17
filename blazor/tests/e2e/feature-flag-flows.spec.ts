import { expect } from "@playwright/test";
import { changeUserRoleThroughAccountApi, expectAccountApiProblem, expectAccountApiValidationProblem, findUserThroughAccountApi, inviteUsersThroughAccountApi } from "@blazor/e2e/account-api";
import { logInInvitedUserThroughBlazor, signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import {
  type BackOfficeAdmin,
  ensureFeatureFlagsActivatedThroughBackOffice,
  featureFlagSwitch,
  getTenantConfigurableFeatureFlags,
  getUserConfigurableFeatureFlags,
  nonConfigurableUserFeatureFlagKey,
  readFeatureFlagActivationThroughBackOffice,
  setTenantFeatureFlagThroughAccountApi,
  setUserFeatureFlagThroughAccountApi,
  signInToBackOfficeAsAdmin,
  tenantFeatureFlagKey,
  tenantOverrideRoute,
  userFeatureFlagKey,
  userOverrideRoute
} from "@blazor/e2e/feature-flags";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { gotoAccountSettingsPage } from "@blazor/e2e/settings";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { accountApiMessages, blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { expectBlazorToast } from "@blazor/e2e/toast";
import { assertNoUnexpectedErrors, createTestContext, expectFeatureFlagHeaderResponse, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * The shared fixture this specification depends on is the global activation of the two configurable flags: both are
 * kill-switch flags the reconciler creates inactive, and a section hides a flag whose base row is inactive. Activation is
 * the only global state a test here writes, it only ever turns a flag on, and it is the same end state the React
 * specification and the feature flags browser harness leave behind, so the six browser and culture projects converge on it
 * instead of racing each other; deactivating it in a teardown is what would break a project still running. Everything else
 * a test writes is a tenant override or a user override on an account created by that test, from an email namespaced by
 * project, worker and retry, so no two runs share an identity, an account or an override. The teardown reads the
 * activation back through the back office and fails the test when the fixture was not left in that state.
 */
let backOfficeAdmin: BackOfficeAdmin | null = null;

const activatedFlags = { [tenantFeatureFlagKey]: true, [userFeatureFlagKey]: true };

test.afterEach(async ({ browser }) => {
  const admin = backOfficeAdmin ?? (await signInToBackOfficeAsAdmin(browser));
  backOfficeAdmin = null;

  try {
    const activation = await readFeatureFlagActivationThroughBackOffice(admin.page, [tenantFeatureFlagKey, userFeatureFlagKey]);

    expect(activation, "The specification leaves both configurable flags globally active, which is the state it found them in.").toEqual(activatedFlags);
  } finally {
    await admin.context.close();
  }
});

test.describe("@smoke", () => {
  /**
   * The tenant and user feature flag sections, the React edition's Features section on the account settings page and
   * Feature preferences section on the preferences page
   * - The owner's Features section names the account overview flag and describes it, with the switch off
   * - Turning it on and off sends the tenant override PUT whose response carries the refreshed x-user-feature-flags header
   *   with and without the flag, shows "Feature updated successfully" with the five-minute note, and moves the switch
   * - The same user turns the compact view flag on and off in Feature preferences through the user override, with the
   *   header and "Preference updated successfully"
   * - No securitypolicyviolation event and no style attribute on either document
   */
  test("should toggle the account and the user feature flag with the refreshed feature flag header", async ({ page, browser }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const email = uniqueBlazorEmail();
    const tenantSwitch = featureFlagSwitch(page, texts.accountOverviewFlagName);
    const userSwitch = featureFlagSwitch(page, texts.compactViewFlagName);

    await step("Activate the two configurable flags through the back office & verify both are globally active")(async () => {
      backOfficeAdmin = await signInToBackOfficeAsAdmin(browser);

      await ensureFeatureFlagsActivatedThroughBackOffice(backOfficeAdmin.page, [tenantFeatureFlagKey, userFeatureFlagKey]);

      expect(await readFeatureFlagActivationThroughBackOffice(backOfficeAdmin.page, [tenantFeatureFlagKey, userFeatureFlagKey])).toEqual(activatedFlags);
    })();

    // === ACCOUNT SETTINGS ===
    await step("Sign up an owner and open the account settings & verify the Features section and its switch")(async () => {
      await signUpThroughBlazor(page, email);

      await gotoAccountSettingsPage(page);

      await expect(page.getByRole("heading", { name: texts.features, exact: true })).toBeVisible();
      await expect(page.getByText(texts.featuresDescription, { exact: true })).toBeVisible();
      await expect(tenantSwitch).toHaveAccessibleDescription(texts.accountOverviewFlagDescription);
      await expect(tenantSwitch).not.toBeChecked();
    })();

    await step("Turn the account overview feature on & verify the header carries it, the toast and the switch")(async () => {
      await expectFeatureFlagHeaderResponse(page, tenantSwitch, {
        urlSubstring: tenantOverrideRoute(tenantFeatureFlagKey),
        expectedFlag: tenantFeatureFlagKey,
        shouldContain: true
      });

      await expectBlazorToast(page, { title: texts.featureUpdated, message: texts.featureFlagUpdatedDetail(texts.accountOverviewFlagName) });
      await expect(tenantSwitch).toBeChecked();
      expect(await getTenantConfigurableFeatureFlags(page)).toEqual({ [tenantFeatureFlagKey]: true });
    })();

    await step("Turn the account overview feature off & verify the header drops it, the toast and the switch")(async () => {
      await expectFeatureFlagHeaderResponse(page, tenantSwitch, {
        urlSubstring: tenantOverrideRoute(tenantFeatureFlagKey),
        expectedFlag: tenantFeatureFlagKey,
        shouldContain: false
      });

      await expectBlazorToast(page, { title: texts.featureUpdated, message: texts.featureFlagUpdatedDetail(texts.accountOverviewFlagName) });
      await expect(tenantSwitch).not.toBeChecked();
      expect(await getTenantConfigurableFeatureFlags(page)).toEqual({ [tenantFeatureFlagKey]: false });
      await expectNoPolicyViolations(page);
    })();

    // === USER PREFERENCES ===
    await step("Open the preferences page & verify the Feature preferences section and its switch")(async () => {
      await gotoBlazor(page, "user/preferences");

      await expect(page.getByRole("heading", { name: texts.featurePreferences, exact: true })).toBeVisible();
      await expect(page.getByText(texts.featurePreferencesDescription, { exact: true })).toBeVisible();
      await expect(userSwitch).toHaveAccessibleDescription(texts.compactViewFlagDescription);
      await expect(userSwitch).not.toBeChecked();
    })();

    await step("Turn the compact view preference on & verify the header carries it, the toast and the switch")(async () => {
      await expectFeatureFlagHeaderResponse(page, userSwitch, {
        urlSubstring: userOverrideRoute(userFeatureFlagKey),
        expectedFlag: userFeatureFlagKey,
        shouldContain: true
      });

      await expectBlazorToast(page, { title: texts.preferenceUpdated, message: texts.featureFlagUpdatedDetail(texts.compactViewFlagName) });
      await expect(userSwitch).toBeChecked();
      expect(await getUserConfigurableFeatureFlags(page)).toEqual({ [userFeatureFlagKey]: true });
    })();

    await step("Turn the compact view preference off & verify the header drops it, the toast and the switch")(async () => {
      await expectFeatureFlagHeaderResponse(page, userSwitch, {
        urlSubstring: userOverrideRoute(userFeatureFlagKey),
        expectedFlag: userFeatureFlagKey,
        shouldContain: false
      });

      await expectBlazorToast(page, { title: texts.preferenceUpdated, message: texts.featureFlagUpdatedDetail(texts.compactViewFlagName) });
      await expect(userSwitch).not.toBeChecked();
      expect(await getUserConfigurableFeatureFlags(page)).toEqual({ [userFeatureFlagKey]: false });
      await expectNoPolicyViolations(page);
    })();

    await assertNoUnexpectedErrors(context);
  });
});

test.describe("@comprehensive", () => {
  /**
   * Who may change a feature flag and what survives a reload, checked against the account API rather than the controls
   * - The owner's tenant override and user override both survive a full page load
   * - An admin and a member see no Features section on the account settings page, and the tenant override endpoint refuses
   *   both with the API's message; the owner's flag keeps the value it had, so a refusal changes nothing
   * - The user override endpoint refuses a flag the registry does not let a user configure, and refuses a tenant-scoped
   *   flag as a validation problem; the user's own flag keeps the value it had
   * - No securitypolicyviolation event and no style attribute on any document
   */
  test("should refuse feature flag changes from an admin, a member and through the wrong scope and keep the state across a reload", async ({ page, browser }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const ownerEmail = uniqueBlazorEmail();
    const adminEmail = `admin-${uniqueBlazorEmail()}`;
    const memberEmail = `member-${uniqueBlazorEmail()}`;
    const tenantSwitch = featureFlagSwitch(page, texts.accountOverviewFlagName);
    const userSwitch = featureFlagSwitch(page, texts.compactViewFlagName);

    await step("Activate the two configurable flags through the back office & verify both are globally active")(async () => {
      backOfficeAdmin = await signInToBackOfficeAsAdmin(browser);

      await ensureFeatureFlagsActivatedThroughBackOffice(backOfficeAdmin.page, [tenantFeatureFlagKey, userFeatureFlagKey]);

      expect(await readFeatureFlagActivationThroughBackOffice(backOfficeAdmin.page, [tenantFeatureFlagKey, userFeatureFlagKey])).toEqual(activatedFlags);
    })();

    // === OWNER ===
    await step("Sign up an owner, invite an admin and a member, then turn the account overview feature on & verify the switch")(async () => {
      await signUpThroughBlazor(page, ownerEmail);
      await inviteUsersThroughAccountApi(page, [adminEmail, memberEmail]);
      expect((await changeUserRoleThroughAccountApi(page, (await findUserThroughAccountApi(page, adminEmail)).id, "Admin")).status).toBe(200);

      await gotoAccountSettingsPage(page);
      await tenantSwitch.click();

      await expectBlazorToast(page, { title: texts.featureUpdated, message: texts.featureFlagUpdatedDetail(texts.accountOverviewFlagName) });
      await expect(tenantSwitch).toBeChecked();
    })();

    await step("Load the account settings again & verify the account overview switch is still on")(async () => {
      await gotoAccountSettingsPage(page);

      await expect(tenantSwitch).toBeChecked();
      await expectNoPolicyViolations(page);
    })();

    await step("Turn the compact view preference on and load the preferences page again & verify the switch is still on")(async () => {
      await gotoBlazor(page, "user/preferences");
      await userSwitch.click();
      await expectBlazorToast(page, { title: texts.preferenceUpdated, message: texts.featureFlagUpdatedDetail(texts.compactViewFlagName) });

      await gotoBlazor(page, "user/preferences");

      await expect(userSwitch).toBeChecked();
      await expectNoPolicyViolations(page);
    })();

    // === WRONG SCOPE ===
    await step("Send the owner's user override to a flag users cannot configure and to a tenant-scoped flag & verify both are refused and nothing changed")(async () => {
      expectAccountApiProblem(
        await setUserFeatureFlagThroughAccountApi(page, nonConfigurableUserFeatureFlagKey, true),
        403,
        accountApiMessages.featureFlagNotConfigurableByUsers(nonConfigurableUserFeatureFlagKey)
      );
      expectAccountApiValidationProblem(await setUserFeatureFlagThroughAccountApi(page, tenantFeatureFlagKey, true), "flagKey", accountApiMessages.featureFlagMustHaveUserScope);

      expect(await getUserConfigurableFeatureFlags(page)).toEqual({ [userFeatureFlagKey]: true });
      await expectNetworkErrors(context, [400]);
      await expectNetworkErrors(context, [403]);
    })();

    // === ADMIN ===
    const adminContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const adminPage = await adminContext.newPage();
    const adminTestContext = createTestContext(adminPage);
    await trackPolicyViolations(adminPage);

    await step("Log in as the admin and open the account settings & verify no Features section and a refused tenant override")(async () => {
      await logInInvitedUserThroughBlazor(adminPage, adminEmail, { firstName: "Ada", lastName: "Admin" });

      await gotoAccountSettingsPage(adminPage);

      await expect(adminPage.getByRole("heading", { name: texts.features, exact: true })).toHaveCount(0);
      await expect(featureFlagSwitch(adminPage, texts.accountOverviewFlagName)).toHaveCount(0);
      expectAccountApiProblem(await setTenantFeatureFlagThroughAccountApi(adminPage, tenantFeatureFlagKey, false), 403, accountApiMessages.onlyOwnersCanConfigureTenantFeatureFlags);
      await expectNetworkErrors(adminTestContext, [403]);
      await expectNoPolicyViolations(adminPage);
    })();

    // === MEMBER ===
    const memberContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const memberPage = await memberContext.newPage();
    const memberTestContext = createTestContext(memberPage);
    await trackPolicyViolations(memberPage);

    await step("Log in as the member and open the account settings & verify no Features section and a refused tenant override")(async () => {
      await logInInvitedUserThroughBlazor(memberPage, memberEmail, { firstName: "Mel", lastName: "Member" });

      await gotoAccountSettingsPage(memberPage);

      await expect(memberPage.getByRole("heading", { name: texts.features, exact: true })).toHaveCount(0);
      await expect(featureFlagSwitch(memberPage, texts.accountOverviewFlagName)).toHaveCount(0);
      expectAccountApiProblem(await setTenantFeatureFlagThroughAccountApi(memberPage, tenantFeatureFlagKey, false), 403, accountApiMessages.onlyOwnersCanConfigureTenantFeatureFlags);
      await expectNetworkErrors(memberTestContext, [403]);
      await expectNoPolicyViolations(memberPage);
    })();

    await step("Read the owner's tenant flags again after both refusals & verify the account overview feature is still on")(async () => {
      await gotoAccountSettingsPage(page);

      expect(await getTenantConfigurableFeatureFlags(page)).toEqual({ [tenantFeatureFlagKey]: true });
      await expect(tenantSwitch).toBeChecked();
    })();

    await assertNoUnexpectedErrors(context);
    await assertNoUnexpectedErrors(adminTestContext);
    await assertNoUnexpectedErrors(memberTestContext);
    await memberContext.close();
    await adminContext.close();
  });
});
