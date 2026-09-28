import { expect, type Page } from "@playwright/test";
import { signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { blazorBackOfficeUrl, openBlazorBackOffice } from "@blazor/e2e/back-office";
import {
  administeredFeatureFlagKey,
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
 * The flag's activation and rollout are global, and every browser and culture project runs this specification at the same
 * time, so the writes run under a hold on the flag (withFeatureFlagHold): each project finds the flag in the state it left,
 * sets it inactive at 0 % as the reconciler creates it, and puts back the state it found before it releases the hold. The
 * precondition and the teardown go through the React back office's administrator document; the steps under test go through
 * the Blazor back office only. The app user is created by the test, so the evaluation it reads belongs to no other run.
 */
test.describe("@smoke", () => {
  /**
   * The Blazor back office's feature flag list and flag detail:
   * - As admin, the list shows the flag in the user flags and a row opens the flag's detail
   * - Activate, a percentage outside 0 to 100 refused before any call, a rollout to 100 % and Deactivate, each confirmed or
   *   reported as the account API stored it, while a new app user's evaluated flag follows the activation
   * - As user, the list and the detail offer no action, and each direct action call is refused with 403
   */
  test("should activate a flag, change its rollout and deactivate it as admin and offer a user no action", async ({ page, browser }) => {
    // The writes wait while the other projects hold the flag
    test.slow();
    createTestContext(page);
    const texts = blazorTexts();
    let userId = "";

    await step("Sign up through Blazor & read the new app user's id")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail());
      userId = (await readBootstrapUser(page))!.id;

      expect(userId.length).toBeGreaterThan(0);
    })();

    const admin = await openBlazorBackOffice(browser, "admin", "back-office/feature-flags", blazorLocale());
    const reactAdmin = await signInToBackOfficeAsAdmin(browser);
    try {
      await trackPolicyViolations(admin.page);

      await step("Open the feature flag list as admin & find the flag in the user flags")(async () => {
        await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.backOfficeFeatureFlags);

        await expect(admin.page.getByRole("heading", { level: 2, name: texts.backOfficeUserFlags })).toBeVisible();
        await expect(flagList(admin.page, texts.backOfficeUserFlags).getByRole("row").filter({ hasText: texts.experimentalUiFlagName })).toHaveCount(1);
      })();

      await withFeatureFlagHold(administeredFeatureFlagKey, async () => {
        const foundState = await readFeatureFlagStateThroughBackOffice(reactAdmin.page, administeredFeatureFlagKey);
        try {
          await step("Set the flag inactive at 0 % & open it from the list")(async () => {
            await setFeatureFlagStateThroughBackOffice(reactAdmin.page, administeredFeatureFlagKey, { isActive: false, rolloutPercentage: 0 });
            await admin.page.reload();

            await flagList(admin.page, texts.backOfficeUserFlags).getByRole("row").filter({ hasText: texts.experimentalUiFlagName }).getByRole("cell").first().click();

            await expect(admin.page).toHaveURL(blazorBackOfficeUrl(`back-office/feature-flags/${administeredFeatureFlagKey}`));
            await expect(admin.page.getByRole("heading", { level: 1 })).toHaveText(texts.experimentalUiFlagName);
            await expect(admin.page.getByTestId("feature-flag-status")).toHaveAttribute("data-status", "inactive");
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
            await expectNoPolicyViolations(admin.page);
          })();
        } finally {
          await setFeatureFlagStateThroughBackOffice(reactAdmin.page, administeredFeatureFlagKey, foundState);
        }
      });
    } finally {
      await reactAdmin.context.close();
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
   */
  test("should toggle the deleted flags, return from a flag to the list and show an unknown key as not found", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();

    const admin = await openBlazorBackOffice(browser, "admin", "back-office/feature-flags", blazorLocale());
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
    } finally {
      await admin.context.close();
    }
  });
});
