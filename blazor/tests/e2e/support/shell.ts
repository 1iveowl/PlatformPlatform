import { expect, type Locator, type Page } from "@playwright/test";
import { openUserMenu, userMenuButton } from "./authentication";
import { blazorTexts } from "./texts";

/**
 * The theme modes of the shell's user menu, keyed to their names in the culture text map
 */
export type ThemeMode = "system" | "light" | "dark";

/**
 * The theme a document applies after resolving the mode; System follows the browser's preferred color scheme
 */
export type AppliedTheme = "light" | "dark";

/**
 * The sidebar's main navigation of the shell; hidden while the sidebar is collapsed and below the small breakpoint
 * @param page Playwright page instance on an authenticated Blazor page
 */
export function mainNavigation(page: Page): Locator {
  return page.getByRole("navigation", { name: blazorTexts().mainNavigation, exact: true });
}

/**
 * The shell's Toggle sidebar button, whose aria-expanded reports whether the sidebar is expanded
 * @param page Playwright page instance on an interactive authenticated Blazor page
 */
export function sidebarToggle(page: Page): Locator {
  return page.getByRole("button", { name: blazorTexts().toggleSidebar, exact: true });
}

/**
 * The shell's user menu once it is open
 * @param page Playwright page instance on an interactive authenticated Blazor page
 */
export function userMenu(page: Page): Locator {
  return page.getByRole("menu", { name: blazorTexts().userMenu, exact: true });
}

/**
 * The item of a theme mode in the Change theme group of the open user menu
 * @param page Playwright page instance with the user menu open
 * @param mode The theme mode
 */
export function themeMenuItem(page: Page, mode: ThemeMode): Locator {
  const texts = blazorTexts();
  const name = { system: texts.themeSystem, light: texts.themeLight, dark: texts.themeDark }[mode];
  return userMenu(page).getByRole("group", { name: texts.changeTheme, exact: true }).getByRole("menuitemradio", { name, exact: true });
}

/**
 * Open the shell's user menu with the keyboard: focus its button and press Enter
 * @param page Playwright page instance on an interactive authenticated Blazor page with the user menu closed
 */
export async function openUserMenuByKeyboard(page: Page): Promise<void> {
  await userMenuButton(page).focus();
  await page.keyboard.press("Enter");

  await expect(userMenu(page)).toBeVisible();
  await expect(userMenuButton(page)).toHaveAttribute("aria-expanded", "true");
}

/**
 * Close the open user menu with Escape once the menu has moved focus to its first item, Profile; focus returns to the user
 * menu button
 * @param page Playwright page instance with the user menu open
 */
export async function closeUserMenuWithEscape(page: Page): Promise<void> {
  await expect(userMenu(page).getByRole("menuitem", { name: blazorTexts().profile, exact: true })).toBeFocused();
  await page.keyboard.press("Escape");

  await expect(userMenu(page)).toBeHidden();
  await expect(userMenuButton(page)).toBeFocused();
}

/**
 * Choose a theme mode in the user menu; the menu closes once the mode is chosen
 * @param page Playwright page instance on an interactive authenticated Blazor page
 * @param mode The theme mode to choose
 */
export async function chooseThemeFromUserMenu(page: Page, mode: ThemeMode): Promise<void> {
  await openUserMenu(page);
  await themeMenuItem(page, mode).click();

  await expect(userMenu(page)).toBeHidden();
}

/**
 * Expect the user menu to mark a theme mode as the chosen one, then close the menu again
 * @param page Playwright page instance on an interactive authenticated Blazor page with the user menu closed
 * @param mode The theme mode expected to be checked
 */
export async function expectThemeModeChecked(page: Page, mode: ThemeMode): Promise<void> {
  await openUserMenu(page);

  await expect(themeMenuItem(page, mode)).toHaveAttribute("aria-checked", "true");
  await closeUserMenuWithEscape(page);
}

/**
 * Expect the document to apply a theme. The theme is the data-theme attribute of the root element, which has no role or
 * accessible name, so this is the one place the specs read it
 * @param page Playwright page instance
 * @param theme The applied theme
 */
export async function expectAppliedTheme(page: Page, theme: AppliedTheme): Promise<void> {
  await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
}

/**
 * The floating Open navigation menu button shown below the small breakpoint
 * @param page Playwright page instance on an interactive authenticated Blazor page
 */
export function mobileMenuButton(page: Page): Locator {
  return page.getByRole("button", { name: blazorTexts().openNavigationMenu, exact: true });
}

/**
 * The mobile navigation menu dialog
 * @param page Playwright page instance on an interactive authenticated Blazor page
 */
export function mobileMenuDialog(page: Page): Locator {
  return page.getByRole("dialog", { name: blazorTexts().mobileNavigationMenu, exact: true });
}

/**
 * Open the mobile navigation menu with the keyboard: focus its button and press Enter
 * @param page Playwright page instance below the small breakpoint with the mobile menu closed
 */
export async function openMobileMenuByKeyboard(page: Page): Promise<void> {
  await mobileMenuButton(page).focus();
  await page.keyboard.press("Enter");

  await expect(mobileMenuDialog(page)).toBeVisible();
  await expect(mobileMenuButton(page)).toHaveAttribute("aria-expanded", "true");
}

/**
 * Close the open mobile navigation menu with Escape; focus returns to the Open navigation menu button
 * @param page Playwright page instance with the mobile menu open
 */
export async function closeMobileMenuWithEscape(page: Page): Promise<void> {
  await page.keyboard.press("Escape");

  await expect(mobileMenuDialog(page)).toBeHidden();
  await expect(mobileMenuButton(page)).toBeFocused();
}

/**
 * Close the open mobile navigation menu with the keyboard through its Close menu button; focus returns to the Open
 * navigation menu button
 * @param page Playwright page instance with the mobile menu open
 */
export async function closeMobileMenuWithCloseButton(page: Page): Promise<void> {
  await mobileMenuDialog(page).getByRole("button", { name: blazorTexts().closeMenu, exact: true }).focus();
  await page.keyboard.press("Enter");

  await expect(mobileMenuDialog(page)).toBeHidden();
  await expect(mobileMenuButton(page)).toBeFocused();
}
