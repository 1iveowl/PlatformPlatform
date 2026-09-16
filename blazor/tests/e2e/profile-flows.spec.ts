import { expect } from "@playwright/test";
import { sendAccountApiRequest } from "@blazor/e2e/account-api";
import { signUpThroughBlazor, test, userMenuButton } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { expectBlazorUrl } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts } from "@blazor/e2e/texts";
import { blazorToast, dismissBlazorToast } from "@blazor/e2e/toast";
import { expectBlazorValidationMessage } from "@blazor/e2e/validation";
import { blurActiveElement, createTestContext, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
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

test.describe("@smoke", () => {
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
  test("should edit the profile with validation, toast and unsaved changes guard", async ({ page }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const email = uniqueBlazorEmail();
    const firstNameInput = page.getByLabel(texts.firstName, { exact: true });
    const lastNameInput = page.getByLabel(texts.lastName, { exact: true });
    const titleInput = page.getByLabel(texts.title, { exact: true });
    const saveButton = page.getByRole("button", { name: texts.saveChanges, exact: true });
    const workspaceLink = page.getByRole("link", { name: texts.home, exact: true });

    // === PROFILE ===
    await step("Sign up and open the profile from the header & verify email, role and saved names")(async () => {
      await signUpThroughBlazor(page, email);

      await page.getByRole("link", { name: texts.profile, exact: true }).click();

      await expectBlazorUrl(page, "user/profile");
      await expect(page.getByRole("heading", { name: texts.profile, exact: true })).toBeVisible();
      await expect(page.getByText(email, { exact: true })).toBeVisible();
      await expect(page.getByText(texts.owner, { exact: true })).toBeVisible();
      await expect(firstNameInput).toHaveValue("Blazor");
      await expect(lastNameInput).toHaveValue("User");
      await expect(userMenuButton(page)).toHaveAccessibleDescription(/^Blazor User /);
    })();

    await step("Update first name, last name and title & verify the toast and the header name without a reload")(async () => {
      await page.evaluate(() => {
        window.__documentBeforeSave = true;
      });
      await firstNameInput.fill("  Ada ");
      await lastNameInput.fill("Lovelace");
      await titleInput.fill("Engineer");

      await saveButton.click();

      const toast = blazorToast(page, texts.profileUpdated);
      await expect(toast).toBeVisible();
      await expect(userMenuButton(page)).toHaveAccessibleDescription(/^Ada Lovelace /);
      await expect(firstNameInput).toHaveValue("Ada");
      expect(await page.evaluate(() => window.__documentBeforeSave)).toBe(true);
      await dismissBlazorToast(page, toast);
    })();

    await step("Reload the profile page & verify the saved values and header name persist")(async () => {
      await expectNoPolicyViolations(page);

      await page.reload();

      await expect(firstNameInput).toHaveValue("Ada");
      await expect(lastNameInput).toHaveValue("Lovelace");
      await expect(titleInput).toHaveValue("Engineer");
      await expect(userMenuButton(page)).toHaveAccessibleDescription(/^Ada Lovelace /);
    })();

    // === VALIDATION ===
    await step("Submit too long names and title & verify the field messages and no request")(async () => {
      const requests: string[] = [];
      page.on("request", (request) => {
        if (request.method() === "PUT") requests.push(request.url());
      });
      await firstNameInput.fill(tooLongName);
      await lastNameInput.fill(tooLongName);
      await titleInput.fill(tooLongTitle);
      await blurActiveElement(page);

      await saveButton.click();

      await expectBlazorValidationMessage(page, texts.firstNameLength);
      await expectBlazorValidationMessage(page, texts.lastNameLength);
      await expectBlazorValidationMessage(page, texts.titleTooLong);
      expect(requests).toEqual([]);
    })();

    await step("Submit a whitespace-only first name and an empty last name & verify the field messages")(async () => {
      await firstNameInput.fill("   ");
      await lastNameInput.fill("");
      await titleInput.fill("Engineer");
      await blurActiveElement(page);
      await expect(page.getByText(texts.titleTooLong)).toHaveCount(0);

      await saveButton.click();

      await expectBlazorValidationMessage(page, texts.firstNameLength);
      await expectBlazorValidationMessage(page, texts.lastNameLength);
      await expect(page.getByText(texts.titleTooLong)).toHaveCount(0);
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
      await firstNameInput.fill("Grace");
      await lastNameInput.fill("Hopper");

      await workspaceLink.click();

      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeVisible();
      await page.getByRole("button", { name: texts.stay }).click();
      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeHidden();
      await expectBlazorUrl(page, "user/profile");
      await expect(firstNameInput).toHaveValue("Grace");
    })();

    await step("Navigate away with unsaved edits and choose Leave & verify the edits are discarded")(async () => {
      await workspaceLink.click();
      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeVisible();

      await page.getByRole("button", { name: texts.leave }).click();

      await expectBlazorUrl(page, "app");
      await expect(userMenuButton(page)).toHaveAccessibleDescription(/^Ada Lovelace /);
      await expectNoPolicyViolations(page);
    })();
  });
});
