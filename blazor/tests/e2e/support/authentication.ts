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
 * The runtime requests of the .NET WebAssembly runtime; a public page must issue none of them
 */
const webAssemblyRuntimeRequestPattern = /\/_framework\/(dotnet[^/]*\.js|[^/]*\.wasm|[^/]*\.dat|blazor\.boot\.json)(\?|$)/;

/**
 * Record every WebAssembly runtime request the page issues from now on
 * @param page Playwright page instance
 * @returns The live list of runtime request URLs, empty while no public page started the runtime
 */
export function trackWebAssemblyRequests(page: Page): string[] {
  const requests: string[] = [];
  page.on("request", (request) => {
    if (webAssemblyRuntimeRequestPattern.test(new URL(request.url()).pathname)) requests.push(request.url());
  });
  return requests;
}

/**
 * Start an email signup or login on a Blazor public page and wait for its verification page
 * @param page Playwright page instance on /blazor/signup or /blazor/login
 * @param flow The flow the page belongs to
 * @param email The email address to submit
 */
export async function startEmailFlowThroughBlazor(page: Page, flow: "signup" | "login", email: string): Promise<void> {
  await page.getByTestId("email").fill(email);
  await page.getByTestId("submit").click();

  await expectBlazorUrl(page, `${flow}/verify`);
  await expect(page.getByTestId("verify-email")).toContainText(email);
}

interface WelcomeSetup {
  accountName: string;
  firstName: string;
  lastName: string;
  title?: string;
}

/**
 * Complete the welcome setup a new account owner is sent to: name the tenant, then set up the profile, landing on the
 * authenticated workspace at /blazor/app
 * @param page Playwright page instance on /blazor/welcome at the account step
 * @param setup The tenant name and profile to submit
 */
export async function completeWelcomeThroughBlazor(page: Page, setup: WelcomeSetup): Promise<void> {
  await expectBlazorUrl(page, "welcome");
  await page.getByTestId("account-name").fill(setup.accountName);
  await page.getByTestId("continue").click();

  await expect(page.getByTestId("first-name")).toBeVisible();
  await page.getByTestId("first-name").fill(setup.firstName);
  await page.getByTestId("last-name").fill(setup.lastName);
  await page.getByTestId("title").fill(setup.title ?? "");
  await page.getByTestId("continue").click();

  await expectBlazorUrl(page, "app");
}

/**
 * Sign up a new user through the Blazor public pages: /blazor/signup, then /blazor/signup/verify with the environment's
 * one-time password, then the welcome setup, landing on the authenticated workspace at /blazor/app once its runtime has
 * started
 * @param page Playwright page instance in a fresh browser context
 * @param email A unique email address for the new user
 */
export async function signUpThroughBlazor(page: Page, email: string): Promise<void> {
  await gotoBlazor(page, "signup");
  await startEmailFlowThroughBlazor(page, "signup", email);

  await submitOneTimePassword(page);
  await completeWelcomeThroughBlazor(page, { accountName: "Blazor account", firstName: "Blazor", lastName: "User" });
  await expect(page.getByTestId("logout")).toBeVisible();
}

/**
 * Log out from the interactive workspace and land on the Blazor login page
 * @param page Playwright page instance on an interactive authenticated Blazor page
 */
export async function logOutThroughBlazor(page: Page): Promise<void> {
  await page.getByTestId("logout").click();

  await expectBlazorUrl(page, "login");
}
