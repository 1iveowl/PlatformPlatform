import { test as base, expect, type Page } from "@playwright/test";
import { assertNoUnexpectedErrors, type TestContext } from "@shared/e2e/utils/test-assertions";
import { submitOneTimePassword } from "./one-time-password";
import { expectBlazorUrl, gotoBlazor } from "./routes";

/**
 * Error responses the Blazor host causes itself and that are owned outside the tests: below the second path segment
 * WebKit and Firefox resolve the scoped stylesheet preloads relative to the document and receive 404.
 */
const knownHostErrorResponseSuffix = ".bundle.scp.css - HTTP 404";

/**
 * The Playwright test for Blazor specs. Every test gets the built-in page fixture, which is a fresh browser context per
 * test with no stored authentication state, so nothing is shared between tests, projects or workers. An automatic
 * fixture asserts the error monitoring started by createTestContext when each test ends.
 */
export const test = base.extend<{ unexpectedErrorCheck: void }>({
  unexpectedErrorCheck: [
    async ({ page }, use) => {
      await use();

      const testContext = (page as Page & { __testContext?: TestContext }).__testContext;
      expect(testContext, "Start every Blazor test with createTestContext(page).").toBeDefined();
      const monitoring = testContext!.monitoring;
      monitoring.networkErrors = monitoring.networkErrors.filter((error) => !error.endsWith(knownHostErrorResponseSuffix));
      await assertNoUnexpectedErrors(testContext!);
    },
    { auto: true }
  ]
});

/**
 * Sign up a new user through the Blazor public pages: /blazor/signup, then /blazor/signup/verify with the environment's
 * one-time password, landing on the authenticated workspace at /blazor/app
 * @param page Playwright page instance in a fresh browser context
 * @param email A unique email address for the new user
 */
export async function signUpThroughBlazor(page: Page, email: string): Promise<void> {
  await gotoBlazor(page, "signup");
  await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();

  await page.getByTestId("email").fill(email);
  await page.getByTestId("submit").click();
  await expectBlazorUrl(page, "signup/verify");
  await expect(page.getByTestId("verify-email")).toContainText(email);

  await submitOneTimePassword(page);
  await expectBlazorUrl(page, "app");
  await expect(page.getByRole("heading", { name: "Your workspace" })).toBeVisible();
}
