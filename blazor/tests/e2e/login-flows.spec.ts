import { expect } from "@playwright/test";
import {
  userMenuButton,
  logOutThroughBlazor,
  signUpThroughBlazor,
  startEmailFlowThroughBlazor,
  test,
  trackWebAssemblyRequests
} from "@blazor/e2e/authentication";
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

test.describe("@comprehensive", () => {
  /**
   * Login attempt and start limits through the Blazor verification page, with codes read from the mailed messages:
   * - The six-slot input keeps one-time-code autofill and the six-letter maximum
   * - A wrong code entered and clicked in the same task is posted once and shows the wrong-code state, cleared and focused
   * - Wrong codes pasted and typed key by key, and the fourth attempt locking the input and Verify
   * - Enabling the locked controls in the page, and a new code requested while locked, both still refused by the account API
   * - Login through the plain input and Verify in a browser context without scripts
   * - A fourth open login start refused with the account API message, also after advancing the page clock past the lockout
   * - No securitypolicyviolation event and no style attribute on the verification and login documents
   */
  test("should lock verification after four wrong codes and refuse a fourth login start whatever the page changes", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const email = uniqueBlazorEmail();
    const resendRevealDelayMs = 31_000; // 31 seconds, past the resend delay whenever the locked page was rendered
    const lockoutWindowMs = 16 * 60_000; // 16 minutes, past the 15-minute login start window
    let mailedCode = "";

    // === SETUP ===
    await step("Sign up and log out & verify login page")(async () => {
      await signUpThroughBlazor(page, email);

      await logOutThroughBlazor(page);

      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
    })();

    await trackPolicyViolations(page);
    await page.clock.install();

    // === WRONG CODES ===
    await step("Log in with a known email & verify the mailed code and the autofill input")(async () => {
      await gotoBlazor(page, "login");
      const sentAfter = mailClock();

      await startEmailFlowThroughBlazor(page, "login", email);
      mailedCode = (await readMailSentAfter(email, sentAfter)).oneTimePassword;

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
      await expectNoPolicyViolations(page);
    })();

    await step("Paste a second wrong code & verify the wrong-code state with the input cleared")(async () => {
      await submitOneTimePassword(page, wrongCodeFor(mailedCode), "paste");

      await expectVerificationState(page, "wrongCode", accountApiMessages.wrongCode);
      await expect(verificationCodeInput(page)).toHaveValue("");
      await expect(verificationCodeInput(page)).toBeFocused();
    })();

    await step("Type a third wrong code key by key & verify the wrong-code state with the input cleared")(async () => {
      await submitOneTimePassword(page, wrongCodeFor(mailedCode).toLowerCase(), "keys");

      await expectVerificationState(page, "wrongCode", accountApiMessages.wrongCode);
      await expect(verificationCodeInput(page)).toHaveValue("");
      await expect(verificationCodeInput(page)).toBeFocused();
    })();

    await step("Paste a fourth wrong code & verify the locked state with the input and Verify disabled")(async () => {
      await submitOneTimePassword(page, wrongCodeFor(mailedCode), "paste");

      await expectVerificationState(page, "locked", accountApiMessages.tooManyAttempts);
      await expect(verificationCodeInput(page)).toBeDisabled();
      await expect(verifyButton(page)).toBeDisabled();
      await expectNoPolicyViolations(page);
    })();

    // === SERVER AUTHORITY ===
    await step("Enable the locked controls in the page and paste the mailed code & verify the account API still refuses")(async () => {
      await enableLockedVerificationControls(page);

      await submitOneTimePassword(page, mailedCode, "paste");

      await expectVerificationState(page, "locked", accountApiMessages.tooManyAttempts);
      await expectBlazorUrl(page, "login/verify");
    })();

    await step("Request a new code while locked and type it & verify the account API still refuses")(async () => {
      await page.clock.fastForward(resendRevealDelayMs);
      await expect(requestNewCodeButton(page)).toBeVisible();
      const resentAfter = mailClock();

      await requestNewCodeButton(page).click();
      await expect(page.getByText(texts.newCodeSent, { exact: true })).toBeVisible();
      const resentCode = (await readMailSentAfter(email, resentAfter)).oneTimePassword;
      await submitOneTimePassword(page, resentCode, "keys");

      expect(resentCode).not.toBe(mailedCode);
      await expectVerificationState(page, "locked", accountApiMessages.tooManyAttempts);
      await expectNoPolicyViolations(page);
    })();

    // === WITHOUT SCRIPTS ===
    await step("Log in without scripts through the plain input and Verify & verify the authenticated home")(async () => {
      const noScriptContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true, javaScriptEnabled: false });
      const noScriptPage = await noScriptContext.newPage();
      createTestContext(noScriptPage);
      await gotoBlazor(noScriptPage, "login");
      const sentAfter = mailClock();

      await startEmailFlowThroughBlazor(noScriptPage, "login", email);
      const code = (await readMailSentAfter(email, sentAfter)).oneTimePassword;
      await submitOneTimePasswordWithoutScripts(noScriptPage, wrongCodeFor(code));
      await expectVerificationState(noScriptPage, "wrongCode", accountApiMessages.wrongCode);
      await submitOneTimePasswordWithoutScripts(noScriptPage, code);

      await expectBlazorUrl(noScriptPage, "app");
      await noScriptContext.close();
    })();

    // === LOGIN START LIMIT ===
    await step("Start two more logins & verify each reaches the verification page")(async () => {
      await gotoBlazor(page, "login");
      await startEmailFlowThroughBlazor(page, "login", email);

      await gotoBlazor(page, "login");
      await startEmailFlowThroughBlazor(page, "login", email);

      await expect(verificationCodeInput(page)).toBeEnabled();
    })();

    await step("Advance the page clock past the lockout and start a fourth login & verify the account API refusal")(async () => {
      await gotoBlazor(page, "login");
      await page.clock.fastForward(lockoutWindowMs);

      await page.getByLabel(texts.email, { exact: true }).fill(email);
      await page.getByRole("button", { name: texts.logInWithEmail, exact: true }).click();

      await expectBlazorFormError(page, accountApiMessages.tooManyStarts);
      await expectBlazorUrl(page, "login");
      await expectNoPolicyViolations(page);
    })();
  });
});

test.describe("@slow", () => {
  const requestNewCodeTimeout = 30_000; // 30 seconds
  const codeValidationTimeout = 300_000; // 5 minutes
  const sessionTimeout = codeValidationTimeout + 90_000; // 6.5 minutes

  /**
   * Login resend and expiry in real time, with no fake page clock, against the account API's own limits:
   * - Resend revealed after a real 30 seconds and accepted, with the resent code mailed
   * - The expired text announced after a real 5 minutes, and the resent code then refused in the expired state
   * - A resend after expiry refused in the locked state
   * - A verification page loaded with an edited query that shows the code as valid, whose code the account API still refuses
   */
  test("should allow resend code 30 seconds after login but refuse the code and a resend after it has expired", async ({ page }) => {
    test.setTimeout(sessionTimeout);
    createTestContext(page);
    const texts = blazorTexts();
    const email = uniqueBlazorEmail();
    let resentCode = "";

    await step("Sign up, log out and start a login & verify the verification page")(async () => {
      await signUpThroughBlazor(page, email);
      await logOutThroughBlazor(page);

      await startEmailFlowThroughBlazor(page, "login", email);

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
      await expectBlazorUrl(page, "login/verify");
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
      await expectBlazorUrl(page, "login/verify");
    })();
  });
});
