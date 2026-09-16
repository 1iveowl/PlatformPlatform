import { expect, type Page } from "@playwright/test";
import { getVerificationCode } from "@shared/e2e/utils/test-data";

/**
 * Enter a one-time password on a Blazor verification page and submit it. The Blazor pages use a plain text input
 * (data-testid="code") posted by a static server-rendered form, not the React input-otp control.
 * @param page Playwright page instance on /blazor/signup/verify or /blazor/login/verify
 * @param code The code to submit; defaults to the environment's verification code
 */
export async function submitOneTimePassword(page: Page, code = getVerificationCode()): Promise<void> {
  const codeInput = page.getByTestId("code");
  await expect(codeInput).toBeVisible();
  await codeInput.fill(code);

  await page.getByTestId("submit").click();
}

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
  await expect(page.getByTestId("resend-code")).toBeHidden();

  await page.clock.fastForward(resendRevealDelayMs);

  await expect(page.getByTestId("resend-code")).toBeVisible();
}
