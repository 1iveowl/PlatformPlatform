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
