import { expect } from "@playwright/test";
import { inviteUsersThroughAccountApi } from "@blazor/e2e/account-api";
import { logOutThroughBlazor, signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, expectBlazorUrl } from "@blazor/e2e/routes";
import { mobileMenuButton, mobileMenuDialog } from "@blazor/e2e/shell";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts } from "@blazor/e2e/texts";
import { blazorToast } from "@blazor/e2e/toast";
import {
  expectUsersListLoaded,
  gotoUsersPage,
  inviteUserThroughDialog,
  phoneUserRow,
  profilePane,
  searchUsers,
  userRows,
  usersGrid,
  usersRoute
} from "@blazor/e2e/users";
import { createTestContext, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

const phoneViewport = { width: 390, height: 844 };
const smallPhoneViewport = { width: 375, height: 667 };
const desktopViewport = { width: 1280, height: 900 };

/**
 * The users invited on top of the three invited through the dialog, so the list has a second server page of 25 rows
 */
const paddingUserCount = 27;

/**
 * How long the account API's answer to a superseded search is held back, so it arrives after the answer to the search that
 * replaced it
 */
const delayedSearchMilliseconds = 2_000;

test.describe("@comprehensive", () => {
  test.use({ hasTouch: true });

  /**
   * The users parts of the React mobile view test on phone viewports, with the phone list's loading, the long-press row menu
   * and the resilience of the loaded range; the navigation parts belong to the shell's mobile specification
   * - At 390 by 844: the users page reached through the mobile menu, three users invited through the dialog, one visible data
   *   column with the email under the name, a tap opening the side pane, arrows moving without opening it, Enter opening it,
   *   and Escape and the close button closing it
   * - The row menu opened by a touch long-press, synthesized as touch pointer events because Playwright's touchscreen can
   *   only tap, without activating the row, and by a right-click
   * - The next page appended through the Load more button with one request and the status announced, the loaded range restored after a reload and after
   *   Back from another document with an empty page cache, and kept when resizing to desktop paging and back
   * - A search superseded while its response is delayed never replaces the newer search's rows
   * - At 375 by 667: a tap and then the keyboard keep a single selection
   * - Logging out and signing up another account shows none of the previous account's rows, even for a deep link to the
   *   previous account's second page, which the list recovers from through the API's out-of-range refusal
   * - No securitypolicyviolation event and no style attribute on any document
   */
  test("should handle the users list, its side pane and row menu on phones", async ({ page }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const ownerEmail = uniqueBlazorEmail();
    const invitedEmails = [1, 2, 3].map((index) => `phone-${index}-${uniqueBlazorEmail()}`);
    const [firstEmail, secondEmail, thirdEmail] = invitedEmails;
    const paddingEmails = Array.from({ length: paddingUserCount }, (_, index) => `padding-${index}-${uniqueBlazorEmail()}`);
    const totalUsers = paddingUserCount + invitedEmails.length + 1;

    // === PHONE USERS PAGE ===
    await step("Sign up, resize to a phone and open Users from the mobile menu & verify the phone list with one data column")(async () => {
      await page.setViewportSize(desktopViewport);
      await signUpThroughBlazor(page, ownerEmail);
      await inviteUsersThroughAccountApi(page, paddingEmails);
      await page.setViewportSize(phoneViewport);

      await mobileMenuButton(page).tap();
      await expect(mobileMenuDialog(page)).toBeVisible();
      await mobileMenuDialog(page).getByRole("link", { name: texts.users, exact: true }).tap();

      await expectBlazorUrl(page, usersRoute);
      await expectUsersListLoaded(page, totalUsers - invitedEmails.length);
      await expect(usersGrid(page)).toHaveAttribute("data-list-load-mode", "infinite");
      await expect(usersGrid(page).getByRole("columnheader", { name: texts.emailColumn })).toBeHidden();
      await expect(usersGrid(page).getByRole("button", { name: texts.nextPage, exact: true })).toHaveCount(0);
    })();

    await step("Invite three users through the dialog on the phone & verify each toast and the list total")(async () => {
      for (const email of invitedEmails) {
        await inviteUserThroughDialog(page, email);

        await expect(blazorToast(page, texts.userInvited).first()).toBeVisible();
      }

      await expectUsersListLoaded(page, totalUsers);
      for (const _ of invitedEmails) {
        await blazorToast(page, texts.userInvited).first().getByRole("button", { name: texts.dismissNotification, exact: true }).tap();
      }
      await expect(blazorToast(page, texts.userInvited)).toHaveCount(0);
      await expectNoPolicyViolations(page);
    })();

    // === LOADING ===
    await step("Activate Load more without scrolling to the end & verify one request appends the second page, announced and in the URL")(async () => {
      const requestUrls: URL[] = [];
      page.on("request", (request) => requestUrls.push(new URL(request.url())));
      await usersGrid(page).getByRole("button", { name: texts.loadMore, exact: true }).dispatchEvent("click");

      await expect(usersGrid(page)).toHaveAttribute("data-list-loaded-count", String(totalUsers));
      await expect(userRows(page)).toHaveCount(totalUsers);
      await expect(usersGrid(page).getByRole("status")).toHaveText(texts.allRowsLoaded);
      await expect(page).toHaveURL((url) => url.searchParams.get("pageOffset") === "1");
      await expect(usersGrid(page).getByRole("button", { name: texts.loadMore, exact: true })).toHaveCount(0);
      expect(requestUrls.filter((url) => url.pathname === "/api/account/users" && url.searchParams.get("PageOffset") === "1")).toHaveLength(1);
    })();

    // === SIDE PANE ===
    await step("Tap a row and close the pane with its close button & verify the pane opens and closes")(async () => {
      await phoneUserRow(page, firstEmail).getByRole("cell").first().tap();

      const pane = profilePane(page);
      await expect(pane.getByTestId("profile-email")).toHaveText(firstEmail);
      await expect(page).toHaveURL((url) => url.searchParams.has("userId"));
      await pane.getByRole("button", { name: texts.closeUserProfile, exact: true }).tap();
      await expect(pane).toBeHidden();
      await expect(page).toHaveURL((url) => !url.searchParams.has("userId"));
    })();

    await step("Move between rows with the arrows, open with Enter and close with Escape & verify only Enter opens the pane")(async () => {
      await phoneUserRow(page, secondEmail).focus();
      await page.keyboard.press("ArrowDown");

      await expect(phoneUserRow(page, thirdEmail)).toBeFocused();
      await expect(profilePane(page)).toBeHidden();
      await page.keyboard.press("Enter");
      await expect(profilePane(page).getByTestId("profile-email")).toHaveText(thirdEmail);
      await page.keyboard.press("Escape");
      await expect(profilePane(page)).toBeHidden();
      await expect(phoneUserRow(page, thirdEmail)).toBeFocused();
      await expect(page).toHaveURL((url) => !url.searchParams.has("userId"));
    })();

    // === ROW MENU ===
    await step("Long-press a row with touch and right-click another & verify the row menu opens without opening the pane")(async () => {
      const pressedCell = phoneUserRow(page, firstEmail).getByRole("cell").first();
      await pressedCell.dispatchEvent("pointerdown", { pointerType: "touch", isPrimary: true, pointerId: 7, clientX: 10, clientY: 10 });
      await pressedCell.dispatchEvent("pointerdown", { pointerType: "touch", isPrimary: true, pointerId: 7, clientX: 10, clientY: 10 });

      const menu = page.getByRole("menu");
      await expect(menu).toBeVisible();
      await expect(menu.getByRole("menuitem", { name: texts.viewProfile, exact: true })).toBeVisible();
      await pressedCell.dispatchEvent("pointerup", { pointerType: "touch", isPrimary: true, pointerId: 7, clientX: 10, clientY: 10 });
      await expect(profilePane(page)).toBeHidden();
      await page.keyboard.press("Escape");
      await expect(menu).toBeHidden();

      await phoneUserRow(page, secondEmail).getByRole("cell").first().click({ button: "right" });
      await expect(menu).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(menu).toBeHidden();
      await expect(profilePane(page)).toBeHidden();
    })();

    // === RANGE RESTORE ===
    await step("Reload, leave for another document and go Back & verify the loaded range is restored with an empty page cache")(async () => {
      await expectNoPolicyViolations(page);
      await page.reload();
      await expectUsersListLoaded(page, totalUsers);
      await expect(usersGrid(page)).toHaveAttribute("data-list-loaded-count", String(totalUsers));

      await expectNoPolicyViolations(page);
      await page.goto(blazorPath("user/profile"));
      await expectBlazorUrl(page, "user/profile");
      await page.goBack();

      await expectBlazorUrl(page, usersRoute);
      await expectUsersListLoaded(page, totalUsers);
      await expect(usersGrid(page)).toHaveAttribute("data-list-loaded-count", String(totalUsers));
      await expect(page).toHaveURL((url) => url.searchParams.get("pageOffset") === "1");
    })();

    await step("Resize to desktop and back to the phone & verify paging on desktop keeps the page and the phone range returns")(async () => {
      await page.setViewportSize(desktopViewport);

      await expect(usersGrid(page)).toHaveAttribute("data-list-load-mode", "pages");
      await expect(usersGrid(page)).toHaveAttribute("data-list-page-offset", "1");
      await expect(userRows(page)).toHaveCount(totalUsers - 25);
      await expect(usersGrid(page).getByRole("columnheader", { name: texts.emailColumn })).toBeVisible();

      await page.setViewportSize(phoneViewport);
      await expect(usersGrid(page)).toHaveAttribute("data-list-load-mode", "infinite");
      await expect(usersGrid(page)).toHaveAttribute("data-list-loaded-count", String(totalUsers));
      await expect(usersGrid(page).getByRole("columnheader", { name: texts.emailColumn })).toBeHidden();
    })();

    // === SUPERSEDED SEARCH ===
    await step("Search while the answer to an earlier search is delayed & verify the settled earlier request never replaces the newer rows")(async () => {
      await page.route(
        (url) => url.pathname === "/api/account/users" && url.searchParams.get("Search") === firstEmail,
        async (route) => {
          await new Promise((resolve) => setTimeout(resolve, delayedSearchMilliseconds));
          await route.continue();
        }
      );
      const settledSearches: (string | null)[] = [];
      page.on("requestfinished", (request) => settledSearches.push(new URL(request.url()).searchParams.get("Search")));
      page.on("requestfailed", (request) => settledSearches.push(new URL(request.url()).searchParams.get("Search")));

      await page.getByRole("textbox", { name: texts.search }).fill(firstEmail);
      await expect(page).toHaveURL((url) => url.searchParams.get("search") === firstEmail);
      await searchUsers(page, secondEmail, 1);
      await expect.poll(() => settledSearches.includes(firstEmail), { timeout: delayedSearchMilliseconds * 5 }).toBe(true);
      await expectNoPolicyViolations(page);

      await expect(phoneUserRow(page, secondEmail)).toBeVisible();
      await expect(userRows(page)).toHaveCount(1);
      await expectUsersListLoaded(page, 1);
      await page.unrouteAll({ behavior: "wait" });
    })();

    // === SMALL PHONE ===
    await step("Tap a row and then move and press Enter on a small phone & verify a single selection follows each input")(async () => {
      await page.setViewportSize(smallPhoneViewport);
      await gotoUsersPage(page, `?search=phone-`);
      await expectUsersListLoaded(page, invitedEmails.length);

      await phoneUserRow(page, firstEmail).getByRole("cell").first().tap();
      await expect(profilePane(page).getByTestId("profile-email")).toHaveText(firstEmail);
      await expect(usersGrid(page)).toHaveAttribute("data-list-selected-count", "1");
      await page.keyboard.press("Escape");
      await expect(profilePane(page)).toBeHidden();

      await phoneUserRow(page, firstEmail).focus();
      await page.keyboard.press("ArrowDown");
      await page.keyboard.press("Enter");

      await expect(profilePane(page).getByTestId("profile-email")).toHaveText(secondEmail);
      await expect(usersGrid(page)).toHaveAttribute("data-list-selected-count", "1");
      await expectNoPolicyViolations(page);
    })();

    // === ANOTHER IDENTITY ===
    await step("Log out and sign up another account in the same browser & verify none of the previous account's rows remain")(async () => {
      await page.setViewportSize(desktopViewport);
      await logOutThroughBlazor(page);
      await signUpThroughBlazor(page, uniqueBlazorEmail());
      await page.setViewportSize(phoneViewport);

      await gotoUsersPage(page, `?pageOffset=1&search=phone-`);

      await expectUsersListLoaded(page, 0);
      await expectNetworkErrors(context, [400]);
      await expect(phoneUserRow(page, firstEmail)).toHaveCount(0);
      await expect(usersGrid(page)).toHaveAttribute("data-list-loaded-count", "0");
      await expectNoPolicyViolations(page);
    })();
  });
});
