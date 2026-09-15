import { expect, type Locator, type Page } from "@playwright/test";
import { expectNoPolicyViolations } from "./policy";
import { blazorPath, expectBlazorUrl } from "./routes";

/**
 * The Blazor users page below the path base
 */
export const usersRoute = "account/users";

/**
 * Open the users page with the given query string as a new document, after expecting the current document, if tracked,
 * to have raised no policy violation, and wait for the list to finish loading
 * @param page Playwright page instance of a signed-in user
 * @param query The query string including its leading "?", or empty
 */
export async function gotoUsersPage(page: Page, query = ""): Promise<void> {
  if (page.url().startsWith("http")) await expectNoPolicyViolations(page);

  await page.goto(`${blazorPath(usersRoute)}${query}`);

  await expectBlazorUrl(page, usersRoute);
  await expectUsersListLoaded(page);
}

/**
 * Expect the users list to have finished loading, with the given total when one is passed
 * @param page Playwright page instance on the users page
 * @param totalCount The expected number of users across all pages
 */
export async function expectUsersListLoaded(page: Page, totalCount?: number): Promise<void> {
  await expect(page.locator('[data-testid="users-grid"]:is([data-list-state="ready"], [data-list-state="empty"])')).toBeVisible();
  if (totalCount !== undefined) await expect(page.getByTestId("users-grid")).toHaveAttribute("data-list-total-count", String(totalCount));
}

/**
 * The list row of the user with the given email on the loaded page
 * @param page Playwright page instance on the users page
 * @param email The user's email address
 */
export function userRow(page: Page, email: string): Locator {
  return page.getByTestId("users-grid").locator("tr.data-list-row").filter({ has: page.locator(`[data-email="${email}"]`) });
}

/**
 * Open a row's actions menu and return the menu
 * @param page Playwright page instance on the users page
 * @param email The email address of the row's user
 * @param userActionsLabel The localized accessible name of the actions button
 */
export async function openUserActions(page: Page, email: string, userActionsLabel: string): Promise<Locator> {
  await userRow(page, email).getByRole("button", { name: userActionsLabel }).click();

  const menu = page.getByRole("menu");
  await expect(menu).toBeVisible();
  return menu;
}

/**
 * Search the users list and wait for the search to reach the URL and the list
 * @param page Playwright page instance on the users page
 * @param searchLabel The localized accessible name of the search box
 * @param search The text to search for
 * @param totalCount The expected number of matching users
 */
export async function searchUsers(page: Page, searchLabel: string, search: string, totalCount: number): Promise<void> {
  await page.getByRole("textbox", { name: searchLabel }).fill(search);

  await expect(page).toHaveURL((url) => url.searchParams.get("search") === search);
  await expectUsersListLoaded(page, totalCount);
}
