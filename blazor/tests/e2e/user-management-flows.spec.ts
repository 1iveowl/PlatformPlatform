import { expect, type Page } from "@playwright/test";
import {
  changeUserRoleThroughAccountApi,
  expectAccountApiProblem,
  findUserThroughAccountApi,
  inviteUsersThroughAccountApi,
  sendAccountApiRequest
} from "@blazor/e2e/account-api";
import { logInInvitedUserThroughBlazor, signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorUrl } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { accountApiMessages, blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { blazorToast } from "@blazor/e2e/toast";
import {
  expectUsersListLoaded,
  gotoUsersPage,
  openUserActions,
  profilePane,
  searchUsers,
  sortButton,
  userRow,
  userRows,
  usersGrid
} from "@blazor/e2e/users";
import { assertNoUnexpectedErrors, createTestContext, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * The users seeded through the account API: two that become the admin and the member, one that is deleted while its role
 * dialog is open, and enough others for a second page of 25 rows
 */
const paddingUserCount = 22;

/**
 * The id attribute of the search box, the stable control that takes focus when a closed pane's row is not on the page
 */
const searchBoxId = "users-search";

async function expectFocusOnSearchBox(page: Page): Promise<void> {
  await expect.poll(() => page.evaluate(() => document.activeElement?.id)).toBe(searchBoxId);
}

test.describe("@smoke", () => {
  /**
   * The users page on the shared list foundation, mirroring the React role-management steps of the user management smoke
   * test. Users are created through the account API's invite endpoint with the owner's session and antiforgery token,
   * because the Blazor edition has no invite dialog yet.
   * - Paging, all five sort columns, Back and Forward, with the list state only in the URL
   * - Search by email that narrows the list and survives reload and Back, and an empty search result
   * - Role, status and date filters in a deep link that are applied, and malformed values that are ignored
   * - The change role dialog: Escape with a selection shows "Unsaved changes", Stay keeps it, Leave closes it
   * - A role change from the side pane that updates the pane, drops the row from a role filter it left, invalidates a
   *   cached sorted variant and shows the toast; closing the pane of a row no longer on the page focuses the search box
   * - A role change rejected by the account API because the user was deleted, shown in the dialog as returned
   * - The owner's own row with the role action disabled and its reason in the pane
   * - The side pane from a row click, from Enter with Escape returning focus to the row, from a deep link, for a user not
   *   in the current view and for a deleted user, and closed by selecting a second row
   * - Admin and member sessions that see the list and the pane with the role action disabled and its reason
   * - The account API rejecting role changes by an admin, a member, an owner on their own role and another account's owner,
   *   with no role changed and no user of the account readable from the other account
   * - No securitypolicyviolation event and no style attribute on any users page document
   */
  test("should handle role management, side pane, search and permissions on the users page", async ({ page, browser }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const ownerEmail = uniqueBlazorEmail();
    const adminEmail = `admin-${uniqueBlazorEmail()}`;
    const memberEmail = `member-${uniqueBlazorEmail()}`;
    const deletedEmail = `deleted-${uniqueBlazorEmail()}`;
    const paddingEmails = Array.from({ length: paddingUserCount }, (_, index) => `padding-${index}-${uniqueBlazorEmail()}`);
    const totalUsers = paddingUserCount + 4;
    let deletedUserId = "";

    // === SEEDING ===
    await step("Sign up an owner and invite users through the account API & verify the users page lists every user")(async () => {
      await signUpThroughBlazor(page, ownerEmail);
      await inviteUsersThroughAccountApi(page, [adminEmail, memberEmail, deletedEmail, ...paddingEmails]);

      await gotoUsersPage(page);

      await expect(page.getByRole("heading", { name: texts.users, exact: true })).toBeVisible();
      await expectUsersListLoaded(page, totalUsers);
    })();

    // === PAGING AND SORTING ===
    await step("Go to the next page, back and forward & verify the page offset follows history")(async () => {
      await page.getByRole("button", { name: texts.nextPage, exact: true }).click();
      await expect(page).toHaveURL((url) => url.searchParams.get("pageOffset") === "1");
      await expect(usersGrid(page)).toHaveAttribute("data-list-page-offset", "1");
      await expect(userRows(page)).toHaveCount(1);

      await page.goBack();
      await expect(usersGrid(page)).toHaveAttribute("data-list-page-offset", "0");
      await expect(userRows(page)).toHaveCount(25);

      await page.goForward();
      await expect(usersGrid(page)).toHaveAttribute("data-list-page-offset", "1");
      await page.goBack();
      await expect(usersGrid(page)).toHaveAttribute("data-list-page-offset", "0");
    })();

    await step("Sort by each column & verify the sort reaches the URL and the list reloads")(async () => {
      for (const [sortKey, columnTitle] of [
        ["Email", texts.emailColumn],
        ["CreatedAt", texts.createdColumn],
        ["LastSeenAt", texts.lastSeenColumn],
        ["Role", texts.roleColumn]
      ]) {
        await sortButton(page, columnTitle).click();

        await expect(page).toHaveURL((url) => url.searchParams.get("orderBy") === sortKey && !url.searchParams.has("sortOrder"));
        await expectUsersListLoaded(page, totalUsers);
      }

      await sortButton(page, texts.roleColumn).click();
      await expect(page).toHaveURL((url) => url.searchParams.get("orderBy") === "Role" && url.searchParams.get("sortOrder") === "Descending");

      await sortButton(page, texts.nameColumn).click();
      await expect(page).toHaveURL((url) => !url.searchParams.has("orderBy") && !url.searchParams.has("sortOrder"));
      await sortButton(page, texts.nameColumn).click();
      await expect(page).toHaveURL((url) => !url.searchParams.has("orderBy") && url.searchParams.get("sortOrder") === "Descending");
      await expectUsersListLoaded(page, totalUsers);

      await page.goBack();
      await expect(page).toHaveURL((url) => !url.searchParams.has("orderBy") && !url.searchParams.has("sortOrder"));
    })();

    // === SEARCH ===
    await step("Search for an email, reload and go back & verify the search narrows the list and survives both")(async () => {
      await searchUsers(page, memberEmail, 1);
      await expect(userRow(page, memberEmail)).toBeVisible();

      await expectNoPolicyViolations(page);
      await page.reload();
      await expectUsersListLoaded(page, 1);
      await expect(userRow(page, memberEmail)).toBeVisible();

      await sortButton(page, texts.emailColumn).click();
      await expect(page).toHaveURL((url) => url.searchParams.get("orderBy") === "Email");
      await page.goBack();
      await expect(page).toHaveURL((url) => url.searchParams.get("search") === memberEmail && !url.searchParams.has("orderBy"));
      await expectUsersListLoaded(page, 1);
    })();

    await step("Search for an email nobody has & verify the empty state")(async () => {
      await searchUsers(page, `nobody-${uniqueBlazorEmail()}`, 0);

      await expect(page.getByText(texts.noUsersFound, { exact: true })).toBeVisible();
    })();

    await step("Open a deep link with role, status and date filters and malformed values & verify valid filters apply")(async () => {
      await gotoUsersPage(page, `?userRole=Owner&userStatus=Active&startDate=2000-01-01&endDate=2999-12-31`);
      await expectUsersListLoaded(page, 1);
      await expect(userRow(page, ownerEmail)).toBeVisible();

      await gotoUsersPage(page, `?userRole=Chief&userStatus=Asleep&startDate=yesterday&pageOffset=abc&search=${encodeURIComponent(ownerEmail)}`);
      await expectUsersListLoaded(page, 1);
      await expect(userRow(page, ownerEmail)).toBeVisible();
    })();

    // === CHANGE ROLE DIALOG ===
    await step("Open Change Role dialog, select role & verify unsaved changes warning on Escape")(async () => {
      await gotoUsersPage(page, `?userRole=Member&search=${encodeURIComponent(adminEmail)}`);
      await expectUsersListLoaded(page, 1);

      const menu = await openUserActions(page, adminEmail);
      await menu.getByRole("menuitem", { name: texts.changeRole }).dispatchEvent("click");
      const dialog = page.getByRole("dialog", { name: texts.changeUserRole });
      await expect(dialog).toBeVisible();
      await dialog.getByRole("radio", { name: texts.owner, exact: true }).check();
      await page.keyboard.press("Escape");

      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeVisible();
      await page.getByRole("button", { name: texts.stay }).click();
      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeHidden();
      await expect(dialog).toBeVisible();
      await expect(dialog.getByRole("radio", { name: texts.owner, exact: true })).toBeChecked();

      await dialog.getByRole("radio", { name: texts.owner, exact: true }).focus();
      await page.keyboard.press("Escape");
      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeVisible();
      await page.getByRole("button", { name: texts.leave }).click();
      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeHidden();
      await expect(dialog).toBeHidden();
      await expect(userRow(page, adminEmail).getByRole("cell", { name: texts.member, exact: true })).toBeVisible();
    })();

    await step("Change a member to Admin from the side pane & verify the pane, the filtered list, a cached sort and the toast")(async () => {
      await sortButton(page, texts.emailColumn).click();
      await expect(page).toHaveURL((url) => url.searchParams.get("orderBy") === "Email");
      await page.goBack();
      await expect(page).toHaveURL((url) => !url.searchParams.has("orderBy"));
      await expectUsersListLoaded(page, 1);

      await userRow(page, adminEmail).getByText(adminEmail, { exact: true }).first().click();
      const pane = profilePane(page);
      await expect(pane.getByTestId("profile-email")).toHaveText(adminEmail);
      await pane.getByRole("button", { name: `${texts.changeUserRoleFor}${adminEmail}` }).click();
      const dialog = page.getByRole("dialog", { name: texts.changeUserRole });
      await dialog.getByRole("radio", { name: texts.admin, exact: true }).check();
      await dialog.getByRole("button", { name: texts.saveChanges }).click();

      await expect(dialog).toBeHidden();
      await expect(blazorToast(page, `${texts.userRoleUpdatedFor}${adminEmail}`)).toBeVisible();
      await expect(pane.getByRole("button", { name: `${texts.changeUserRoleFor}${adminEmail}` })).toHaveText(texts.admin);
      await expectUsersListLoaded(page, 0);
      await expect(pane.getByText(texts.userNotInCurrentView, { exact: true })).toBeVisible();

      await page.goForward();
      await expect(page).toHaveURL((url) => url.searchParams.get("orderBy") === "Email");
      await expectUsersListLoaded(page, 0);
      await page.goBack();
      await expectUsersListLoaded(page, 0);
    })();

    await step("Close the pane of a user no longer on the page & verify focus moves to the search box")(async () => {
      await page.getByRole("button", { name: texts.closeUserProfile }).click();

      await expect(profilePane(page)).toBeHidden();
      await expect(page).toHaveURL((url) => !url.searchParams.has("userId"));
      await expectFocusOnSearchBox(page);
    })();

    await step("Save a role change for a user deleted while the dialog is open & verify the API message in the dialog")(async () => {
      await gotoUsersPage(page, `?search=${encodeURIComponent(deletedEmail)}`);
      await expectUsersListLoaded(page, 1);
      const deletedUser = await findUserThroughAccountApi(page, deletedEmail);
      deletedUserId = deletedUser.id;
      const menu = await openUserActions(page, deletedEmail);
      await menu.getByRole("menuitem", { name: texts.changeRole }).dispatchEvent("click");
      const dialog = page.getByRole("dialog", { name: texts.changeUserRole });
      await dialog.getByRole("radio", { name: texts.admin, exact: true }).check();
      const deletion = await sendAccountApiRequest(page, "DELETE", `/api/account/users/${deletedUser.id}`);
      expect(deletion.status).toBe(200);

      await dialog.getByRole("button", { name: texts.saveChanges }).click();

      await expect(dialog.getByRole("alert").getByText(accountApiMessages.userNotFound(deletedUser.id), { exact: true })).toBeVisible();
      await expectNetworkErrors(context, [404]);
      await dialog.getByRole("button", { name: texts.cancel }).click();
      await page.getByRole("button", { name: texts.leave }).click();
      await expect(dialog).toBeHidden();
    })();

    await step("Open a deep link to the deleted user & verify the localized not found state shows nothing about the user")(async () => {
      await gotoUsersPage(page, `?userId=${deletedUserId}`);

      const pane = profilePane(page);
      await expect(pane.getByText(texts.userNotFound, { exact: true })).toBeVisible();
      await expect(pane.getByTestId("profile-email")).toHaveCount(0);
      await expect(page.getByText(deletedEmail)).toHaveCount(0);
      await expectNetworkErrors(context, [404]);
    })();

    // === OWNER'S OWN ROW ===
    await step("Open owner's actions menu & verify role change is disabled with its reason in the pane")(async () => {
      await searchUsers(page, ownerEmail, 1);

      const menu = await openUserActions(page, ownerEmail);
      await expect(menu.getByRole("menuitem", { name: texts.changeRole })).toBeDisabled();
      await menu.getByRole("menuitem", { name: texts.viewProfile }).dispatchEvent("click");

      const pane = profilePane(page);
      await expect(pane.getByTestId("profile-email")).toHaveText(ownerEmail);
      const roleButton = pane.getByRole("button", { name: texts.owner, exact: true });
      await expect(roleButton).toBeDisabled();
      await expect(roleButton).toHaveAccessibleDescription(texts.cannotChangeOwnRole);
    })();

    // === SIDE PANE ===
    await step("Open a deep link to a user outside the current view & verify the pane and its notice")(async () => {
      const member = await findUserThroughAccountApi(page, memberEmail);

      await gotoUsersPage(page, `?search=${encodeURIComponent(ownerEmail)}&userId=${member.id}`);

      const pane = profilePane(page);
      await expect(pane.getByRole("heading", { name: texts.userProfile })).toBeVisible();
      await expect(pane.getByTestId("profile-email")).toHaveText(memberEmail);
      await expect(pane.getByTestId("profile-id")).toHaveText(member.id);
      await expect(pane.getByRole("button", { name: `${texts.changeUserRoleFor}${memberEmail}` })).toBeEnabled();
      await expect(pane.getByText(texts.userNotInCurrentView, { exact: true })).toBeVisible();
    })();

    await step("Open the pane with Enter, close it with Escape and select a second row & verify focus and pane closing")(async () => {
      await searchUsers(page, "padding-", paddingUserCount);
      const firstRow = userRows(page).nth(0);
      const secondRow = userRows(page).nth(1);

      await firstRow.focus();
      await page.keyboard.press("Enter");
      await expect(profilePane(page)).toBeVisible();
      await expect(page).toHaveURL((url) => url.searchParams.has("userId"));
      await page.keyboard.press("Escape");
      await expect(profilePane(page)).toBeHidden();
      await expect(firstRow).toBeFocused();

      await firstRow.click();
      await expect(profilePane(page)).toBeVisible();
      await secondRow.click({ modifiers: ["ControlOrMeta"] });
      await expect(profilePane(page)).toBeHidden();
      await expect(usersGrid(page)).toHaveAttribute("data-list-selected-count", "2");
      await expectNoPolicyViolations(page);
    })();

    // === ADMIN AND MEMBER ===
    const adminContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const adminPage = await adminContext.newPage();
    const adminTestContext = createTestContext(adminPage);
    await trackPolicyViolations(adminPage);
    const memberContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const memberPage = await memberContext.newPage();
    const memberTestContext = createTestContext(memberPage);
    await trackPolicyViolations(memberPage);

    await step("Log in as the admin and open another user's pane & verify the role action is disabled for a non-owner")(async () => {
      await logInInvitedUserThroughBlazor(adminPage, adminEmail, { firstName: "Ada", lastName: "Admin" });
      await gotoUsersPage(adminPage, `?search=${encodeURIComponent(memberEmail)}`);
      await expectUsersListLoaded(adminPage, 1);

      const menu = await openUserActions(adminPage, memberEmail);
      await expect(menu.getByRole("menuitem", { name: texts.changeRole })).toHaveCount(0);
      await menu.getByRole("menuitem", { name: texts.viewProfile }).dispatchEvent("click");

      const pane = profilePane(adminPage);
      const roleButton = pane.getByRole("button", { name: texts.member, exact: true });
      await expect(roleButton).toBeDisabled();
      await expect(roleButton).toHaveAccessibleDescription(texts.onlyOwnersCanChangeRoles);
      await expectNoPolicyViolations(adminPage);
    })();

    await step("Log in as the member and open the admin's pane & verify the role action is disabled for a non-owner")(async () => {
      await logInInvitedUserThroughBlazor(memberPage, memberEmail, { firstName: "Mel", lastName: "Member" });
      await gotoUsersPage(memberPage, `?search=${encodeURIComponent(adminEmail)}`);
      await expectUsersListLoaded(memberPage, 1);

      await userRow(memberPage, adminEmail).getByText("Ada Admin", { exact: true }).click();

      const pane = profilePane(memberPage);
      await expect(pane.getByTestId("profile-name")).toHaveText("Ada Admin");
      const roleButton = pane.getByRole("button", { name: texts.admin, exact: true });
      await expect(roleButton).toBeDisabled();
      await expect(roleButton).toHaveAccessibleDescription(texts.onlyOwnersCanChangeRoles);
      await expectNoPolicyViolations(memberPage);
    })();

    // === AUTHORIZATION ===
    await step("Change roles through the account API as admin, member and owner on their own role & verify each is rejected")(async () => {
      const admin = await findUserThroughAccountApi(page, adminEmail);
      const member = await findUserThroughAccountApi(page, memberEmail);
      const owner = await findUserThroughAccountApi(page, ownerEmail);

      expectAccountApiProblem(await changeUserRoleThroughAccountApi(adminPage, member.id, "Owner"), 403, accountApiMessages.onlyOwnersCanChangeUserRoles);
      expectAccountApiProblem(await changeUserRoleThroughAccountApi(memberPage, admin.id, "Member"), 403, accountApiMessages.onlyOwnersCanChangeUserRoles);
      expectAccountApiProblem(await changeUserRoleThroughAccountApi(memberPage, member.id, "Owner"), 403, accountApiMessages.cannotChangeOwnUserRole);
      expectAccountApiProblem(await changeUserRoleThroughAccountApi(page, owner.id, "Member"), 403, accountApiMessages.cannotChangeOwnUserRole);

      await expectNetworkErrors(adminTestContext, [403]);
      await expectNetworkErrors(memberTestContext, [403]);
      await expectNetworkErrors(context, [403]);

      expect((await findUserThroughAccountApi(page, adminEmail)).role).toBe("Admin");
      expect((await findUserThroughAccountApi(page, memberEmail)).role).toBe("Member");
      expect((await findUserThroughAccountApi(page, ownerEmail)).role).toBe("Owner");
    })();

    const otherContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const otherPage = await otherContext.newPage();
    const otherTestContext = createTestContext(otherPage);
    await trackPolicyViolations(otherPage);

    await step("Change a role and read a user through the account API as another account's owner & verify nothing leaks")(async () => {
      const member = await findUserThroughAccountApi(page, memberEmail);
      await signUpThroughBlazor(otherPage, uniqueBlazorEmail());

      expectAccountApiProblem(await changeUserRoleThroughAccountApi(otherPage, member.id, "Owner"), 404, accountApiMessages.userNotFound(member.id));
      const lookup = await sendAccountApiRequest(otherPage, "GET", `/api/account/users/${member.id}`);
      expect(lookup.status).toBe(404);
      expect(lookup.body).not.toContain(memberEmail);
      const search = await sendAccountApiRequest(otherPage, "GET", `/api/account/users?Search=${encodeURIComponent(memberEmail)}`);
      expect((JSON.parse(search.body) as { totalCount: number }).totalCount).toBe(0);
      await expectNetworkErrors(otherTestContext, [404]);

      await gotoUsersPage(otherPage, `?userId=${member.id}`);
      await expect(profilePane(otherPage).getByText(texts.userNotFound, { exact: true })).toBeVisible();
      await expect(otherPage.getByText(memberEmail)).toHaveCount(0);
      await expectNetworkErrors(otherTestContext, [404]);

      expect((await findUserThroughAccountApi(page, memberEmail)).role).toBe("Member");
    })();

    await expectNoPolicyViolations(page);
    await assertNoUnexpectedErrors(adminTestContext);
    await assertNoUnexpectedErrors(memberTestContext);
    await expectNoPolicyViolations(otherPage);
    await assertNoUnexpectedErrors(otherTestContext);
    await otherContext.close();
    await memberContext.close();
    await adminContext.close();
  });
});
