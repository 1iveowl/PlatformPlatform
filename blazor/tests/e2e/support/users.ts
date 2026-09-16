import { expect, type Locator, type Page } from "@playwright/test";
import { expectNoPolicyViolations } from "./policy";
import { blazorPath, expectBlazorUrl } from "./routes";
import { blazorTexts } from "./texts";

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
  await expect(usersGrid(page).and(page.locator(':is([data-list-state="ready"], [data-list-state="empty"])'))).toBeVisible();
  if (totalCount !== undefined) await expect(usersGrid(page)).toHaveAttribute("data-list-total-count", String(totalCount));
}

/**
 * The users list, whose data-list-* attributes carry the list's loading state, total count, page offset and selection
 * @param page Playwright page instance on the users page
 */
export function usersGrid(page: Page): Locator {
  return page.getByTestId("users-grid");
}

/**
 * The data rows of the users list on the loaded page, leaving out the header row
 * @param page Playwright page instance on the users page
 */
export function userRows(page: Page): Locator {
  return usersGrid(page)
    .getByRole("row")
    .filter({ has: page.getByRole("cell") });
}

/**
 * The list row of the user with the given email on the loaded page
 * @param page Playwright page instance on the users page
 * @param email The user's email address
 */
export function userRow(page: Page, email: string): Locator {
  return userRows(page).filter({ has: page.getByRole("cell", { name: email, exact: true }) });
}

/**
 * The sort button in the header of a users list column
 * @param page Playwright page instance on the users page
 * @param columnTitle The localized title of the column
 */
export function sortButton(page: Page, columnTitle: string): Locator {
  return usersGrid(page).getByRole("columnheader").getByRole("button", { name: columnTitle });
}

/**
 * The side pane showing a user's profile, named by its title while it is open
 * @param page Playwright page instance on the users page
 */
export function profilePane(page: Page): Locator {
  return page.getByRole("complementary", { name: blazorTexts().userProfile, exact: true });
}

/**
 * Open a row's actions menu and return the menu
 * @param page Playwright page instance on the users page
 * @param email The email address of the row's user
 */
export async function openUserActions(page: Page, email: string): Promise<Locator> {
  await userRow(page, email).getByRole("button", { name: blazorTexts().userActions, exact: true }).click();

  const menu = page.getByRole("menu");
  await expect(menu).toBeVisible();
  return menu;
}

/**
 * Search the users list and wait for the search to reach the URL and the list
 * @param page Playwright page instance on the users page
 * @param search The text to search for
 * @param totalCount The expected number of matching users
 */
export async function searchUsers(page: Page, search: string, totalCount: number): Promise<void> {
  await page.getByRole("textbox", { name: blazorTexts().search }).fill(search);

  await expect(page).toHaveURL((url) => url.searchParams.get("search") === search);
  await expectUsersListLoaded(page, totalCount);
}
