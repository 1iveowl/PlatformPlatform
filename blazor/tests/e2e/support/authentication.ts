import { test as base, expect, type Locator, type Page } from "@playwright/test";
import { assertNoUnexpectedErrors, type TestContext } from "@shared/e2e/utils/test-assertions";
import { submitOneTimePassword } from "./one-time-password";
import { expectBlazorUrl, gotoBlazor } from "./routes";
import { blazorTexts } from "./texts";

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
  const texts = blazorTexts();
  await page.getByLabel(texts.email, { exact: true }).fill(email);
  await page.getByRole("button", { name: flow === "signup" ? texts.signUpWithEmail : texts.logInWithEmail, exact: true }).click();

  await expectBlazorUrl(page, `${flow}/verify`);
  await expect(page.getByText(email)).toBeVisible();
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
  const texts = blazorTexts();
  await expectBlazorUrl(page, "welcome");
  await page.getByLabel(texts.accountName, { exact: true }).fill(setup.accountName);
  await page.getByRole("button", { name: texts.continue, exact: true }).click();

  await expect(page.getByLabel(texts.firstName, { exact: true })).toBeVisible();
  await page.getByLabel(texts.firstName, { exact: true }).fill(setup.firstName);
  await page.getByLabel(texts.lastName, { exact: true }).fill(setup.lastName);
  await page.getByLabel(texts.title, { exact: true }).fill(setup.title ?? "");
  await page.getByRole("button", { name: texts.continue, exact: true }).click();

  await expectBlazorUrl(page, "app");
}

/**
 * The shell's user menu button, rendered once an authenticated Blazor page is interactive; the signed-in marker
 * @param page Playwright page instance
 */
export function userMenuButton(page: Page): Locator {
  return page.getByRole("button", { name: blazorTexts().userMenu, exact: true });
}

/**
 * Open the shell's user menu unless it is already open
 * @param page Playwright page instance on an interactive authenticated Blazor page
 */
export async function openUserMenu(page: Page): Promise<void> {
  const button = userMenuButton(page);
  if ((await button.getAttribute("aria-expanded")) !== "true") await button.click();
  await expect(page.getByRole("menu", { name: blazorTexts().userMenu, exact: true })).toBeVisible();
}

/**
 * The Log out item of the shell's user menu; open the menu with openUserMenu first
 * @param page Playwright page instance
 */
export function logOutButton(page: Page): Locator {
  return page.getByRole("menuitem", { name: blazorTexts().logOut, exact: true });
}

/**
 * Sign up a new user through the Blazor public pages: /blazor/signup, then /blazor/signup/verify with the environment's
 * one-time password, then the welcome setup, landing on the authenticated workspace at /blazor/app once its runtime has
 * started
 * @param page Playwright page instance in a fresh browser context
 * @param email A unique email address for the new user
 * @param accountName The name of the new user's tenant
 */
export async function signUpThroughBlazor(page: Page, email: string, accountName = "Blazor account"): Promise<void> {
  await gotoBlazor(page, "signup");
  await startEmailFlowThroughBlazor(page, "signup", email);

  await submitOneTimePassword(page);
  await completeWelcomeThroughBlazor(page, { accountName, firstName: "Blazor", lastName: "User" });
  await expect(userMenuButton(page)).toBeVisible();
}

/**
 * Log out from the interactive workspace and land on the Blazor login page
 * @param page Playwright page instance on an interactive authenticated Blazor page
 */
export async function logOutThroughBlazor(page: Page): Promise<void> {
  await openUserMenu(page);
  await logOutButton(page).click();

  await expectBlazorUrl(page, "login");
}

/**
 * Log in a user whose profile is set up through the Blazor public pages, landing on the authenticated workspace at
 * /blazor/app once its runtime has started
 * @param page Playwright page instance on any page of the gateway's origin, or a fresh browser context
 * @param email The user's email address
 */
export async function logInThroughBlazor(page: Page, email: string): Promise<void> {
  await gotoBlazor(page, "login");
  await startEmailFlowThroughBlazor(page, "login", email);
  await submitOneTimePassword(page);

  await expectBlazorUrl(page, "app");
  await expect(userMenuButton(page)).toBeVisible();
}

/**
 * Log in an existing user through the Blazor public pages and complete the profile step of the welcome setup that a user
 * invited to an account is sent to on the first login, landing on the authenticated workspace at /blazor/app
 * @param page Playwright page instance in a fresh browser context
 * @param email The invited user's email address
 * @param profile The first and last name to submit on the profile step
 */
export async function logInInvitedUserThroughBlazor(page: Page, email: string, profile: { firstName: string; lastName: string }): Promise<void> {
  await gotoBlazor(page, "login");
  await startEmailFlowThroughBlazor(page, "login", email);
  await submitOneTimePassword(page);

  const texts = blazorTexts();
  await expectBlazorUrl(page, "welcome");
  await page.getByLabel(texts.firstName, { exact: true }).fill(profile.firstName);
  await page.getByLabel(texts.lastName, { exact: true }).fill(profile.lastName);
  await page.getByRole("button", { name: texts.continue, exact: true }).click();

  await expectBlazorUrl(page, "app");
  await expect(userMenuButton(page)).toBeVisible();
}
