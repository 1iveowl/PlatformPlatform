import { expect } from "@playwright/test";
import { signUpThroughBlazor, startEmailFlowThroughBlazor, test, trackWebAssemblyRequests } from "@blazor/e2e/authentication";
import { revealResendThroughBlazor, submitOneTimePassword } from "@blazor/e2e/one-time-password";
import { blazorPath, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { accountApiMessages, blazorCultures } from "@blazor/e2e/texts";
import { blazorToastTestIds, expectBlazorToast } from "@blazor/e2e/toast";
import { expectBlazorFormError, expectBlazorValidationMessage } from "@blazor/e2e/validation";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { getVerificationCode, uniqueEmail } from "@shared/e2e/utils/test-data";
import { step } from "@shared/e2e/utils/test-step-wrapper";

for (const culture of blazorCultures) {
  test.describe("@smoke", () => {
    test.use({ locale: culture.locale });

    /**
     * Signup through the Blazor public pages and the welcome setup in one culture, mirroring the React signup smoke test:
     * - Required, too long and malformed email validated on the static form
     * - The verification page with the remaining validity, resend hidden until 30 seconds on a fake page clock, and a resend
     * - A valid-shaped wrong code posted to the host and rejected by the account API
     * - Completion as a full navigation to welcome, where the tenant name is limited to 30 characters
     * - The profile step with the first name, last name and title limits
     * - The authenticated workspace with the new tenant name, staying there on reload
     * - No WebAssembly runtime request on the public pages or on welcome
     */
    test(`should handle signup flow with validation, welcome setup and workspace in ${culture.locale}`, async ({ page }) => {
      createTestContext(page);
      const email = uniqueEmail();
      const accountName = "Blazor signup account";
      const runtimeRequests = trackWebAssemblyRequests(page);
      await page.clock.install();

      // === EMAIL VALIDATION ===
      await step("Submit empty, too long and malformed email & verify each is rejected on the signup page")(async () => {
        await gotoBlazor(page, "signup");
        await expect(page.getByRole("heading", { name: culture.createYourAccount })).toBeVisible();

        await page.getByTestId("submit").click();
        await expectBlazorValidationMessage(page, culture.emailAddressRequired);

        await page.getByTestId("email").fill(`${"a".repeat(90)}@example.com`);
        await page.getByTestId("submit").click();
        await expectBlazorValidationMessage(page, culture.emailAddressTooLong);

        await page.getByTestId("email").fill("invalid-email");
        await page.getByTestId("submit").click();
        expect(await page.getByTestId("email").evaluate((input: HTMLInputElement) => input.validity.typeMismatch)).toBe(true);
        await expectBlazorUrl(page, "signup");
      })();

      // === VERIFICATION ===
      await step("Sign up with a valid email & verify the verification page")(async () => {
        await startEmailFlowThroughBlazor(page, "signup", email);

        await expect(page.getByRole("heading", { name: culture.enterYourVerificationCode })).toBeVisible();
        await expect(page.getByTestId("code-valid-for")).toContainText(culture.validForPrefix);
        await expect(page.getByTestId("code")).toHaveAttribute("autocomplete", "one-time-code");
      })();

      await step("Request a new code after 30 seconds & verify confirmation")(async () => {
        await revealResendThroughBlazor(page);
        await expect(page.getByTestId("resend-code")).toHaveText(culture.requestNewCode);

        await page.getByTestId("resend-code").click();

        await expect(page.getByTestId("code-resent")).toHaveText(culture.newCodeSent);
        await expect(page.getByTestId("resend-code")).toBeHidden();
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
        await expect(page.getByRole("heading", { name: culture.setUpYourAccount })).toBeVisible();
      })();

      // === WELCOME ===
      await step("Submit a 31-character account name & verify the length validation")(async () => {
        await page.getByTestId("account-name").fill("a".repeat(31));
        await page.getByTestId("continue").click();

        await expectBlazorValidationMessage(page, culture.tenantNameLength);
        await expect(page.getByRole("heading", { name: culture.setUpYourAccount })).toBeVisible();
      })();

      await step("Submit a 30-character account name & verify the profile setup")(async () => {
        await page.getByTestId("account-name").fill(accountName.padEnd(30, "x"));
        await page.getByTestId("continue").click();

        await expect(page.getByRole("heading", { name: culture.setUpYourProfile })).toBeVisible();
        await expect(page.getByTestId("account-name")).toHaveCount(0);
      })();

      await step("Submit a profile over the limits & verify each field's validation")(async () => {
        await page.getByTestId("first-name").fill("a".repeat(31));
        await page.getByTestId("last-name").fill("b".repeat(31));
        await page.getByTestId("title").fill("c".repeat(51));
        await page.getByTestId("continue").click();

        await expectBlazorValidationMessage(page, culture.firstNameLength);
        await expectBlazorValidationMessage(page, culture.lastNameLength);
        await expectBlazorValidationMessage(page, culture.titleTooLong);
      })();

      await step("Submit a valid profile & verify the authenticated workspace")(async () => {
        expect(runtimeRequests).toEqual([]);

        await page.getByTestId("first-name").fill("a".repeat(30));
        await page.getByTestId("last-name").fill("Blazor");
        await page.getByTestId("title").fill("c".repeat(50));
        await page.getByTestId("continue").click();

        await expectBlazorUrl(page, "app");
        await expect(page.getByRole("heading", { name: culture.yourWorkspace })).toBeVisible();
        await expect(page.getByTestId("bootstrap-tenant-name")).toHaveText(accountName.padEnd(30, "x"));
        await expect(page.getByTestId("bootstrap-email")).toHaveText(email);
      })();

      await step("Reload the workspace & verify it stays without returning to welcome")(async () => {
        await page.reload();

        await expectBlazorUrl(page, "app");
        await expect(page.getByTestId("logout")).toBeVisible();
      })();
    });
  });
}

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
