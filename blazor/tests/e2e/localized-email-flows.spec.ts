import { expect } from "@playwright/test";
import {
  completeWelcomeThroughBlazor,
  logOutThroughBlazor,
  startEmailFlowThroughBlazor,
  test,
  userMenuButton
} from "@blazor/e2e/authentication";
import { expectMailContent, mailClock, readMailSentAfter } from "@blazor/e2e/mail";
import { requestNewCodeButton, revealResendThroughBlazor, submitOneTimePassword, verificationCodeInput } from "@blazor/e2e/one-time-password";
import { blazorUrl, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts } from "@blazor/e2e/texts";
import { productName } from "@shared/e2e/utils/constants";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

test.describe("@smoke", () => {
  /**
   * The login and signup emails the account API sends for the Blazor pages, in the culture of the running project, read
   * through Mailpit and completed with the mailed codes:
   * - The signup code email (subject, HTML and plain text, and the autofill suffix) and the verification input's
   *   one-time-code autocomplete
   * - The login code email for the user the signup created in this culture
   * - The resent login code email after resend is revealed on a fake page clock, and login with the resent code
   * - The unknown user email for a login start with an address that has no account
   */
  test("should send localized signup, login, resend and unknown user emails for the Blazor pages", async ({ page }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const email = uniqueBlazorEmail();
    const unknownEmail = uniqueBlazorEmail();
    const autofillSuffix = `@${new URL(blazorUrl()).hostname} #`;
    await page.clock.install();

    // === SIGNUP ===
    await step("Sign up with email & verify the localized signup email and the autofill input")(async () => {
      await gotoBlazor(page, "signup");
      await expect(page.getByRole("heading", { name: texts.createYourAccount })).toBeVisible();
      const sentAfter = mailClock();

      await startEmailFlowThroughBlazor(page, "signup", email);
      const mail = await readMailSentAfter(email, sentAfter);

      expectMailContent(mail, texts.mailSignupSubject, [texts.mailConfirmationCodeBelow, texts.mailEnterInBrowser]);
      expect(mail.text).toContain(`${autofillSuffix}${mail.oneTimePassword}`);
      await expect(verificationCodeInput(page)).toHaveAttribute("autocomplete", "one-time-code");

      await submitOneTimePassword(page, mail.oneTimePassword, "paste");
      await completeWelcomeThroughBlazor(page, { accountName: "Localized email account", firstName: "Localized", lastName: "Email" });
      await expect(userMenuButton(page)).toBeVisible();
    })();

    // === LOGIN ===
    await step("Log out and start a login & verify the localized login email")(async () => {
      await logOutThroughBlazor(page);
      const sentAfter = mailClock();

      await startEmailFlowThroughBlazor(page, "login", email);
      const mail = await readMailSentAfter(email, sentAfter);

      expectMailContent(mail, `${productName}${texts.mailLoginSubjectAfterProductName}`, [texts.mailConfirmationCodeBelow, texts.mailEnterInBrowser]);
      expect(mail.text).toContain(`${autofillSuffix}${mail.oneTimePassword}`);
      await expect(verificationCodeInput(page)).toHaveAttribute("autocomplete", "one-time-code");
    })();

    await step("Request a new login code & verify the localized resend email and login with the resent code")(async () => {
      await revealResendThroughBlazor(page);
      const resentAfter = mailClock();

      await requestNewCodeButton(page).click();
      await expect(page.getByText(texts.newCodeSent, { exact: true })).toBeVisible();
      const mail = await readMailSentAfter(email, resentAfter);

      expectMailContent(mail, texts.mailResendSubject, [texts.mailResendExpiry]);
      expect(mail.text).toContain(`${autofillSuffix}${mail.oneTimePassword}`);

      await submitOneTimePassword(page, mail.oneTimePassword, "keys");
      await expectBlazorUrl(page, "app");
      await expect(userMenuButton(page)).toBeVisible();
    })();

    // === UNKNOWN USER ===
    await step("Log out and start a login with an unknown email & verify the localized unknown user email")(async () => {
      await logOutThroughBlazor(page);
      const sentAfter = mailClock();

      await startEmailFlowThroughBlazor(page, "login", unknownEmail);
      const mail = await readMailSentAfter(unknownEmail, sentAfter);

      expectMailContent(mail, texts.mailUnknownUserSubject, [texts.mailUnknownUserQuestion, unknownEmail]);
    })();
  });
});
