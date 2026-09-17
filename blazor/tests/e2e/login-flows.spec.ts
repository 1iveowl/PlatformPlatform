import { expect } from "@playwright/test";
import {
  userMenuButton,
  logOutThroughBlazor,
  signUpThroughBlazor,
  startEmailFlowThroughBlazor,
  test,
  trackWebAssemblyRequests
} from "@blazor/e2e/authentication";
import { requestNewCodeButton, revealResendThroughBlazor, submitOneTimePassword, verificationCodeInput } from "@blazor/e2e/one-time-password";
import { blazorPath, blazorUrl, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { accountApiMessages, blazorTexts } from "@blazor/e2e/texts";
import { expectBlazorFormError, expectBlazorValidationMessage } from "@blazor/e2e/validation";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { getVerificationCode } from "@shared/e2e/utils/test-data";
import { step } from "@shared/e2e/utils/test-step-wrapper";

test.describe("@smoke", () => {
  /**
   * Login through the Blazor public pages in the culture of the running project, mirroring the React login smoke test:
   * - Logout from the workspace and the redirect of a deep link to login with its return path
   * - Required, too long and malformed email, and the six-letter code shape, validated on the static form
   * - An unknown email that reaches the verification page exactly like a known one
   * - A valid-shaped wrong code posted to the host and rejected by the account API
   * - Resend revealed after 30 seconds on a fake page clock, its confirmation, and the rejected second resend
   * - Completion with a lower case code as a full navigation to the requested destination
   * - A return path outside the path base that falls back to the authenticated home
   * - No WebAssembly runtime request on any public page
   */
  test("should handle login flow with validation, resend, return path and logout", async ({ page }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const email = uniqueBlazorEmail();
    const verifyPath = blazorPath("login/verify");
    const emailInput = page.getByLabel(texts.email, { exact: true });
    const logInButton = page.getByRole("button", { name: texts.logInWithEmail, exact: true });
    const validFor = page.getByText(texts.validForPrefix);

    // === LOGOUT AND AUTHENTICATION PROTECTION ===
    await step("Sign up and log out & verify login page")(async () => {
      await signUpThroughBlazor(page, email);

      await logOutThroughBlazor(page);

      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
    })();

    await page.clock.install();
    const runtimeRequests = trackWebAssemblyRequests(page);

    await step("Open a deep link while logged out & verify redirect to login with return path")(async () => {
      await page.goto(blazorPath("app/details"));

      await expect(page).toHaveURL(`${blazorUrl("login")}?returnPath=${encodeURIComponent(blazorPath("app/details"))}`);
      await expect(logInButton).toBeVisible();
    })();

    // === EMAIL VALIDATION ===
    await step("Submit empty, too long and malformed email & verify each is rejected on the login page")(async () => {
      await logInButton.click();
      await expectBlazorValidationMessage(page, texts.emailAddressRequired);

      await emailInput.fill(`${"a".repeat(90)}@example.com`);
      await logInButton.click();
      await expectBlazorValidationMessage(page, texts.emailAddressTooLong);

      await emailInput.fill("invalid-email");
      await logInButton.click();
      expect(await emailInput.evaluate((input: HTMLInputElement) => input.validity.typeMismatch)).toBe(true);
      await expectBlazorUrl(page, "login");
    })();

    // === UNKNOWN EMAIL ===
    await step("Log in with an unknown email & verify the verification page shows no disclosure")(async () => {
      await gotoBlazor(page, "login");

      await startEmailFlowThroughBlazor(page, "login", uniqueBlazorEmail());

      await expect(page.getByRole("heading", { name: texts.enterYourVerificationCode })).toBeVisible();
      await expect(validFor).toBeVisible();
      await expect(requestNewCodeButton(page)).toBeHidden();
    })();

    // === VERIFICATION ===
    await step("Log in from the deep link with a known email & verify the return path travels to verification")(async () => {
      await page.goto(`${blazorPath("login")}?returnPath=${encodeURIComponent(blazorPath("app/details"))}`);

      await startEmailFlowThroughBlazor(page, "login", email);

      expect(new URL(page.url()).searchParams.get("returnPath")).toBe(blazorPath("app/details"));
      await expect(validFor).toBeVisible();
    })();

    await step("Submit a code of the wrong shape & verify local validation message")(async () => {
      await submitOneTimePassword(page, "ABC");

      await expectBlazorValidationMessage(page, texts.verificationCodeFormat);
    })();

    await step("Submit a valid-shaped wrong code & verify the account API rejection")(async () => {
      const completion = page.waitForResponse(
        (response) => response.request().method() === "POST" && new URL(response.url()).pathname === verifyPath
      );

      await submitOneTimePassword(page, "wronga");

      expect((await completion).status()).toBe(200);
      await expectBlazorFormError(page, accountApiMessages.wrongCode);
      await expect(page.getByText(texts.verificationCodeFormat)).toHaveCount(0);
    })();

    // === RESEND ===
    await step("Request a new code after 30 seconds & verify confirmation and a new resend delay")(async () => {
      await revealResendThroughBlazor(page);

      await requestNewCodeButton(page).click();

      await expect(page.getByText(texts.newCodeSent, { exact: true })).toBeVisible();
      await expect(validFor).toBeVisible();
      await expect(requestNewCodeButton(page)).toBeHidden();
    })();

    await step("Request a second new code & verify the account API rejection")(async () => {
      await revealResendThroughBlazor(page);

      await requestNewCodeButton(page).click();

      await expectBlazorFormError(page, accountApiMessages.tooManyAttempts);
      await expect(verificationCodeInput(page)).toBeDisabled();
    })();

    await step("Submit the code in lower case & verify full navigation to the requested page")(async () => {
      expect(runtimeRequests).toEqual([]);
      // The refused resend locks only the page it answered; the code sent before it is still valid on a new load
      await page.goto(page.url());

      await submitOneTimePassword(page, getVerificationCode().toLowerCase());

      await expectBlazorUrl(page, "app/details");
      await expect(userMenuButton(page)).toBeVisible();
    })();

    // === RETURN PATH OUTSIDE THE PATH BASE ===
    await step("Log in with a return path outside the path base & verify the authenticated home")(async () => {
      await logOutThroughBlazor(page);
      await page.goto(`${blazorPath("login")}?returnPath=${encodeURIComponent("//evil.example/blazor/app")}`);

      await startEmailFlowThroughBlazor(page, "login", email);
      await submitOneTimePassword(page);

      await expectBlazorUrl(page, "app");
      await expect(page.getByRole("heading", { name: texts.yourWorkspace })).toBeVisible();
    })();
  });
});
