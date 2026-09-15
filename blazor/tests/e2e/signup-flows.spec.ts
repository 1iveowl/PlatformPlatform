import { expect } from "@playwright/test";
import { signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { submitOneTimePassword } from "@blazor/e2e/one-time-password";
import { expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { blazorToastTestIds, expectBlazorToast } from "@blazor/e2e/toast";
import { expectBlazorFormError, expectBlazorValidationMessage } from "@blazor/e2e/validation";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { uniqueEmail } from "@shared/e2e/utils/test-data";
import { step } from "@shared/e2e/utils/test-step-wrapper";

test.describe("@comprehensive", () => {
  /**
   * Signup through the Blazor public pages and the adapters they rely on:
   * - Field validation on an empty email (ValidationMessage)
   * - A form-level error on a verification page without a signup (FormErrorAlert)
   * - Email signup with the one-time password, landing on the Blazor workspace with the new user's bootstrap
   * - An API failure toast on the Development-only interactive forms fixture (ToastRegion)
   */
  test("should sign up through the Blazor pages with validation, form errors and toasts", async ({ page }) => {
    createTestContext(page);
    const email = uniqueEmail();

    // === VALIDATION ===
    await step("Submit signup with empty email & verify validation message")(async () => {
      await gotoBlazor(page, "signup");

      await page.getByTestId("submit").click();

      await expectBlazorValidationMessage(page, "Email address required");
      await expectBlazorUrl(page, "signup");
    })();

    await step("Submit verification code without a signup & verify form error")(async () => {
      await gotoBlazor(page, "signup/verify");

      await submitOneTimePassword(page);

      await expectBlazorFormError(page, "The signup has expired. Start again.");
      await expectBlazorUrl(page, "signup/verify");
    })();

    // === SIGNUP ===
    await step("Sign up with email and one-time password & verify Blazor workspace")(async () => {
      await signUpThroughBlazor(page, email);

      await expect(page.getByTestId("bootstrap-email")).toHaveText(email);
      await expect(page.getByTestId("render-mode")).toHaveText("Interactive: True");
    })();

    // === TOASTS ===
    await step("Present an API failure on the interactive forms fixture & verify error toast")(async () => {
      await gotoBlazor(page, "development/form-errors/interactive");
      await expect(page.getByTestId("fixture-interactive")).toHaveText("Interactive: True");

      await page.getByTestId("present-detail").click();

      await expectBlazorToast(page, {
        testId: blazorToastTestIds.apiFailure,
        title: "Something went wrong",
        message: "The user was changed by someone else."
      });
    })();
  });
});
