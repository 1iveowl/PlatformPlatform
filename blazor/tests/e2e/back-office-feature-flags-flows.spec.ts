import { expect, type Page } from "@playwright/test";
import { signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { blazorBackOfficeUrl, openBlazorBackOffice } from "@blazor/e2e/back-office";
import {
  administeredFeatureFlagKey,
  audienceFeatureFlagKey,
  readFeatureFlagStateThroughBackOffice,
  readUserFeatureFlagThroughBackOffice,
  setFeatureFlagStateThroughBackOffice,
  withFeatureFlagHold
} from "@blazor/e2e/back-office-feature-flags";
import { readBootstrapUser } from "@blazor/e2e/external-login";
import { signInToBackOfficeAsAdmin } from "@blazor/e2e/feature-flags";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { expectBlazorToast } from "@blazor/e2e/toast";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * The flag's list, found by its accessible name whichever of the two roles the list renders with
 */
function flagList(page: Page, name: string) {
  return page.getByRole("grid", { name }).or(page.getByRole("table", { name }));
}

/**
 * The audience list's row for one account or user and its state filter, read in the flag detail that stays mounted while the
 * flag's activation and rollout change
 */
function audience(page: Page, grid: "tenants" | "users", name: string) {
  const list = page.getByTestId(`feature-flag-${grid}-grid`);
  return {
    list,
    override: list.getByRole("switch", { name: blazorTexts().backOfficeOverrideFor(name) }),
    state: (label: string) => page.getByTestId(`feature-flag-${grid}-state-filter`).getByRole("button", { name: label, exact: true })
  };
}

/**
 * The flag's activation and rollout are global, and every browser and culture project runs this specification at the same
 * time, so the writes run under a hold on the flag (withFeatureFlagHold): each project finds the flag in the state it left,
 * sets it inactive at 0 % as the reconciler creates it, and puts back the state it found before it releases the hold. The
 * precondition and the teardown go through a second administrator document; the steps under test go through the first one
 * only. The app user is created by the test, so the evaluation it reads belongs to no other run.
 */
test.describe("@smoke", () => {
  /**
   * The Blazor back office's feature flag list and flag detail:
   * - As admin, the list shows the flag in the user flags and a row opens the flag's detail
   * - Activate, a percentage outside 0 to 100 refused before any call, a rollout to 100 % and Deactivate, each confirmed or
   *   reported as the account API stored it, while a new app user's evaluated flag follows the activation
   * - The users list below stays mounted through those actions and is read again after each one: the new app user's switch
   *   and the list's count under the Enabled and Disabled filters follow the flag, and the search and filter stay in the URL
   * - As user, the list and the detail offer no action, and each direct action call is refused with 403
   */
  test("should activate a flag, change its rollout and deactivate it as admin and offer a user no action", async ({ page, browser }) => {
    // The writes wait while the other projects hold the flag
    test.slow();
    createTestContext(page);
    const texts = blazorTexts();
    const userEmail = uniqueBlazorEmail();
    let userId = "";

    await step("Sign up through Blazor & read the new app user's id")(async () => {
      await signUpThroughBlazor(page, userEmail);
      userId = (await readBootstrapUser(page))!.id;

      expect(userId.length).toBeGreaterThan(0);
    })();

    const admin = await openBlazorBackOffice(browser, "admin", "back-office/feature-flags", blazorLocale());
    const stateAdmin = await signInToBackOfficeAsAdmin(browser);
    try {
      await trackPolicyViolations(admin.page);
      const users = audience(admin.page, "users", userEmail);

      await step("Open the feature flag list as admin & find the flag in the user flags")(async () => {
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.backOfficeFeatureFlags);

        await expect(admin.page.getByRole("heading", { level: 2, name: texts.backOfficeUserFlags })).toBeVisible();
        await expect(flagList(admin.page, texts.backOfficeUserFlags).getByRole("row").filter({ hasText: texts.experimentalUiFlagName })).toHaveCount(1);
      })();

      await withFeatureFlagHold(administeredFeatureFlagKey, async () => {
        const foundState = await readFeatureFlagStateThroughBackOffice(stateAdmin.page, administeredFeatureFlagKey);
        try {
          await step("Set the flag inactive at 0 % & open it from the list")(async () => {
            await setFeatureFlagStateThroughBackOffice(stateAdmin.page, administeredFeatureFlagKey, { isActive: false, rolloutPercentage: 0 });
            await admin.page.reload();

            await flagList(admin.page, texts.backOfficeUserFlags).getByRole("row").filter({ hasText: texts.experimentalUiFlagName }).getByRole("cell").first().click();

            await expect(admin.page).toHaveURL(blazorBackOfficeUrl(`back-office/feature-flags/${administeredFeatureFlagKey}`));
            await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.experimentalUiFlagName);
            await expect(admin.page.getByTestId("feature-flag-status")).toHaveAttribute("data-status", "inactive");
          })();

          await step("Show the flag's users in every state and search for the new app user & see the user's switch off")(async () => {
            await users.state(texts.backOfficeStateAll).click();
            await admin.page.getByRole("textbox", { name: texts.backOfficeSearchUsers }).fill(userEmail);

            await expect(admin.page).toHaveURL((url) => url.searchParams.get("usersState") === "All" && url.searchParams.get("usersSearch") === userEmail);
            await expect(users.list).toHaveAttribute("data-list-total-count", "1");
            await expect(users.override).toHaveAttribute("aria-checked", "false");
          })();

          await step("Activate the flag after its confirmation & see it active for the new app user")(async () => {
            await admin.page.getByRole("button", { name: texts.activateFlag, exact: true }).click();
            const dialog = admin.page.getByRole("alertdialog", { name: texts.activateFeatureFlag });
            await expect(dialog).toContainText(texts.experimentalUiFlagName);
            await expect(dialog).toContainText(texts.userFlagScope);
            await dialog.getByRole("button", { name: texts.activateFlag, exact: true }).click();

            await expectBlazorToast(admin.page, { title: texts.featureFlagActivated, message: texts.featureFlagChangesReachUsers });
            await expect(admin.page.getByTestId("feature-flag-status")).toHaveAttribute("data-status", "active");
            expect(await readFeatureFlagStateThroughBackOffice(admin.page, administeredFeatureFlagKey)).toEqual({ isActive: true, rolloutPercentage: 0 });
            expect(await readUserFeatureFlagThroughBackOffice(admin.page, userId, administeredFeatureFlagKey)).toBe(false);
            await expect(users.override).toHaveAttribute("aria-checked", "false");
          })();

          await step("Enter a rollout of 101 & see it refused before any call")(async () => {
            const requests: string[] = [];
            admin.page.on("request", (request) => requests.push(request.url()));
            await admin.page.getByLabel(texts.rolloutPercentageLabel).fill("101");
            await admin.page.getByRole("button", { name: texts.saveRolloutPercentage }).click();

            await expect(admin.page.getByText(texts.rolloutPercentageInvalid)).toBeVisible();
            await expect(admin.page.getByLabel(texts.rolloutPercentageLabel)).toHaveAttribute("aria-invalid", "true");
            expect(requests.filter((url) => url.endsWith("/rollout-percentage"))).toEqual([]);
          })();

          await step("Change the rollout to 100 % & see the new app user in the rollout")(async () => {
            await admin.page.getByLabel(texts.rolloutPercentageLabel).fill("100");
            await admin.page.getByRole("button", { name: texts.saveRolloutPercentage }).click();

            await expectBlazorToast(admin.page, { title: texts.rolloutPercentageUpdated, message: texts.featureFlagChangesReachUsers });
            await expect(admin.page.getByTestId("feature-flag-rollout")).toHaveText("100%");
            expect(await readFeatureFlagStateThroughBackOffice(admin.page, administeredFeatureFlagKey)).toEqual({ isActive: true, rolloutPercentage: 100 });
            expect(await readUserFeatureFlagThroughBackOffice(admin.page, userId, administeredFeatureFlagKey)).toBe(true);
            await expect(users.override).toHaveAttribute("aria-checked", "true");
          })();

          await step("Show only the enabled users & see the new app user counted")(async () => {
            await users.state(texts.backOfficeStateEnabled).click();

            await expect(users.state(texts.backOfficeStateEnabled)).toHaveAttribute("aria-pressed", "true");
            await expect(users.list).toHaveAttribute("data-list-total-count", "1");
            await expect(users.override).toHaveAttribute("aria-checked", "true");
          })();

          await step("Deactivate the flag after its confirmation & see it off for the new app user")(async () => {
            await admin.page.getByRole("button", { name: texts.deactivateFlag, exact: true }).click();
            const dialog = admin.page.getByRole("alertdialog", { name: texts.deactivateFeatureFlag });
            await expect(dialog).toContainText(texts.experimentalUiFlagName);
            await dialog.getByRole("button", { name: texts.deactivateFlag, exact: true }).click();

            await expectBlazorToast(admin.page, { title: texts.featureFlagDeactivated, message: texts.featureFlagChangesReachUsers });
            await expect(admin.page.getByTestId("feature-flag-status")).toHaveAttribute("data-status", "inactive");
            expect(await readFeatureFlagStateThroughBackOffice(admin.page, administeredFeatureFlagKey)).toEqual({ isActive: false, rolloutPercentage: 100 });
            expect(await readUserFeatureFlagThroughBackOffice(admin.page, userId, administeredFeatureFlagKey)).toBe(false);
            await expect(users.list).toHaveAttribute("data-list-total-count", "0");
            await expect(users.override).toHaveCount(0);
          })();

          await step("Show only the disabled users & see the new app user counted with the switch off and the search kept")(async () => {
            await users.state(texts.backOfficeStateDisabled).click();

            await expect(admin.page).toHaveURL((url) => url.searchParams.get("usersState") === "Disabled" && url.searchParams.get("usersSearch") === userEmail);
            await expect(users.list).toHaveAttribute("data-list-total-count", "1");
            await expect(users.override).toHaveAttribute("aria-checked", "false");
            await expectNoPolicyViolations(admin.page);
          })();
        } finally {
          await setFeatureFlagStateThroughBackOffice(stateAdmin.page, administeredFeatureFlagKey, foundState);
        }
      });
    } finally {
      await stateAdmin.context.close();
      await admin.context.close();
    }

    const user = await openBlazorBackOffice(browser, "user", `back-office/feature-flags/${administeredFeatureFlagKey}`, blazorLocale());
    try {
      await step("Open the flag as user & see its information but no action")(async () => {
        await expect(user.page.getByRole("heading", { level: 1 })).toHaveText(texts.experimentalUiFlagName);
        await expect(user.page.getByTestId("feature-flag-key")).toHaveText(administeredFeatureFlagKey);

        await expect(user.page.getByRole("button", { name: texts.activateFlag, exact: true })).toHaveCount(0);
        await expect(user.page.getByRole("button", { name: texts.deactivateFlag, exact: true })).toHaveCount(0);
        await expect(user.page.getByLabel(texts.rolloutPercentageLabel)).toHaveCount(0);
      })();

      await step("Call each flag action directly as user & get 403 from the account API")(async () => {
        const statuses = await user.page.evaluate(async (flagKey) => {
          const send = async (method: string, path: string, body?: string) =>
            (await fetch(`/api/back-office/feature-flags/${flagKey}${path}`, { method, body, headers: { "content-type": "application/json" } })).status;
          return [await send("PUT", "/activate"), await send("PUT", "/deactivate"), await send("PUT", "/rollout-percentage", '{"rolloutPercentage":50}'), await send("DELETE", "")];
        }, administeredFeatureFlagKey);

        expect(statuses).toEqual([403, 403, 403, 403]);
      })();
    } finally {
      await user.context.close();
    }
  });
});

test.describe("@comprehensive", () => {
  /**
   * The list's show-deleted toggle, the way back to the list and the not-found state:
   * - The toggle is pressed and reads the flags again, and pressing it again returns to the flags in code
   * - The detail's back link returns to the list
   * - A key that names no flag shows the not-found state inside the back office
   * - A tenant flag's accounts list stays mounted while the flag's rollout and activation change and is read again after each
   *   action: a new account's switch and the list's count under the Enabled and Disabled filters follow the flag, the search
   *   and filter stay in the URL, and the account's switch then acts on the state the list read again
   */
  test("should toggle the deleted flags, return to the list, show an unknown key as not found and keep a flag's accounts current", async ({ page, browser }) => {
    // The writes wait while the other projects hold the flag
    test.slow();
    createTestContext(page);
    const texts = blazorTexts();
    const accountName = `Audience ${Math.random().toString(36).slice(2, 10)}`;

    await step("Sign up through Blazor with a new account & land in the app")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail(), accountName);

      expect((await readBootstrapUser(page))!.id.length).toBeGreaterThan(0);
    })();

    const admin = await openBlazorBackOffice(browser, "admin", "back-office/feature-flags", blazorLocale());
    const stateAdmin = await signInToBackOfficeAsAdmin(browser);
    try {
      await step("Show the deleted flags & see the toggle pressed with the flags read again")(async () => {
        const toggle = admin.page.getByRole("button", { name: texts.showDeletedFlags });
        await expect(flagList(admin.page, texts.backOfficeUserFlags)).toBeVisible();

        await toggle.click();

        await expect(toggle).toHaveAttribute("aria-pressed", "true");
        await expect(flagList(admin.page, texts.backOfficeUserFlags).getByRole("row").filter({ hasText: texts.experimentalUiFlagName })).toHaveCount(1);
      })();

      await step("Hide the deleted flags again & see the toggle released")(async () => {
        const toggle = admin.page.getByRole("button", { name: texts.showDeletedFlags });

        await toggle.click();

        await expect(toggle).toHaveAttribute("aria-pressed", "false");
        await expect(flagList(admin.page, texts.backOfficeUserFlags)).toBeVisible();
      })();

      await step("Open the flag and follow the back link & land on the list")(async () => {
        await admin.page.goto(blazorBackOfficeUrl(`back-office/feature-flags/${administeredFeatureFlagKey}`));
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.experimentalUiFlagName);

        await admin.page.getByRole("link", { name: texts.backToFeatureFlags }).click();

        await expect(admin.page).toHaveURL(blazorBackOfficeUrl("back-office/feature-flags"));
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.backOfficeFeatureFlags);
      })();

      await step("Open a key that names no flag & see the not-found state inside the back office")(async () => {
        await admin.page.goto(blazorBackOfficeUrl("back-office/feature-flags/no-such-flag"));

        await expect(admin.page.getByTestId("back-office-shell")).toBeVisible();
        await expect(admin.page.getByTestId("back-office-feature-flag-detail")).toHaveAttribute("data-state", "notfound");
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.pageNotFound);
      })();

      // === A TENANT FLAG'S ACCOUNTS ===
      const accounts = audience(admin.page, "tenants", accountName);
      await withFeatureFlagHold(audienceFeatureFlagKey, async () => {
        const foundState = await readFeatureFlagStateThroughBackOffice(stateAdmin.page, audienceFeatureFlagKey);
        try {
          await step("Set the tenant flag inactive at 0 %, open it and search its accounts in every state & see the new account's switch off")(async () => {
            await setFeatureFlagStateThroughBackOffice(stateAdmin.page, audienceFeatureFlagKey, { isActive: false, rolloutPercentage: 0 });
            await admin.page.goto(blazorBackOfficeUrl(`back-office/feature-flags/${audienceFeatureFlagKey}`));
            await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.betaFeaturesFlagName);

            await accounts.state(texts.backOfficeStateAll).click();
            await admin.page.getByRole("textbox", { name: texts.backOfficeSearchAccountsOrOwners }).fill(accountName);

            await expect(admin.page).toHaveURL((url) => url.searchParams.get("tenantsState") === "All" && url.searchParams.get("tenantsSearch") === accountName);
            await expect(accounts.list).toHaveAttribute("data-list-total-count", "1");
            await expect(accounts.override).toHaveAttribute("aria-checked", "false");
          })();

          await step("Change the rollout to 100 % and show only the enabled accounts & see none while the flag is inactive")(async () => {
            await admin.page.getByLabel(texts.rolloutPercentageLabel).fill("100");
            await admin.page.getByRole("button", { name: texts.saveRolloutPercentage }).click();
            await expectBlazorToast(admin.page, { title: texts.rolloutPercentageUpdated, message: texts.featureFlagChangesReachUsers });
            await expect(accounts.override).toHaveAttribute("aria-checked", "false");

            await accounts.state(texts.backOfficeStateEnabled).click();

            await expect(accounts.state(texts.backOfficeStateEnabled)).toHaveAttribute("aria-pressed", "true");
            await expect(accounts.list).toHaveAttribute("data-list-total-count", "0");
          })();

          await step("Activate the tenant flag after its confirmation & see the new account counted as enabled")(async () => {
            await admin.page.getByRole("button", { name: texts.activateFlag, exact: true }).click();
            await admin.page.getByRole("alertdialog", { name: texts.activateFeatureFlag }).getByRole("button", { name: texts.activateFlag, exact: true }).click();

            await expectBlazorToast(admin.page, { title: texts.featureFlagActivated, message: texts.featureFlagChangesReachUsers });
            await expect(accounts.list).toHaveAttribute("data-list-total-count", "1");
            await expect(accounts.override).toHaveAttribute("aria-checked", "true");
          })();

          await step("Show only the disabled accounts and deactivate the tenant flag & see the new account counted as disabled")(async () => {
            await accounts.state(texts.backOfficeStateDisabled).click();
            await expect(accounts.list).toHaveAttribute("data-list-total-count", "0");

            await admin.page.getByRole("button", { name: texts.deactivateFlag, exact: true }).click();
            await admin.page.getByRole("alertdialog", { name: texts.deactivateFeatureFlag }).getByRole("button", { name: texts.deactivateFlag, exact: true }).click();

            await expectBlazorToast(admin.page, { title: texts.featureFlagDeactivated, message: texts.featureFlagChangesReachUsers });
            await expect(admin.page).toHaveURL((url) => url.searchParams.get("tenantsState") === "Disabled" && url.searchParams.get("tenantsSearch") === accountName);
            await expect(accounts.list).toHaveAttribute("data-list-total-count", "1");
            await expect(accounts.override).toHaveAttribute("aria-checked", "false");
          })();

          await step("Activate the tenant flag again, show every account and press the new account's switch & see it turned off")(async () => {
            await admin.page.getByRole("button", { name: texts.activateFlag, exact: true }).click();
            await admin.page.getByRole("alertdialog", { name: texts.activateFeatureFlag }).getByRole("button", { name: texts.activateFlag, exact: true }).click();
            await expectBlazorToast(admin.page, { title: texts.featureFlagActivated, message: texts.featureFlagChangesReachUsers });
            await expect(accounts.list).toHaveAttribute("data-list-total-count", "0");
            await accounts.state(texts.backOfficeStateAll).click();
            await expect(accounts.override).toHaveAttribute("aria-checked", "true");

            await accounts.override.click();

            await expectBlazorToast(admin.page, { title: texts.backOfficeFeatureFlagDisabledFor(texts.betaFeaturesFlagName, accountName), message: texts.featureFlagChangesReachUsers });
            await expect(accounts.override).toHaveAttribute("aria-checked", "false");
            await expect(accounts.list.getByText(texts.backOfficeManualOverride, { exact: true })).toBeVisible();
          })();
        } finally {
          await setFeatureFlagStateThroughBackOffice(stateAdmin.page, audienceFeatureFlagKey, foundState);
        }
      });
    } finally {
      await stateAdmin.context.close();
      await admin.context.close();
    }
  });
});
