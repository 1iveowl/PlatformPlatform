import { expect, type Locator, type Page } from "@playwright/test";
import { getVerificationCode } from "@shared/e2e/utils/test-data";
import { blazorTexts } from "./texts";

/**
 * The verification code input of a Blazor verification page, labelled for login or for signup. The Blazor pages render one
 * text input posted by a static server-rendered form, which js/one-time-password.js draws as six slots; the input stays the
 * labelled field, so it is located by its label in both states and without scripts.
 * @param page Playwright page instance on /blazor/signup/verify or /blazor/login/verify
 */
export function verificationCodeInput(page: Page): Locator {
  const texts = blazorTexts();
  return page.getByLabel(texts.loginVerificationCode, { exact: true }).or(page.getByLabel(texts.signupVerificationCode, { exact: true }));
}

/**
 * The Verify button of a Blazor verification page
 * @param page Playwright page instance on /blazor/signup/verify or /blazor/login/verify
 */
export function verifyButton(page: Page): Locator {
  return page.getByRole("button", { name: blazorTexts().verify, exact: true });
}

/**
 * The "Request a new code" button of a Blazor verification page
 * @param page Playwright page instance on /blazor/signup/verify or /blazor/login/verify
 */
export function requestNewCodeButton(page: Page): Locator {
  return page.getByRole("button", { name: blazorTexts().requestNewCode, exact: true });
}

/**
 * How a code is entered: filled as autofill does, pasted as one input event, or typed key by key
 */
export type OneTimePasswordEntry = "fill" | "paste" | "keys";

/**
 * Enter a one-time password on a Blazor verification page and submit it. A six-character code submits itself once it is
 * entered, so the adapter waits for that post instead of clicking; a shorter code is submitted with the Verify button. A
 * page without scripts uses submitOneTimePasswordWithoutScripts.
 * @param page Playwright page instance on /blazor/signup/verify or /blazor/login/verify
 * @param code The code to submit; defaults to the environment's verification code
 * @param entry How the code is entered into the input
 */
export async function submitOneTimePassword(page: Page, code = getVerificationCode(), entry: OneTimePasswordEntry = "fill"): Promise<void> {
  const codeInput = verificationCodeInput(page);
  await expect(codeInput).toBeVisible();

  if (code.length !== oneTimePasswordLength) {
    await enterOneTimePassword(page, codeInput, code, entry);
    await verifyButton(page).click();
    return;
  }

  const posted = page.waitForRequest((request) => request.method() === "POST" && new URL(request.url()).pathname.endsWith("/verify"));
  await enterOneTimePassword(page, codeInput, code, entry);
  await posted;
}

async function enterOneTimePassword(page: Page, codeInput: Locator, code: string, entry: OneTimePasswordEntry): Promise<void> {
  if (entry === "fill") return codeInput.fill(code);
  await codeInput.focus();
  if (entry === "paste") return page.keyboard.insertText(code);
  await codeInput.pressSequentially(code);
}

/**
 * Fill a one-time password into the plain input of a verification page loaded without scripts and submit it with Verify,
 * the fallback js/one-time-password.js enhances
 * @param page Playwright page instance in a browser context with javaScriptEnabled false
 * @param code The code to submit
 */
export async function submitOneTimePasswordWithoutScripts(page: Page, code: string): Promise<void> {
  const codeInput = verificationCodeInput(page);
  await expect(codeInput).toBeVisible();

  await codeInput.fill(code);
  await verifyButton(page).click();
}

/**
 * Count the posts of the verification form from now on, so a test can expect that a code entered once is posted once
 * @param page Playwright page instance, before the code is entered
 * @returns The live count of verification posts
 */
export function trackVerificationPosts(page: Page): { count: number } {
  const posts = { count: 0 };
  page.on("request", (request) => {
    if (request.method() === "POST" && new URL(request.url()).pathname.endsWith("/verify")) posts.count++;
  });
  return posts;
}

/**
 * Enter a six-character code and click Verify in the same task, the double submit the auto-submit guard must fold into one
 * post; wait for the verification page the post answers with
 * @param page Playwright page instance on an enhanced verification page
 * @param code A six-character code
 */
export async function submitOneTimePasswordTwiceAtOnce(page: Page, code: string): Promise<void> {
  const answered = page.waitForResponse((response) => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith("/verify"));
  const verify = await verifyButton(page).elementHandle();
  await verificationCodeInput(page).evaluate(
    (input: HTMLInputElement, { value, button }) => {
      input.value = value;
      input.dispatchEvent(new Event("input", { bubbles: true }));
      (button as HTMLButtonElement).click();
    },
    { value: code, button: verify }
  );
  await answered;
}

/**
 * Enable the input and Verify button of a locked verification page in the browser, the way a user could with developer
 * tools; the account API must still refuse the attempt
 * @param page Playwright page instance on a verification page in the locked state
 */
export async function enableLockedVerificationControls(page: Page): Promise<void> {
  await verificationCodeInput(page).evaluate((input: HTMLInputElement) => {
    input.disabled = false;
  });
  await verifyButton(page).evaluate((button: HTMLButtonElement) => {
    button.disabled = false;
  });
}

/**
 * The state a verification page shows for a refused code or resend: the localized state label above the account API's
 * message, which stays in English as returned
 */
export type VerificationState = "wrongCode" | "expired" | "locked";

/**
 * Expect a verification page to show a state: the alert with the localized state label and the account API's message
 * @param page Playwright page instance on /blazor/signup/verify or /blazor/login/verify
 * @param state The state the page shows
 * @param apiMessage The message the account API returned
 */
export async function expectVerificationState(page: Page, state: VerificationState, apiMessage: string): Promise<void> {
  const texts = blazorTexts();
  const label = { wrongCode: texts.verificationStateWrongCode, expired: texts.verificationStateExpired, locked: texts.verificationStateLocked }[state];
  const alert = page.getByRole("alert").filter({ hasText: label });

  await expect(alert.getByText(label, { exact: true })).toBeVisible();
  await expect(alert.getByText(apiMessage, { exact: true })).toBeVisible();
}

/**
 * The length at which the verification page submits the code by itself
 */
const oneTimePasswordLength = 6;

/**
 * A six-letter code that is never the given code: every letter moves one place on in the alphabet
 * @param code A six-letter code
 */
export function wrongCodeFor(code: string): string {
  return [...code.toUpperCase()].map((letter) => String.fromCharCode(((letter.charCodeAt(0) - 65 + 1) % 26) + 65)).join("");
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
  await expect(requestNewCodeButton(page)).toBeHidden();

  await page.clock.fastForward(resendRevealDelayMs);

  await expect(requestNewCodeButton(page)).toBeVisible();
}
