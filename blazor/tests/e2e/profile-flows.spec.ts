import { expect } from "@playwright/test";
import { sendAccountApiRequest } from "@blazor/e2e/account-api";
import { signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { expectBlazorUrl } from "@blazor/e2e/routes";
import { blazorCultures } from "@blazor/e2e/texts";
import { expectBlazorValidationMessage } from "@blazor/e2e/validation";
import { blurActiveElement, createTestContext, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
import { uniqueEmail } from "@shared/e2e/utils/test-data";
import { step } from "@shared/e2e/utils/test-step-wrapper";

declare global {
  interface Window {
    __documentBeforeSave?: boolean;
  }
}

/**
 * The first name and last name limit of UpdateCurrentUserCommand's validator, plus one
 */
const tooLongName = "a".repeat(31);

/**
 * The title limit of UpdateCurrentUserCommand's validator, plus one
 */
const tooLongTitle = "t".repeat(51);

for (const culture of blazorCultures) {
  test.describe("@smoke", () => {
    test.use({ locale: culture.locale });

    /**
     * The profile page, the React edition's profile form without the avatar and identity verification sections.
     * - The page from the header shows email and role and the saved first name, last name and title
     * - Saving trimmed values shows "Profile updated successfully" and updates the header's name without a reload; the
     *   saved values survive the next page load
     * - Too long names, an empty last name, a whitespace-only first name and a too long title show the backend's limits as
     *   field messages and send no request; the account API rejects the same values itself
     * - Leaving with unsaved edits opens "Unsaved changes": Stay keeps the page and the edits, Leave navigates and discards
     * - No securitypolicyviolation event and no style attribute on any profile page document
     */
    test(`should edit the profile with validation, toast and unsaved changes guard in ${culture.locale}`, async ({ page }) => {
      const context = createTestContext(page);
      await trackPolicyViolations(page);
      const email = uniqueEmail();

      // === PROFILE ===
      await step("Sign up and open the profile from the header & verify email, role and saved names")(async () => {
        await signUpThroughBlazor(page, email);

        await page.getByTestId("nav-profile").click();

        await expectBlazorUrl(page, "user/profile");
        await expect(page.getByRole("heading", { name: culture.profile, exact: true })).toBeVisible();
        await expect(page.getByTestId("profile-email")).toHaveText(email);
        await expect(page.getByTestId("profile-role")).toHaveText(culture.owner);
        await expect(page.getByTestId("first-name")).toHaveValue("Blazor");
        await expect(page.getByTestId("last-name")).toHaveValue("User");
        await expect(page.getByTestId("header-user-name")).toHaveText("Blazor User");
      })();

      await step("Update first name, last name and title & verify the toast and the header name without a reload")(async () => {
        await page.evaluate(() => {
          window.__documentBeforeSave = true;
        });
        await page.getByTestId("first-name").fill("  Ada ");
        await page.getByTestId("last-name").fill("Lovelace");
        await page.getByTestId("title").fill("Engineer");

        await page.getByTestId("save-profile").click();

        const toast = page.getByTestId("toast-region").getByTestId("profile-updated-toast");
        await expect(toast.getByTestId("toast-title")).toHaveText(culture.profileUpdated);
        await expect(page.getByTestId("header-user-name")).toHaveText("Ada Lovelace");
        await expect(page.getByTestId("first-name")).toHaveValue("Ada");
        expect(await page.evaluate(() => window.__documentBeforeSave)).toBe(true);
        await toast.getByTestId("toast-dismiss").click();
        await expect(toast).toHaveCount(0);
      })();

      await step("Reload the profile page & verify the saved values and header name persist")(async () => {
        await expectNoPolicyViolations(page);

        await page.reload();

        await expect(page.getByTestId("first-name")).toHaveValue("Ada");
        await expect(page.getByTestId("last-name")).toHaveValue("Lovelace");
        await expect(page.getByTestId("title")).toHaveValue("Engineer");
        await expect(page.getByTestId("header-user-name")).toHaveText("Ada Lovelace");
      })();

      // === VALIDATION ===
      await step("Submit too long names and title & verify the field messages and no request")(async () => {
        const requests: string[] = [];
        page.on("request", (request) => {
          if (request.method() === "PUT") requests.push(request.url());
        });
        await page.getByTestId("first-name").fill(tooLongName);
        await page.getByTestId("last-name").fill(tooLongName);
        await page.getByTestId("title").fill(tooLongTitle);
        await blurActiveElement(page);

        await page.getByTestId("save-profile").click();

        await expectBlazorValidationMessage(page, culture.firstNameLength);
        await expectBlazorValidationMessage(page, culture.lastNameLength);
        await expectBlazorValidationMessage(page, culture.titleTooLong);
        expect(requests).toEqual([]);
      })();

      await step("Submit a whitespace-only first name and an empty last name & verify the field messages")(async () => {
        await page.getByTestId("first-name").fill("   ");
        await page.getByTestId("last-name").fill("");
        await page.getByTestId("title").fill("Engineer");
        await blurActiveElement(page);
        await expect(page.getByText(culture.titleTooLong)).toHaveCount(0);

        await page.getByTestId("save-profile").click();

        await expectBlazorValidationMessage(page, culture.firstNameLength);
        await expectBlazorValidationMessage(page, culture.lastNameLength);
        await expect(page.getByText(culture.titleTooLong)).toHaveCount(0);
      })();

      await step("Send too long values to the account API & verify the server rejects them")(async () => {
        const response = await sendAccountApiRequest(page, "PUT", "/api/account/users/me", { firstName: tooLongName, lastName: "   ", title: tooLongTitle });

        expect(response.status).toBe(400);
        expect(response.body).toContain("First name must be between 1 and 30 characters.");
        expect(response.body).toContain("Last name must be between 1 and 30 characters.");
        expect(response.body).toContain("Title must be no longer than 50 characters.");
        await expectNetworkErrors(context, [400]);
      })();

      // === UNSAVED CHANGES ===
      await step("Navigate away with unsaved edits and choose Stay & verify the page and edits are kept")(async () => {
        await page.getByTestId("first-name").fill("Grace");
        await page.getByTestId("last-name").fill("Hopper");

        await page.getByTestId("nav-app").click();

        await expect(page.getByRole("alertdialog", { name: culture.unsavedChanges })).toBeVisible();
        await page.getByRole("button", { name: culture.stay }).click();
        await expect(page.getByRole("alertdialog", { name: culture.unsavedChanges })).toBeHidden();
        await expectBlazorUrl(page, "user/profile");
        await expect(page.getByTestId("first-name")).toHaveValue("Grace");
      })();

      await step("Navigate away with unsaved edits and choose Leave & verify the edits are discarded")(async () => {
        await page.getByTestId("nav-app").click();
        await expect(page.getByRole("alertdialog", { name: culture.unsavedChanges })).toBeVisible();

        await page.getByRole("button", { name: culture.leave }).click();

        await expectBlazorUrl(page, "app");
        await expect(page.getByTestId("header-user-name")).toHaveText("Ada Lovelace");
        await expectNoPolicyViolations(page);
      })();
    });
  });
}
