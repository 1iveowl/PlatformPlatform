import { expect, type Locator, type Page } from "@playwright/test";
import { userMenuButton } from "./authentication";
import { mainNavigation, mobileMenuButton, mobileMenuDialog, sidebarToggle, themeLabel, type ThemeMode } from "./shell";
import { blazorTexts } from "./texts";

/**
 * A viewport the mobile specifications run at
 */
export interface Viewport {
  width: number;
  height: number;
}

/**
 * The phone the mobile specifications run at, the width the React mobile view test uses
 */
export const phoneViewport: Viewport = { width: 390, height: 844 };

/**
 * The smallest phone the mobile specifications run at, the second width of the React mobile view test
 */
export const smallPhoneViewport: Viewport = { width: 375, height: 667 };

/**
 * The desktop width the shell shows the sidebar and the docked side pane at
 */
export const desktopViewport: Viewport = { width: 1280, height: 900 };

/**
 * Resize the page to a viewport
 * @param page Playwright page instance
 * @param viewport The viewport to resize to
 */
export async function resizeTo(page: Page, viewport: Viewport): Promise<void> {
  await page.setViewportSize(viewport);
}

/**
 * Expect the authenticated shell to show its phone chrome: the sidebar with the main navigation and the user menu, which
 * carries the theme, the language and the way to reach support on wider screens, are all hidden, and the floating Open
 * navigation menu button is the way to every one of them
 * @param page Playwright page instance on an interactive authenticated Blazor page below the small breakpoint
 */
export async function expectPhoneChrome(page: Page): Promise<void> {
  await expect(mobileMenuButton(page)).toBeVisible();
  await expect(mainNavigation(page)).toBeHidden();
  await expect(sidebarToggle(page)).toBeHidden();
  await expect(userMenuButton(page)).toBeHidden();
}

/**
 * Open the mobile navigation menu by touch: tap its button, which is how a phone reaches the navigation
 * @param page Playwright page instance below the small breakpoint with the mobile menu closed, in a context with touch
 */
export async function openMobileMenuByTouch(page: Page): Promise<void> {
  await mobileMenuButton(page).tap();

  await expect(mobileMenuDialog(page)).toBeVisible();
  await expect(mobileMenuButton(page)).toHaveAttribute("aria-expanded", "true");
}

/**
 * Close the open mobile navigation menu by touch, through its Close menu button
 * @param page Playwright page instance with the mobile menu open, in a context with touch
 */
export async function closeMobileMenuByTouch(page: Page): Promise<void> {
  await mobileMenuDialog(page).getByRole("button", { name: blazorTexts().closeMenu, exact: true }).tap();

  await expect(mobileMenuDialog(page)).toBeHidden();
  await expect(mobileMenuButton(page)).toHaveAttribute("aria-expanded", "false");
}

/**
 * A navigation link of the open mobile navigation menu
 * @param page Playwright page instance with the mobile menu open
 * @param name The link's localized name
 */
export function mobileMenuLink(page: Page, name: string): Locator {
  return mobileMenuDialog(page)
    .getByRole("navigation", { name: blazorTexts().mainNavigation, exact: true })
    .getByRole("link", { name, exact: true });
}

/**
 * Follow a navigation link of the open mobile navigation menu by touch; the menu closes with the navigation
 * @param page Playwright page instance with the mobile menu open, in a context with touch
 * @param name The link's localized name
 */
export async function tapMobileMenuLink(page: Page, name: string): Promise<void> {
  await mobileMenuLink(page, name).tap();

  await expect(mobileMenuDialog(page)).toBeHidden();
}

/**
 * The button of a theme mode in the Change theme group of the open mobile navigation menu; its aria-pressed reports
 * whether the mode is the chosen one
 * @param page Playwright page instance with the mobile menu open
 * @param mode The theme mode
 */
export function mobileMenuThemeButton(page: Page, mode: ThemeMode): Locator {
  return mobileMenuDialog(page)
    .getByRole("group", { name: blazorTexts().changeTheme, exact: true })
    .getByRole("button", { name: themeLabel(mode), exact: true });
}

/**
 * The button of a language in the Change language group of the open mobile navigation menu; its aria-pressed reports
 * whether the language is the running culture
 * @param page Playwright page instance with the mobile menu open
 * @param languageName The language's own name, for example "English"
 */
export function mobileMenuLanguageButton(page: Page, languageName: string): Locator {
  return mobileMenuDialog(page)
    .getByRole("group", { name: blazorTexts().changeLanguage, exact: true })
    .getByRole("button", { name: languageName, exact: true });
}

/**
 * The mail link to support in the open mobile navigation menu, which renders only when the brand names an address
 * @param page Playwright page instance with the mobile menu open
 */
export function mobileMenuSupportLink(page: Page): Locator {
  return mobileMenuDialog(page).getByRole("link", { name: blazorTexts().contactSupport, exact: true });
}

/**
 * Press and hold an element with a touch pointer. Playwright's touchscreen can only tap, so the hold is synthesized as the
 * two touch pointerdown events the row menu listens for; a press on a physical screen is a manual check.
 * @param target The element to press and hold
 */
export async function longPressByTouch(target: Locator): Promise<void> {
  const pointer = { pointerType: "touch", isPrimary: true, pointerId: 7, clientX: 10, clientY: 10 };
  await target.dispatchEvent("pointerdown", pointer);
  await target.dispatchEvent("pointerdown", pointer);
}

/**
 * End a synthesized touch press on an element
 * @param target The element the press was started on
 */
export async function releaseTouchPress(target: Locator): Promise<void> {
  await target.dispatchEvent("pointerup", { pointerType: "touch", isPrimary: true, pointerId: 7, clientX: 10, clientY: 10 });
}
