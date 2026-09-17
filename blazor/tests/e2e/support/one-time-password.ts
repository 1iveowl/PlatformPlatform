import { expect, type Locator, type Page } from "@playwright/test";
import { getVerificationCode } from "@shared/e2e/utils/test-data";
import { blazorTexts } from "./texts";

/**
 * The verification code input of a Blazor verification page, labelled for login or for signup. The Blazor pages render one
 * text input posted by a static server-rendered form, which js/one-time-password.js draws as six slots; the input stays the
 * labelled field, so it is located by its label in both states.
 * @param page Playwright page instance on /blazor/signup/verify or /blazor/login/verify
 */
export function verificationCodeInput(page: Page): Locator {
  const texts = blazorTexts();
  return page.getByLabel(texts.loginVerificationCode, { exact: true }).or(page.getByLabel(texts.signupVerificationCode, { exact: true }));
}

/**
 * The "Request a new code" button of a Blazor verification page
 * @param page Playwright page instance on /blazor/signup/verify or /blazor/login/verify
 */
export function requestNewCodeButton(page: Page): Locator {
  return page.getByRole("button", { name: blazorTexts().requestNewCode, exact: true });
}

/**
 * Enter a one-time password on a Blazor verification page and submit it. A six-character code submits itself once it is
 * entered, so the adapter waits for that post instead of clicking; a shorter code is submitted with the Verify button.
 * @param page Playwright page instance on /blazor/signup/verify or /blazor/login/verify
 * @param code The code to submit; defaults to the environment's verification code
 */
export async function submitOneTimePassword(page: Page, code = getVerificationCode()): Promise<void> {
  const codeInput = verificationCodeInput(page);
  await expect(codeInput).toBeVisible();

  if (code.length !== oneTimePasswordLength) {
    await codeInput.fill(code);
    await page.getByRole("button", { name: blazorTexts().verify, exact: true }).click();
    return;
  }

  const posted = page.waitForRequest((request) => request.method() === "POST" && new URL(request.url()).pathname.endsWith("/verify"));
  await codeInput.fill(code);
  await posted;
}

/**
 * The length at which the verification page submits the code by itself
 */
const oneTimePasswordLength = 6;

/**
 * The client delay before a verification page reveals "Request a new code", plus one timer tick
 */
const resendRevealDelayMs = 31_000;

/**
 * Advance the page's fake clock past the resend delay and expect the resend action to be revealed. The test must call
 * page.clock.install() before the verification page loads; the verification timer measures elapsed time with the page's
 * clock, so no real 30 seconds pass and the server's resend and expiry limits are unaffected.
 * @param page Playwright page instance on /blazor/signup/verify or /blazor/login/verify
 */
export async function revealResendThroughBlazor(page: Page): Promise<void> {
  await expect(requestNewCodeButton(page)).toBeHidden();

  await page.clock.fastForward(resendRevealDelayMs);

  await expect(requestNewCodeButton(page)).toBeVisible();
}
