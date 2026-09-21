import { expect } from "@playwright/test";
import { inviteUsersThroughAccountApi } from "@blazor/e2e/account-api";
import { logOutThroughBlazor, signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import {
  closeMobileMenuByTouch,
  desktopViewport,
  expectPhoneChrome,
  longPressByTouch,
  mobileMenuLanguageButton,
  mobileMenuLink,
  mobileMenuSupportLink,
  mobileMenuThemeButton,
  openMobileMenuByTouch,
  phoneViewport,
  releaseTouchPress,
  resizeTo,
  smallPhoneViewport,
  tapMobileMenuLink
} from "@blazor/e2e/mobile";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, expectBlazorUrl } from "@blazor/e2e/routes";
import { accountNameInput, gotoAccountSettingsPage, saveAccountSettingsButton } from "@blazor/e2e/settings";
import {
  closeMobileMenuWithEscape,
  expectAppliedTheme,
  mobileMenuButton,
  mobileMenuDialog,
  openMobileMenuByKeyboard
} from "@blazor/e2e/shell";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts } from "@blazor/e2e/texts";
import { blazorToast, dismissBlazorToast } from "@blazor/e2e/toast";
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
import { expectBlazorValidationMessage } from "@blazor/e2e/validation";
import { createTestContext, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * The users invited on top of the three invited through the dialog, so the list has a second server page of 25 rows
 */
const paddingUserCount = 27;

/**
 * How long the account API's answer to a superseded search is held back, so it arrives after the answer to the search that
 * replaced it
 */
const delayedSearchMilliseconds = 2_000;

test.describe("@smoke", () => {
  test.use({ hasTouch: true });

  /**
   * The phone's way into the shell and into a user's profile, at 390 by 844
   * - The sidebar, its main navigation and the user menu, which carries the theme and the language on wider screens, are
   *   replaced by the floating menu button, which opens the menu by touch and closes it by touch and with the keyboard
   * - Dark chosen in the menu by touch is applied and shown as pressed with the menu still open, and the running culture's
   *   language is pressed in the same menu
   * - Users opened from the menu by touch shows the phone list; a tap on a row opens the profile as a full-screen modal
   *   dialog named User profile, whose Tab never leaves it for the list behind it
   * - The role dialog opened over the pane owns the focus trap, and Escape closes it and leaves the pane open
   * - Crossing to desktop docks the same pane as a region and crossing back makes it full-screen again
   * - Escape closes a pane opened from a row with the keyboard and gives the row its focus back
   * - Home opened from the menu navigates, closes the menu and keeps the chosen theme
   * - No securitypolicyviolation event and no style attribute on any document
   */
  test("should open the mobile menu by touch and keyboard, change the theme and show the profile pane full screen", async ({ page }) => {
    createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const ownerEmail = uniqueBlazorEmail();
    const memberEmail = `member-${uniqueBlazorEmail()}`;

    // === MOBILE MENU ===
    await step("Sign up, resize to a phone and open the navigation menu by touch & verify it replaces the sidebar and the user menu")(async () => {
      await resizeTo(page, desktopViewport);
      await signUpThroughBlazor(page, ownerEmail);
      await resizeTo(page, phoneViewport);

      await expectPhoneChrome(page);
      await openMobileMenuByTouch(page);

      await expect(mobileMenuLink(page, texts.home)).toBeVisible();
      await expect(mobileMenuDialog(page).getByRole("button", { name: texts.logOut, exact: true })).toBeVisible();
      await expect(mobileMenuDialog(page).getByText(ownerEmail, { exact: true })).toBeVisible();
    })();

    await step("Close the navigation menu with its Close menu button by touch & verify it closes")(async () => {
      await closeMobileMenuByTouch(page);

      await expect(mobileMenuDialog(page)).toBeHidden();
      await expectNoPolicyViolations(page);
    })();

    await step("Open the menu with the keyboard, choose Dark by touch and press Escape & verify the theme and the returned focus")(async () => {
      await openMobileMenuByKeyboard(page);
      await mobileMenuThemeButton(page, "dark").tap();

      await expectAppliedTheme(page, "dark");
      await expect(mobileMenuThemeButton(page, "dark")).toHaveAttribute("aria-pressed", "true");
      await expect(mobileMenuLanguageButton(page, texts.languageName)).toHaveAttribute("aria-pressed", "true");
      await expect(mobileMenuDialog(page)).toBeVisible();

      await closeMobileMenuWithEscape(page);
    })();

    // === USERS FROM THE MENU ===
    await step("Invite a user and open Users from the menu by touch & verify the phone list")(async () => {
      await inviteUsersThroughAccountApi(page, [memberEmail]);
      await openMobileMenuByTouch(page);

      await tapMobileMenuLink(page, texts.users);

      await expectBlazorUrl(page, usersRoute);
      await expectUsersListLoaded(page, 2);
      await expect(usersGrid(page).getByRole("columnheader", { name: texts.emailColumn })).toBeHidden();
    })();

    // === FULL-SCREEN PANE ===
    await step("Tap the invited user's row & verify the profile opens as a full-screen modal dialog")(async () => {
      await phoneUserRow(page, memberEmail).getByRole("cell").first().tap();

      const pane = page.getByRole("dialog", { name: texts.userProfile, exact: true });
      await expect(pane).toBeVisible();
      await expect(pane).toHaveAttribute("aria-modal", "true");
      await expect(pane).toHaveAttribute("data-side-pane-mode", "fullscreen");
      await expect(pane.getByTestId("profile-email")).toHaveText(memberEmail);
    })();

    await step("Move through the pane with Tab & verify focus stays inside the modal and never reaches the list")(async () => {
      const pane = profilePane(page);
      await pane.getByRole("button", { name: texts.closeUserProfile, exact: true }).focus();

      await page.keyboard.press("Tab");
      await expect(pane.getByRole("button", { name: `${texts.changeUserRoleFor}${memberEmail}`, exact: true })).toBeFocused();
      await page.keyboard.press("Tab");

      // Past its last control the trap wraps to the pane's first, so focus never reaches the list behind the modal and
      // never stops on the backdrop, which carries tabindex="-1" because it exists for the pointer alone
      await expect(pane.getByRole("button", { name: texts.closeUserProfile, exact: true })).toBeFocused();
      await expect(pane.getByRole("button", { name: texts.closeSidePanel, exact: true })).not.toBeFocused();
      await expect(phoneUserRow(page, memberEmail)).not.toBeFocused();
    })();

    await step("Open the role dialog over the pane and press Escape & verify the dialog closes and the pane stays open")(async () => {
      const pane = profilePane(page);
      await pane.getByRole("button", { name: `${texts.changeUserRoleFor}${memberEmail}`, exact: true }).tap();

      const roleDialog = page.getByRole("dialog", { name: texts.changeUserRole, exact: true });
      await expect(roleDialog).toBeVisible();
      await expect(roleDialog.getByRole("radio", { name: texts.member, exact: true })).toBeChecked();
      await page.keyboard.press("Escape");

      await expect(roleDialog).toBeHidden();
      await expect(pane).toBeVisible();
      await expect(pane.getByTestId("profile-email")).toHaveText(memberEmail);
    })();

    await step("Resize to desktop and back to the phone & verify the pane docks as a region and fills the screen again")(async () => {
      await resizeTo(page, desktopViewport);

      const dockedPane = page.getByRole("region", { name: texts.userProfile, exact: true });
      await expect(dockedPane).toBeVisible();
      await expect(dockedPane).toHaveAttribute("data-side-pane-mode", "docked");
      await expect(dockedPane).not.toHaveAttribute("aria-modal", "true");

      await resizeTo(page, phoneViewport);

      const fullScreenPane = page.getByRole("dialog", { name: texts.userProfile, exact: true });
      await expect(fullScreenPane).toHaveAttribute("data-side-pane-mode", "fullscreen");
      await expect(fullScreenPane.getByTestId("profile-email")).toHaveText(memberEmail);
    })();

    await step("Close the pane by touch, open it from the row with Enter and press Escape & verify the row keeps the focus")(async () => {
      await profilePane(page).getByRole("button", { name: texts.closeUserProfile, exact: true }).tap();
      await expect(profilePane(page)).toBeHidden();

      await phoneUserRow(page, memberEmail).focus();
      await page.keyboard.press("Enter");
      await expect(profilePane(page).getByTestId("profile-email")).toHaveText(memberEmail);
      await page.keyboard.press("Escape");

      await expect(profilePane(page)).toBeHidden();
      await expect(phoneUserRow(page, memberEmail)).toBeFocused();
    })();

    // === BACK TO THE WORKSPACE ===
    await step("Open Home from the menu by touch & verify the workspace with the menu closed and the theme kept")(async () => {
      await openMobileMenuByTouch(page);

      await tapMobileMenuLink(page, texts.home);

      await expectBlazorUrl(page, "app");
      await expect(mobileMenuButton(page)).toHaveAttribute("aria-expanded", "false");
      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);
    })();
  });
});

test.describe("@comprehensive", () => {
  test.use({ hasTouch: true });

  /**
   * The users, navigation and form parts of the React mobile view tests on phone viewports, with the phone list's loading,
   * the long-press row menu, the resilience of the loaded range, the mobile menu's own entries and the forms a phone submits
   * - At 390 by 844: the users page reached through the mobile menu, three users invited through the dialog, one visible data
   *   column with the email under the name, a tap opening the side pane, arrows moving without opening it, Enter opening it,
   *   and Escape and the close button closing it
   * - The row menu opened by a touch long-press, synthesized as touch pointer events because Playwright's touchscreen can
   *   only tap, without activating the row, by a right-click, and from the explicit User actions button with the keyboard
   * - The next page appended through the Load more button activated with the keyboard, with one request and the status
   *   announced, the loaded range restored after a reload and after Back from another document with an empty page cache, and
   *   kept when resizing to desktop paging and back
   * - The mobile menu carries the navigation, the user block with Log out, the theme, the language and, when the brand names
   *   an address, support; Profile opened from it saves a new title, and Users returns to the list
   * - A search superseded while its response is delayed never replaces the newer search's rows
   * - At 375 by 667: a tap and then the keyboard keep a single selection, the account name is saved, the invite dialog shows
   *   its field message on an empty submit and closes on Cancel, and the mobile menu closes with Escape
   * - Logging out and signing up another account shows none of the previous account's rows, even for a deep link to the
   *   previous account's second page, which the list recovers from through the API's out-of-range refusal
   * - No securitypolicyviolation event and no style attribute on any document
   */
  test("should handle the users list, the navigation menu and the forms on phones", async ({ page }) => {
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
      await resizeTo(page, desktopViewport);
      await signUpThroughBlazor(page, ownerEmail);
      await inviteUsersThroughAccountApi(page, paddingEmails);
      await resizeTo(page, phoneViewport);

      await openMobileMenuByTouch(page);
      await tapMobileMenuLink(page, texts.users);

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
    await step("Activate Load more with the keyboard & verify one request appends the second page, announced and in the URL")(async () => {
      const requestUrls: URL[] = [];
      page.on("request", (request) => requestUrls.push(new URL(request.url())));
      await usersGrid(page).getByRole("button", { name: texts.loadMore, exact: true }).focus();
      await page.keyboard.press("Enter");

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
      await longPressByTouch(pressedCell);

      const menu = page.getByRole("menu");
      await expect(menu).toBeVisible();
      await expect(menu.getByRole("menuitem", { name: texts.viewProfile, exact: true })).toBeVisible();
      await releaseTouchPress(pressedCell);
      await expect(profilePane(page)).toBeHidden();
      await page.keyboard.press("Escape");
      await expect(menu).toBeHidden();

      await phoneUserRow(page, secondEmail).getByRole("cell").first().click({ button: "right" });
      await expect(menu).toBeVisible();
      await page.keyboard.press("Escape");
      await expect(menu).toBeHidden();
      await expect(profilePane(page)).toBeHidden();
    })();

    await step("Open the row menu from the User actions button with the keyboard & verify the alternative to the long press")(async () => {
      await phoneUserRow(page, thirdEmail).getByRole("button", { name: texts.userActions, exact: true }).focus();
      await page.keyboard.press("Enter");

      const menu = page.getByRole("menu");
      await expect(menu).toBeVisible();
      await expect(menu.getByRole("menuitem", { name: texts.viewProfile, exact: true })).toBeVisible();
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
      await resizeTo(page, desktopViewport);

      await expect(usersGrid(page)).toHaveAttribute("data-list-load-mode", "pages");
      await expect(usersGrid(page)).toHaveAttribute("data-list-page-offset", "1");
      await expect(userRows(page)).toHaveCount(totalUsers - 25);
      await expect(usersGrid(page).getByRole("columnheader", { name: texts.emailColumn })).toBeVisible();

      await resizeTo(page, phoneViewport);
      await expect(usersGrid(page)).toHaveAttribute("data-list-load-mode", "infinite");
      await expect(usersGrid(page)).toHaveAttribute("data-list-loaded-count", String(totalUsers));
      await expect(usersGrid(page).getByRole("columnheader", { name: texts.emailColumn })).toBeHidden();
    })();

    // === MOBILE NAVIGATION ===
    await step("Open the navigation menu on the users page & verify the navigation, the user block, the theme and the language")(async () => {
      await expectPhoneChrome(page);
      await openMobileMenuByTouch(page);

      for (const name of [texts.home, texts.profile, texts.preferences, texts.sessions, texts.settings, texts.users]) {
        await expect(mobileMenuLink(page, name)).toBeVisible();
      }
      await expect(mobileMenuDialog(page).getByText(ownerEmail, { exact: true })).toBeVisible();
      await expect(mobileMenuDialog(page).getByRole("button", { name: texts.logOut, exact: true })).toBeVisible();
      await expect(mobileMenuThemeButton(page, "system")).toHaveAttribute("aria-pressed", "true");
      await expect(mobileMenuLanguageButton(page, texts.languageName)).toHaveAttribute("aria-pressed", "true");

      // application/platform-settings.jsonc leaves supportEmail empty, so the menu renders no way to write to support
      await expect(mobileMenuSupportLink(page)).toHaveCount(0);
    })();

    await step("Open Profile from the menu and save a new title & verify the profile form and the toast")(async () => {
      await tapMobileMenuLink(page, texts.profile);

      await expectBlazorUrl(page, "user/profile");
      await expect(page.getByRole("heading", { name: texts.profile, exact: true })).toBeVisible();
      await expect(page.getByLabel(texts.firstName, { exact: true })).toBeVisible();
      await expect(page.getByLabel(texts.lastName, { exact: true })).toBeVisible();
      await expect(page.getByText(ownerEmail, { exact: true })).toBeVisible();

      await page.getByLabel(texts.title, { exact: true }).fill("Phone owner");
      await page.getByRole("button", { name: texts.saveChanges, exact: true }).tap();

      await dismissBlazorToast(page, blazorToast(page, texts.profileUpdated));
      await expect(page.getByLabel(texts.title, { exact: true })).toHaveValue("Phone owner");
    })();

    await step("Open Users from the menu and close the menu with Close menu & verify the list and the closed menu")(async () => {
      await openMobileMenuByTouch(page);
      await tapMobileMenuLink(page, texts.users);

      await expectBlazorUrl(page, usersRoute);
      await expectUsersListLoaded(page, totalUsers);

      await openMobileMenuByTouch(page);
      await closeMobileMenuByTouch(page);

      await expect(mobileMenuDialog(page)).toBeHidden();
      await expectNoPolicyViolations(page);
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
      await resizeTo(page, smallPhoneViewport);
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

    // === SMALL PHONE FORMS ===
    await step("Save a new account name on a small phone & verify the toast")(async () => {
      await gotoAccountSettingsPage(page);

      await accountNameInput(page).fill("Phone account");
      await saveAccountSettingsButton(page).tap();

      await dismissBlazorToast(page, blazorToast(page, texts.accountSettingsUpdated));
      await expect(accountNameInput(page)).toHaveValue("Phone account");
    })();

    await step("Submit the invite dialog empty and cancel it on a small phone & verify the field message and the closed dialog")(async () => {
      await gotoUsersPage(page);
      await page.getByRole("button", { name: texts.inviteUser, exact: true }).tap();

      const dialog = page.getByRole("dialog", { name: texts.inviteUser, exact: true });
      await expect(dialog).toBeVisible();
      await dialog.getByRole("button", { name: texts.sendInvite, exact: true }).tap();
      await expectBlazorValidationMessage(page, texts.emailAddressRequired);

      await dialog.getByRole("button", { name: texts.cancel, exact: true }).tap();
      await expect(dialog).toBeHidden();
    })();

    await step("Open the navigation menu with the keyboard on a small phone and press Escape & verify it closes and focus returns")(async () => {
      await openMobileMenuByKeyboard(page);

      await closeMobileMenuWithEscape(page);

      await expect(mobileMenuButton(page)).toHaveAttribute("aria-expanded", "false");
      await expectNoPolicyViolations(page);
    })();

    // === ANOTHER IDENTITY ===
    await step("Log out and sign up another account in the same browser & verify none of the previous account's rows remain")(async () => {
      await resizeTo(page, desktopViewport);
      await logOutThroughBlazor(page);
      await signUpThroughBlazor(page, uniqueBlazorEmail());
      await resizeTo(page, phoneViewport);

      await gotoUsersPage(page, `?pageOffset=1&search=phone-`);

      await expectUsersListLoaded(page, 0);
      await expectNetworkErrors(context, [400]);
      await expect(phoneUserRow(page, firstEmail)).toHaveCount(0);
      await expect(usersGrid(page)).toHaveAttribute("data-list-loaded-count", "0");
      await expectNoPolicyViolations(page);
    })();
  });
});
