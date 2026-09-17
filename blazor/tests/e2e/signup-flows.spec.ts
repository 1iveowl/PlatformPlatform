import { expect } from "@playwright/test";
import { userMenuButton, signUpThroughBlazor, startEmailFlowThroughBlazor, test, trackWebAssemblyRequests } from "@blazor/e2e/authentication";
import { mailClock, readMailSentAfter } from "@blazor/e2e/mail";
import {
  enableLockedVerificationControls,
  expectVerificationState,
  requestNewCodeButton,
  revealResendThroughBlazor,
  submitOneTimePassword,
  submitOneTimePasswordTwiceAtOnce,
  submitOneTimePasswordWithoutScripts,
  trackVerificationPosts,
  verificationCodeInput,
  verifyButton,
  wrongCodeFor
} from "@blazor/e2e/one-time-password";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, blazorUrl, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { accountApiMessages, blazorLocale, blazorTexts } from "@blazor/e2e/texts";
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
   * Signup through the Blazor pages, its attempt and start limits with codes read from the mailed messages, and the adapters
   * the pages rely on:
   * - Field validation on an empty email (ValidationMessage) and a form-level error on a verification page without a signup
   *   (FormErrorAlert)
   * - Email signup with the one-time password and the welcome setup, landing on the Blazor workspace with the new user's
   *   bootstrap, and an API failure toast on the Development-only interactive forms fixture (ToastRegion)
   * - The six-slot input with one-time-code autofill; a wrong code entered and clicked in the same task posted once
   * - Wrong codes pasted and typed key by key, the fourth attempt locking the input and Verify, and the mailed code refused
   *   after enabling the locked controls in the page
   * - Signup through the plain input and Verify in a browser context without scripts
   * - A fourth open signup start refused with the account API message, also after advancing the page clock past the lockout
   * - No securitypolicyviolation event and no style attribute on the signup and verification documents
   */
  test("should sign up with validation, form errors and toasts, and lock wrong codes and a fourth signup start", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const email = uniqueBlazorEmail();
    const limitedEmail = uniqueBlazorEmail();
    const lockoutWindowMs = 61 * 60_000; // 61 minutes, past the 60-minute signup start window
    let mailedCode = "";
    await trackPolicyViolations(page);

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

    // === WRONG CODES ===
    await page.clock.install();

    await step("Sign up with a new email & verify the mailed code and the autofill input")(async () => {
      await gotoBlazor(page, "signup");
      const sentAfter = mailClock();

      await startEmailFlowThroughBlazor(page, "signup", limitedEmail);
      mailedCode = (await readMailSentAfter(limitedEmail, sentAfter)).oneTimePassword;

      expect(mailedCode).toHaveLength(6);
      await expect(verificationCodeInput(page)).toHaveAttribute("autocomplete", "one-time-code");
      await expect(verificationCodeInput(page)).toHaveAttribute("maxlength", "6");
      await expectNoPolicyViolations(page);
    })();

    await step("Enter a wrong code and click Verify in the same task & verify one post and the wrong-code state")(async () => {
      const verificationPosts = trackVerificationPosts(page);

      await submitOneTimePasswordTwiceAtOnce(page, wrongCodeFor(mailedCode));

      await expectVerificationState(page, "wrongCode", accountApiMessages.wrongCode);
      expect(verificationPosts.count).toBe(1);
      await expect(verificationCodeInput(page)).toHaveValue("");
      await expect(verificationCodeInput(page)).toBeFocused();
    })();

    await step("Paste a second and type a third wrong code & verify the wrong-code state each time")(async () => {
      await submitOneTimePassword(page, wrongCodeFor(mailedCode), "paste");
      await expectVerificationState(page, "wrongCode", accountApiMessages.wrongCode);

      await submitOneTimePassword(page, wrongCodeFor(mailedCode).toLowerCase(), "keys");

      await expectVerificationState(page, "wrongCode", accountApiMessages.wrongCode);
      await expect(verificationCodeInput(page)).toBeFocused();
    })();

    await step("Paste a fourth wrong code & verify the locked state with the input and Verify disabled")(async () => {
      await submitOneTimePassword(page, wrongCodeFor(mailedCode), "paste");

      await expectVerificationState(page, "locked", accountApiMessages.tooManyAttempts);
      await expect(verificationCodeInput(page)).toBeDisabled();
      await expect(verifyButton(page)).toBeDisabled();
      await expectNoPolicyViolations(page);
    })();

    await step("Enable the locked controls in the page and paste the mailed code & verify the account API still refuses")(async () => {
      await enableLockedVerificationControls(page);

      await submitOneTimePassword(page, mailedCode, "paste");

      await expectVerificationState(page, "locked", accountApiMessages.tooManyAttempts);
      await expectBlazorUrl(page, "signup/verify");
    })();

    // === WITHOUT SCRIPTS ===
    await step("Sign up without scripts through the plain input and Verify & verify the account setup")(async () => {
      const noScriptContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true, javaScriptEnabled: false });
      const noScriptPage = await noScriptContext.newPage();
      createTestContext(noScriptPage);
      const noScriptEmail = uniqueBlazorEmail();
      await gotoBlazor(noScriptPage, "signup");
      const sentAfter = mailClock();

      await startEmailFlowThroughBlazor(noScriptPage, "signup", noScriptEmail);
      const code = (await readMailSentAfter(noScriptEmail, sentAfter)).oneTimePassword;
      await submitOneTimePasswordWithoutScripts(noScriptPage, wrongCodeFor(code));
      await expectVerificationState(noScriptPage, "wrongCode", accountApiMessages.wrongCode);
      await submitOneTimePasswordWithoutScripts(noScriptPage, code);

      await expectBlazorUrl(noScriptPage, "welcome");
      await expect(noScriptPage.getByRole("heading", { name: texts.setUpYourAccount })).toBeVisible();
      await noScriptContext.close();
    })();

    // === SIGNUP START LIMIT ===
    await step("Start two more signups with the locked email & verify each reaches the verification page")(async () => {
      await gotoBlazor(page, "signup");
      await startEmailFlowThroughBlazor(page, "signup", limitedEmail);

      await gotoBlazor(page, "signup");
      await startEmailFlowThroughBlazor(page, "signup", limitedEmail);

      await expect(verificationCodeInput(page)).toBeEnabled();
    })();

    await step("Advance the page clock past the lockout and start a fourth signup & verify the account API refusal")(async () => {
      await gotoBlazor(page, "signup");
      await page.clock.fastForward(lockoutWindowMs);

      await page.getByLabel(texts.email, { exact: true }).fill(limitedEmail);
      await page.getByRole("button", { name: texts.signUpWithEmail, exact: true }).click();

      await expectBlazorFormError(page, accountApiMessages.tooManyStarts);
      await expectBlazorUrl(page, "signup");
      await expectNoPolicyViolations(page);
    })();
  });
});

test.describe("@slow", () => {
  const requestNewCodeTimeout = 30_000; // 30 seconds
  const codeValidationTimeout = 300_000; // 5 minutes
  const sessionTimeout = codeValidationTimeout + 90_000; // 6.5 minutes

  /**
   * Signup resend and expiry in real time, with no fake page clock, against the account API's own limits:
   * - Resend revealed after a real 30 seconds and accepted, with the resent code mailed
   * - The expired text announced after a real 5 minutes, and the resent code then refused in the expired state
   * - A resend after expiry refused in the locked state
   * - A verification page loaded with an edited query that shows the code as valid, whose code the account API still refuses
   */
  test("should allow resend code 30 seconds after signup but refuse the code and a resend after it has expired", async ({ page }) => {
    test.setTimeout(sessionTimeout);
    createTestContext(page);
    const texts = blazorTexts();
    const email = uniqueBlazorEmail();
    let resentCode = "";

    await step("Start a signup & verify the verification page")(async () => {
      await gotoBlazor(page, "signup");

      await startEmailFlowThroughBlazor(page, "signup", email);

      await expect(page.getByText(texts.validForPrefix)).toBeVisible();
      await expect(requestNewCodeButton(page)).toBeHidden();
    })();

    await step("Wait 30 seconds and request a new code & verify confirmation and the mailed code")(async () => {
      await page.waitForTimeout(requestNewCodeTimeout);
      await expect(requestNewCodeButton(page)).toBeVisible();
      const resentAfter = mailClock();

      await requestNewCodeButton(page).click();

      await expect(page.getByText(texts.newCodeSent, { exact: true })).toBeVisible();
      await expect(requestNewCodeButton(page)).toBeHidden();
      resentCode = (await readMailSentAfter(email, resentAfter)).oneTimePassword;
      expect(resentCode).toHaveLength(6);
    })();

    await step("Wait for code expiration and submit the resent code & verify the expired state")(async () => {
      await page.waitForTimeout(codeValidationTimeout);
      await expect(page.getByText(texts.verificationCodeExpired, { exact: true })).toBeVisible();

      await submitOneTimePassword(page, resentCode);

      await expectVerificationState(page, "expired", accountApiMessages.codeNoLongerValid);
      await expectBlazorUrl(page, "signup/verify");
    })();

    await step("Request a new code after expiry & verify the locked state")(async () => {
      await expect(requestNewCodeButton(page)).toBeVisible();

      await requestNewCodeButton(page).click();

      await expectVerificationState(page, "locked", accountApiMessages.tooManyAttempts);
      await expect(verificationCodeInput(page)).toBeDisabled();
    })();

    await step("Load the page with a query that shows the code as valid and submit it & verify the account API refusal")(async () => {
      const editedUrl = new URL(page.url());
      editedUrl.searchParams.set("sent", String(Math.floor(Date.now() / 1000)));
      editedUrl.searchParams.delete("resent");

      await page.goto(editedUrl.toString());
      await expect(page.getByText(texts.validForPrefix)).toBeVisible();
      await submitOneTimePassword(page, resentCode);

      await expectVerificationState(page, "wrongCode", accountApiMessages.codeNoLongerValid);
      await expectBlazorUrl(page, "signup/verify");
    })();
  });
});
