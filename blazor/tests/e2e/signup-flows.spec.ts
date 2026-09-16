import { expect } from "@playwright/test";
import { userMenuButton, signUpThroughBlazor, startEmailFlowThroughBlazor, test, trackWebAssemblyRequests } from "@blazor/e2e/authentication";
import { requestNewCodeButton, revealResendThroughBlazor, submitOneTimePassword, verificationCodeInput } from "@blazor/e2e/one-time-password";
import { blazorPath, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { accountApiMessages, blazorTexts } from "@blazor/e2e/texts";
import { expectBlazorToast } from "@blazor/e2e/toast";
import { expectBlazorFormError, expectBlazorValidationMessage } from "@blazor/e2e/validation";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { getVerificationCode } from "@shared/e2e/utils/test-data";
import { step } from "@shared/e2e/utils/test-step-wrapper";

test.describe("@smoke", () => {
  /**
   * Signup through the Blazor public pages and the welcome setup in the culture of the running project, mirroring the React
   * signup smoke test:
   * - Required, too long and malformed email validated on the static form
   * - The verification page with the remaining validity, resend hidden until 30 seconds on a fake page clock, and a resend
   * - A valid-shaped wrong code posted to the host and rejected by the account API
   * - Completion as a full navigation to welcome, where the tenant name is limited to 30 characters
   * - The profile step with the first name, last name and title limits
   * - The authenticated workspace with the new tenant name, staying there on reload
   * - No WebAssembly runtime request on the public pages or on welcome
   */
  test("should handle signup flow with validation, welcome setup and workspace", async ({ page }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const email = uniqueBlazorEmail();
    const accountName = "Blazor signup account";
    const emailInput = page.getByLabel(texts.email, { exact: true });
    const signUpButton = page.getByRole("button", { name: texts.signUpWithEmail, exact: true });
    const accountNameInput = page.getByLabel(texts.accountName, { exact: true });
    const firstNameInput = page.getByLabel(texts.firstName, { exact: true });
    const lastNameInput = page.getByLabel(texts.lastName, { exact: true });
    const titleInput = page.getByLabel(texts.title, { exact: true });
    const continueButton = page.getByRole("button", { name: texts.continue, exact: true });
    const runtimeRequests = trackWebAssemblyRequests(page);
    await page.clock.install();

    // === EMAIL VALIDATION ===
    await step("Submit empty, too long and malformed email & verify each is rejected on the signup page")(async () => {
      await gotoBlazor(page, "signup");
      await expect(page.getByRole("heading", { name: texts.createYourAccount })).toBeVisible();

      await signUpButton.click();
      await expectBlazorValidationMessage(page, texts.emailAddressRequired);

      await emailInput.fill(`${"a".repeat(90)}@example.com`);
      await signUpButton.click();
      await expectBlazorValidationMessage(page, texts.emailAddressTooLong);

      await emailInput.fill("invalid-email");
      await signUpButton.click();
      expect(await emailInput.evaluate((input: HTMLInputElement) => input.validity.typeMismatch)).toBe(true);
      await expectBlazorUrl(page, "signup");
    })();

    // === VERIFICATION ===
    await step("Sign up with a valid email & verify the verification page")(async () => {
      await startEmailFlowThroughBlazor(page, "signup", email);

      await expect(page.getByRole("heading", { name: texts.enterYourVerificationCode })).toBeVisible();
      await expect(page.getByText(texts.validForPrefix)).toBeVisible();
      await expect(verificationCodeInput(page)).toHaveAttribute("autocomplete", "one-time-code");
    })();

    await step("Request a new code after 30 seconds & verify confirmation")(async () => {
      await revealResendThroughBlazor(page);

      await requestNewCodeButton(page).click();

      await expect(page.getByText(texts.newCodeSent, { exact: true })).toBeVisible();
      await expect(requestNewCodeButton(page)).toBeHidden();
    })();

    await step("Submit a valid-shaped wrong code & verify the account API rejection")(async () => {
      const completion = page.waitForResponse(
        (response) => response.request().method() === "POST" && new URL(response.url()).pathname === blazorPath("signup/verify")
      );

      await submitOneTimePassword(page, "WRONGA");

      expect((await completion).status()).toBe(200);
      await expectBlazorFormError(page, accountApiMessages.wrongCode);
    })();

    await step("Submit the correct code & verify full navigation to the account setup")(async () => {
      await submitOneTimePassword(page, getVerificationCode());

      await expectBlazorUrl(page, "welcome");
      await expect(page.getByRole("heading", { name: texts.setUpYourAccount })).toBeVisible();
    })();

    // === WELCOME ===
    await step("Submit a 31-character account name & verify the length validation")(async () => {
      await accountNameInput.fill("a".repeat(31));
      await continueButton.click();

      await expectBlazorValidationMessage(page, texts.tenantNameLength);
      await expect(page.getByRole("heading", { name: texts.setUpYourAccount })).toBeVisible();
    })();

    await step("Submit a 30-character account name & verify the profile setup")(async () => {
      await accountNameInput.fill(accountName.padEnd(30, "x"));
      await continueButton.click();

      await expect(page.getByRole("heading", { name: texts.setUpYourProfile })).toBeVisible();
      await expect(accountNameInput).toHaveCount(0);
    })();

    await step("Submit a profile over the limits & verify each field's validation")(async () => {
      await firstNameInput.fill("a".repeat(31));
      await lastNameInput.fill("b".repeat(31));
      await titleInput.fill("c".repeat(51));
      await continueButton.click();

      await expectBlazorValidationMessage(page, texts.firstNameLength);
      await expectBlazorValidationMessage(page, texts.lastNameLength);
      await expectBlazorValidationMessage(page, texts.titleTooLong);
    })();

    await step("Submit a valid profile & verify the authenticated workspace")(async () => {
      expect(runtimeRequests).toEqual([]);

      await firstNameInput.fill("a".repeat(30));
      await lastNameInput.fill("Blazor");
      await titleInput.fill("c".repeat(50));
      await continueButton.click();

      await expectBlazorUrl(page, "app");
      await expect(page.getByRole("heading", { name: texts.yourWorkspace })).toBeVisible();
      await expect(page.getByTestId("bootstrap-tenant-name")).toHaveText(accountName.padEnd(30, "x"));
      await expect(page.getByTestId("bootstrap-email")).toHaveText(email);
    })();

    await step("Reload the workspace & verify it stays without returning to welcome")(async () => {
      await page.reload();

      await expectBlazorUrl(page, "app");
      await expect(userMenuButton(page)).toBeVisible();
    })();
  });
});

test.describe("@comprehensive", () => {
  /**
   * Signup through the Blazor public pages and the adapters they rely on:
   * - Field validation on an empty email (ValidationMessage)
   * - A form-level error on a verification page without a signup (FormErrorAlert)
   * - Email signup with the one-time password and the welcome setup, landing on the Blazor workspace with the new user's
   *   bootstrap
   * - An API failure toast on the Development-only interactive forms fixture (ToastRegion)
   */
  test("should sign up through the Blazor pages with validation, form errors and toasts", async ({ page }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const email = uniqueBlazorEmail();

    // === VALIDATION ===
    await step("Submit signup with empty email & verify validation message")(async () => {
      await gotoBlazor(page, "signup");

      await page.getByRole("button", { name: texts.signUpWithEmail, exact: true }).click();

      await expectBlazorValidationMessage(page, texts.emailAddressRequired);
      await expectBlazorUrl(page, "signup");
    })();

    await step("Submit verification code without a signup & verify form error")(async () => {
      await gotoBlazor(page, "signup/verify");

      await submitOneTimePassword(page);

      await expectBlazorFormError(page, texts.signupExpired);
      await expectBlazorUrl(page, "signup/verify");
    })();

    // === SIGNUP ===
    await step("Sign up with email and one-time password & verify Blazor workspace")(async () => {
      await signUpThroughBlazor(page, email);

      await expect(page.getByTestId("bootstrap-email")).toHaveText(email);
      await expect(page.getByTestId("render-mode")).toHaveText(texts.interactiveTrue);
    })();

    // === TOASTS ===
    await step("Present an API failure on the interactive forms fixture & verify error toast")(async () => {
      await gotoBlazor(page, "development/form-errors/interactive");
      await expect(page.getByTestId("fixture-interactive")).toHaveText("Interactive: True");

      await page.getByRole("button", { name: "detail", exact: true }).click();

      await expectBlazorToast(page, { title: texts.somethingWentWrong, message: "The user was changed by someone else." });
    })();
  });
});
