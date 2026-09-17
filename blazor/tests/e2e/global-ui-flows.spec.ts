import { expect } from "@playwright/test";
import { logInThroughBlazor, logOutThroughBlazor, openUserMenu, signUpThroughBlazor, test, userMenuButton } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import {
  chooseThemeFromUserMenu,
  closeMobileMenuWithCloseButton,
  closeMobileMenuWithEscape,
  closeUserMenuWithEscape,
  expectAppliedTheme,
  expectThemeModeChecked,
  mainNavigation,
  mobileMenuButton,
  mobileMenuDialog,
  openMobileMenuByKeyboard,
  openUserMenuByKeyboard,
  sidebarToggle,
  userMenu
} from "@blazor/e2e/shell";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts } from "@blazor/e2e/texts";
import { createTestContext, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * The message of the exception the Development throwing fixture at /blazor/development/throw raises
 */
const fixtureFailureMessage = "The development fixture failed on purpose.";

const desktopViewport = { width: 1280, height: 800 };
const ultraHdViewport = { width: 3840, height: 2160 };
const tabletViewport = { width: 768, height: 1024 };
const mobileViewport = { width: 390, height: 844 };

/*
 * The Blazor edition of the account WebApp's global-ui-flows specification. The React preferences page does not exist in
 * the Blazor edition yet, so every theme change goes through the Change theme group of the shell's user menu. The mobile
 * menu has no theme entry, so the light theme the React test chooses on the mobile preferences page is chosen from the user
 * menu once the viewport is back at desktop size. The React test triggers the error page with the Konami code; here the
 * Development throwing fixture raises it, and Try again returns to the page that failed, which is the fixture again.
 */

test.describe("@smoke", () => {
  /**
   * The authenticated shell, the theme and the not-found page for a new account owner.
   * - The shell renders the main navigation with Home (current), Users and Profile
   * - The user menu opens with the keyboard, has no Switch account group for a user with one tenant and closes with Escape
   * - Dark chosen in the user menu is applied, is kept on reload, on logout at the login page and after logging in again
   * - An unknown route shows the not-found page inside the shell, and Go to home returns to the workspace
   * - No securitypolicyviolation event and no style attribute on any document
   */
  test("should render the shell, persist the theme across reload and logout, and show the not-found page", async ({ page }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const email = uniqueBlazorEmail();

    // === SHELL ===
    await step("Sign up & verify the shell renders the main navigation")(async () => {
      await signUpThroughBlazor(page, email);

      await expect(mainNavigation(page).getByRole("link", { name: texts.home, exact: true })).toHaveAttribute("aria-current", "page");
      await expect(mainNavigation(page).getByRole("link", { name: texts.users, exact: true })).toBeVisible();
      await expect(mainNavigation(page).getByRole("link", { name: texts.profile, exact: true })).toBeVisible();
      await expectAppliedTheme(page, "light");
      await expectNoPolicyViolations(page);
    })();

    await step("Open the user menu with the keyboard & verify no Switch account group for one tenant")(async () => {
      await openUserMenuByKeyboard(page);

      await expect(userMenu(page).getByRole("group", { name: texts.changeTheme, exact: true })).toBeVisible();
      await expect(userMenu(page).getByRole("group", { name: texts.switchAccount, exact: true })).toHaveCount(0);
      await expect(userMenu(page).getByRole("menuitem", { name: texts.logOut, exact: true })).toBeVisible();
    })();

    await step("Close the user menu with Escape & verify focus returns to its button")(async () => {
      await closeUserMenuWithEscape(page);

      await expect(userMenuButton(page)).toHaveAttribute("aria-expanded", "false");
    })();

    // === THEME ===
    await step("Choose Dark in the user menu & verify the dark theme is applied")(async () => {
      await chooseThemeFromUserMenu(page, "dark");

      await expectAppliedTheme(page, "dark");
      await expectThemeModeChecked(page, "dark");
      await expectNoPolicyViolations(page);
    })();

    await step("Reload the workspace & verify the dark theme is kept")(async () => {
      await page.reload();

      await expect(userMenuButton(page)).toBeVisible();
      await expectAppliedTheme(page, "dark");
      await expectThemeModeChecked(page, "dark");
      await expectNoPolicyViolations(page);
    })();

    await step("Log out & verify the login page keeps the dark theme")(async () => {
      await logOutThroughBlazor(page);

      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack, exact: true })).toBeVisible();
      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);
    })();

    await step("Log in again & verify the workspace keeps the dark theme")(async () => {
      await logInThroughBlazor(page, email);

      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);
    })();

    // === NOT FOUND ===
    await step("Navigate to an unknown route & verify the not-found page inside the shell")(async () => {
      await gotoBlazor(page, "app/does-not-exist");

      await expectNetworkErrors(context, [404]);
      await expect(page.getByRole("heading", { level: 1, name: texts.pageNotFound, exact: true })).toBeVisible();
      await expect(mainNavigation(page)).toBeVisible();
      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);
    })();

    await step("Click Go to home & verify the workspace renders")(async () => {
      await page.getByRole("link", { name: texts.goToHome, exact: true }).click();

      await expectBlazorUrl(page, "app");
      await expect(userMenuButton(page)).toBeVisible();
      await expect(mainNavigation(page).getByRole("link", { name: texts.home, exact: true })).toHaveAttribute("aria-current", "page");
      await expectNoPolicyViolations(page);
    })();
  });
});

test.describe("@comprehensive", () => {
  /**
   * Theme persistence across viewports, sidebar and mobile menu behaviour, a new tab and the error page.
   * - The users page starts in the light theme with a nonced script; Dark is kept on reload and enhanced navigation
   * - System follows the browser's light color scheme; Dark chosen at 4K is applied
   * - The sidebar collapsed at 4K stays collapsed after a reload and expands again
   * - At tablet size the theme is kept, the sidebar starts collapsed with the user menu reachable, and expands and collapses
   * - At mobile size the theme is kept, the sidebar is replaced by the mobile menu, which opens with the keyboard and closes
   *   with Escape and with Close menu, returning focus to its button
   * - Back at desktop size Light is applied and kept on reload; Dark chosen again is kept in a new tab
   * - The error page from the Development fixture renders inside the shell: Show details reveals the message, Try again
   *   loads the failing page again, Go to home returns to the workspace
   * - No securitypolicyviolation event and no style attribute on any document
   */
  test("should persist the theme across viewports and tabs and handle the sidebar, mobile menu and error page", async ({ page }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const email = uniqueBlazorEmail();

    // === THEME ON DESKTOP ===
    await step("Sign up and open Users from the main navigation & verify the light theme with a nonced script")(async () => {
      await page.setViewportSize(desktopViewport);
      await signUpThroughBlazor(page, email);

      await mainNavigation(page).getByRole("link", { name: texts.users, exact: true }).click();

      await expectBlazorUrl(page, "account/users");
      await expect(page.getByRole("heading", { name: texts.users, exact: true })).toBeVisible();
      await expectAppliedTheme(page, "light");
      await expect(page.locator("head script[nonce]").first()).toBeAttached();
      await expectNoPolicyViolations(page);
    })();

    await step("Choose Dark in the user menu and reload & verify the dark theme is kept")(async () => {
      await chooseThemeFromUserMenu(page, "dark");
      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);

      await page.reload();

      await expect(userMenuButton(page)).toBeVisible();
      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);
    })();

    await step("Navigate to Home and back to Users & verify the dark theme is kept")(async () => {
      await mainNavigation(page).getByRole("link", { name: texts.home, exact: true }).click();
      await expectBlazorUrl(page, "app");
      await expectAppliedTheme(page, "dark");

      await mainNavigation(page).getByRole("link", { name: texts.users, exact: true }).click();

      await expectBlazorUrl(page, "account/users");
      await expectAppliedTheme(page, "dark");
      await expectThemeModeChecked(page, "dark");
    })();

    await step("Choose System in the user menu & verify the browser's light color scheme is applied")(async () => {
      await chooseThemeFromUserMenu(page, "system");

      await expectAppliedTheme(page, "light");
      await expectThemeModeChecked(page, "system");
      await expectNoPolicyViolations(page);
    })();

    // === 4K ===
    await step("Resize to 4K and choose Dark & verify the dark theme is applied")(async () => {
      await page.setViewportSize(ultraHdViewport);
      await expectAppliedTheme(page, "light");

      await chooseThemeFromUserMenu(page, "dark");

      await expectAppliedTheme(page, "dark");
      await expect(sidebarToggle(page)).toHaveAttribute("aria-expanded", "true");
    })();

    await step("Collapse the sidebar at 4K and reload & verify the sidebar stays collapsed")(async () => {
      await sidebarToggle(page).click();
      await expect(sidebarToggle(page)).toHaveAttribute("aria-expanded", "false");
      await expect(mainNavigation(page)).toBeHidden();
      await expectNoPolicyViolations(page);

      await page.reload();

      await expect(userMenuButton(page)).toBeVisible();
      await expect(sidebarToggle(page)).toHaveAttribute("aria-expanded", "false");
      await expect(mainNavigation(page)).toBeHidden();
      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);
    })();

    await step("Expand the sidebar at 4K & verify the main navigation is shown")(async () => {
      await sidebarToggle(page).click();

      await expect(sidebarToggle(page)).toHaveAttribute("aria-expanded", "true");
      await expect(mainNavigation(page).getByRole("link", { name: texts.users, exact: true })).toBeVisible();
    })();

    // === TABLET ===
    await step("Resize to tablet & verify the dark theme and a collapsed sidebar with the user menu reachable")(async () => {
      await page.setViewportSize(tabletViewport);

      await expectAppliedTheme(page, "dark");
      await expect(sidebarToggle(page)).toHaveAttribute("aria-expanded", "false");
      await expect(mainNavigation(page)).toBeHidden();
      await openUserMenu(page);
      await expect(userMenu(page)).toBeInViewport({ ratio: 1 });
      await expect(userMenu(page).getByRole("menuitem", { name: texts.logOut, exact: true })).toBeVisible();
      await closeUserMenuWithEscape(page);
    })();

    await step("Expand and collapse the sidebar at tablet size & verify the navigation follows and the user menu stays reachable")(async () => {
      await sidebarToggle(page).click();
      await expect(sidebarToggle(page)).toHaveAttribute("aria-expanded", "true");
      await expect(mainNavigation(page).getByRole("link", { name: texts.users, exact: true })).toBeVisible();

      await sidebarToggle(page).click();

      await expect(sidebarToggle(page)).toHaveAttribute("aria-expanded", "false");
      await expect(mainNavigation(page)).toBeHidden();
      await expect(userMenuButton(page)).toBeInViewport({ ratio: 1 });
      await expectNoPolicyViolations(page);
    })();

    // === MOBILE ===
    await step("Resize to mobile & verify the dark theme and the mobile menu button replacing the sidebar")(async () => {
      await page.setViewportSize(mobileViewport);

      await expectAppliedTheme(page, "dark");
      await expect(mobileMenuButton(page)).toBeVisible();
      await expect(sidebarToggle(page)).toBeHidden();
      await expect(userMenuButton(page)).toBeHidden();
    })();

    await step("Open the mobile menu with the keyboard and press Escape & verify it closes and focus returns")(async () => {
      await openMobileMenuByKeyboard(page);
      await expect(mobileMenuDialog(page).getByRole("link", { name: texts.users, exact: true })).toBeVisible();
      await expect(mobileMenuDialog(page).getByRole("button", { name: texts.logOut, exact: true })).toBeVisible();

      await closeMobileMenuWithEscape(page);

      await expect(mobileMenuButton(page)).toHaveAttribute("aria-expanded", "false");
    })();

    await step("Open the mobile menu with the keyboard and press Close menu & verify it closes and focus returns")(async () => {
      await openMobileMenuByKeyboard(page);

      await closeMobileMenuWithCloseButton(page);

      await expect(mobileMenuButton(page)).toHaveAttribute("aria-expanded", "false");
      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);
    })();

    // === BACK TO DESKTOP ===
    await step("Resize to desktop, choose Light and reload & verify the light theme is kept")(async () => {
      await page.setViewportSize(desktopViewport);
      await expect(userMenuButton(page)).toBeVisible();
      await chooseThemeFromUserMenu(page, "light");
      await expectAppliedTheme(page, "light");
      await expectNoPolicyViolations(page);

      await page.reload();

      await expect(userMenuButton(page)).toBeVisible();
      await expectAppliedTheme(page, "light");
      await expectThemeModeChecked(page, "light");
      await expect(sidebarToggle(page)).toHaveAttribute("aria-expanded", "true");
      await expectNoPolicyViolations(page);
    })();

    await step("Choose Dark again and open Users in a new tab & verify the new tab keeps the dark theme")(async () => {
      await chooseThemeFromUserMenu(page, "dark");
      await expectAppliedTheme(page, "dark");
      const newTab = await page.context().newPage();
      await trackPolicyViolations(newTab);

      await gotoBlazor(newTab, "account/users");

      await expect(userMenuButton(newTab)).toBeVisible();
      await expectAppliedTheme(newTab, "dark");
      await expectThemeModeChecked(newTab, "dark");
      await expectNoPolicyViolations(newTab);
      await newTab.close();
    })();

    // === ERROR PAGE ===
    await step("Open the Development throwing fixture & verify the error page inside the shell")(async () => {
      await expectNoPolicyViolations(page);

      await gotoBlazor(page, "development/throw");

      await expectNetworkErrors(context, [500]);
      await expect(page.getByRole("heading", { level: 1, name: texts.somethingWentWrong, exact: true })).toBeVisible();
      await expect(mainNavigation(page)).toBeVisible();
      await expect(page.getByText(fixtureFailureMessage, { exact: true })).toBeHidden();
      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);
    })();

    await step("Click Show details & verify the exception message is revealed")(async () => {
      await page.getByText(texts.showDetails, { exact: true }).click();

      await expect(page.getByText(fixtureFailureMessage, { exact: true })).toBeVisible();
      await expect(page.getByText(texts.hideDetails, { exact: true })).toBeVisible();
      await expect(page.getByText(texts.showDetails, { exact: true })).toBeHidden();
    })();

    await step("Click Try again & verify the failing page is loaded again with the details closed")(async () => {
      await page.getByRole("link", { name: texts.tryAgain, exact: true }).click();

      await expectBlazorUrl(page, "development/throw");
      await expectNetworkErrors(context, [500]);
      await expect(page.getByRole("heading", { level: 1, name: texts.somethingWentWrong, exact: true })).toBeVisible();
      await expect(page.getByText(texts.showDetails, { exact: true })).toBeVisible();
      await expect(page.getByText(fixtureFailureMessage, { exact: true })).toBeHidden();
      await expectNoPolicyViolations(page);
    })();

    await step("Click Go to home on the error page & verify the workspace renders")(async () => {
      await page.getByRole("link", { name: texts.goToHome, exact: true }).click();

      await expectBlazorUrl(page, "app");
      await expect(userMenuButton(page)).toBeVisible();
      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);
    })();
  });
});
