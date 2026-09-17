import { expect, type Page } from "@playwright/test";
import {
  bulkDeleteUsersThroughAccountApi,
  bulkPurgeUsersThroughAccountApi,
  changeUserRoleThroughAccountApi,
  deleteUserResponseThroughAccountApi,
  deleteUserThroughAccountApi,
  emptyRecycleBinThroughAccountApi,
  expectAccountApiProblem,
  expectAccountApiValidationProblem,
  findUserThroughAccountApi,
  getDeletedUsersResponseThroughAccountApi,
  getDeletedUsersThroughAccountApi,
  getUserEmailsThroughAccountApi,
  inviteUsersThroughAccountApi,
  inviteUserThroughAccountApi,
  purgeUserThroughAccountApi,
  refreshClaimsByUpdatingProfileThroughAccountApi,
  restoreUserThroughAccountApi,
  sendAccountApiRequest,
  unknownUserIds
} from "@blazor/e2e/account-api";
import { logInInvitedUserThroughBlazor, signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorUrl, expectBlazorUrl } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { accountApiMessages, blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { blazorToast } from "@blazor/e2e/toast";
import {
  deletedUserRow,
  expectDeletedUsersListLoaded,
  expectUsersListLoaded,
  gotoRecycleBinPage,
  gotoUsersPage,
  inviteUserThroughDialog,
  openUserActions,
  profilePane,
  recycleBinRoute,
  searchUsers,
  selectRowsWithModifier,
  sortButton,
  userRow,
  userRows,
  usersGrid,
  usersTabs
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
   * test, with the invitation and deletion steps. The users the list needs are created through the account API's invite
   * endpoint with the owner's session and antiforgery token; one user is invited through the dialog.
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
   * - The invite dialog: Escape with a typed email shows "Unsaved changes", an existing user's email is rejected by the account
   *   API with its message shown in the dialog, and a new invitation shows the toast and the user in the list
   * - Deleting one user from the row menu with the toast and the row count, the owner's own Delete disabled, and the deleted
   *   users listed in the recycle bin reached through its tab
   * - Admin and member sessions that see the list and the pane with the role action disabled and its reason; the admin sees
   *   the recycle bin tab, the member sees no tabs and no Invite button
   * - The account API rejecting role changes by an admin, a member, an owner on their own role and another account's owner,
   *   with no role changed and no user of the account readable from the other account
   * - No securitypolicyviolation event and no style attribute on any users page document
   */
  test("should handle invitation, deletion, role management, side pane, search and permissions on the users page", async ({ page, browser }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const ownerEmail = uniqueBlazorEmail();
    const adminEmail = `admin-${uniqueBlazorEmail()}`;
    const memberEmail = `member-${uniqueBlazorEmail()}`;
    const deletedEmail = `deleted-${uniqueBlazorEmail()}`;
    const invitedEmail = `invited-${uniqueBlazorEmail()}`;
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

    // === INVITE AND DELETE ===
    await step("Type an email in the invite dialog and press Escape & verify the unsaved changes warning and Leave closing it")(async () => {
      await gotoUsersPage(page);
      await expectUsersListLoaded(page, totalUsers - 1);

      await page.getByRole("button", { name: texts.inviteUser, exact: true }).click();
      const dialog = page.getByRole("dialog", { name: texts.inviteUser, exact: true });
      await dialog.getByRole("textbox", { name: texts.email }).fill(invitedEmail);
      await page.keyboard.press("Escape");

      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeVisible();
      await page.getByRole("button", { name: texts.leave }).click();
      await expect(dialog).toBeHidden();
      await expectUsersListLoaded(page, totalUsers - 1);
    })();

    await step("Invite an email that already has a user & verify the API message in the dialog and nothing invited")(async () => {
      await page.getByRole("button", { name: texts.inviteUser, exact: true }).click();
      const dialog = page.getByRole("dialog", { name: texts.inviteUser, exact: true });
      await expect(dialog.getByRole("textbox", { name: texts.email })).toHaveValue("");
      await dialog.getByRole("textbox", { name: texts.email }).fill(memberEmail);
      await dialog.getByRole("button", { name: texts.sendInvite, exact: true }).click();

      await expect(dialog.getByRole("alert").getByText(accountApiMessages.userAlreadyExists(memberEmail), { exact: true })).toBeVisible();
      await expectNetworkErrors(context, [400]);
      await dialog.getByRole("button", { name: texts.cancel, exact: true }).click();
      await page.getByRole("button", { name: texts.leave }).click();
      await expect(dialog).toBeHidden();
      await expectUsersListLoaded(page, totalUsers - 1);
    })();

    await step("Invite a new user through the dialog & verify the toast and the user in the list")(async () => {
      await inviteUserThroughDialog(page, invitedEmail);

      await expect(blazorToast(page, texts.userInvited)).toBeVisible();
      await expectUsersListLoaded(page, totalUsers);
      await searchUsers(page, invitedEmail, 1);
      await expect(userRow(page, invitedEmail)).toBeVisible();
    })();

    await step("Delete the invited user from its row menu & verify the toast, the row count and the owner's own delete disabled")(async () => {
      const menu = await openUserActions(page, invitedEmail);
      await menu.getByRole("menuitem", { name: texts.delete, exact: true }).dispatchEvent("click");
      const dialog = page.getByRole("alertdialog", { name: texts.deleteUser, exact: true });
      await expect(dialog).toBeVisible();
      await dialog.getByRole("button", { name: texts.delete, exact: true }).click();

      await expect(dialog).toBeHidden();
      await expect(blazorToast(page, texts.userDeleted(invitedEmail))).toBeVisible();
      await expectUsersListLoaded(page, 0);
      await searchUsers(page, ownerEmail, 1);
      const ownerMenu = await openUserActions(page, ownerEmail);
      await expect(ownerMenu.getByRole("menuitem", { name: texts.delete, exact: true })).toBeDisabled();
      await page.keyboard.press("Escape");
    })();

    await step("Open the recycle bin tab & verify the deleted users are listed there")(async () => {
      await expect(usersTabs(page).getByRole("link", { name: texts.allUsers, exact: true })).toHaveAttribute("aria-current", "page");
      await usersTabs(page).getByRole("link", { name: texts.recycleBin, exact: true }).click();

      await expectBlazorUrl(page, recycleBinRoute);
      await expect(page.getByRole("heading", { name: texts.userRecycleBin, exact: true })).toBeVisible();
      await expectDeletedUsersListLoaded(page, 2);
      await expect(deletedUserRow(page, invitedEmail)).toBeVisible();
      await expect(deletedUserRow(page, deletedEmail)).toBeVisible();
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
      await expect(usersTabs(adminPage).getByRole("link", { name: texts.recycleBin, exact: true })).toBeVisible();
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

      await expect(memberPage.getByRole("link", { name: texts.allUsers, exact: true })).toHaveCount(0);
      await expect(memberPage.getByRole("link", { name: texts.recycleBin, exact: true })).toHaveCount(0);
      await expect(memberPage.getByRole("button", { name: texts.inviteUser, exact: true })).toHaveCount(0);
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

test.describe("@comprehensive", () => {
  /**
   * The users filters, bulk deletion and the recycle bin, mirroring the React single and bulk user deletion test without its
   * dashboard steps, plus the authorization matrix of the users administration API across two accounts
   * - Role, status and modified-date filters inline on a wide page, collapsed behind the filter button with its count badge
   *   when the page is narrower than 54 rem, the filter dialog with Clear and OK, and Clear filters inline
   * - Bulk deletion of a Ctrl or Cmd selection with its toast
   * - The recycle bin: restore with its toast, permanent deletion of one user, of a selection and emptying the bin
   * - An admin sees restore and permanent delete for one user but not Empty recycle bin, and the account API refuses the
   *   admin's bulk purge, empty, invite and deletions while it restores and permanently deletes one user; the account API
   *   refuses every users administration call of a member
   * - Another account's user ids in single and mixed bulk requests are refused as not found, nothing of the other account is
   *   disclosed and no selected user of either account changes
   * - Deleting yourself alone or in a batch, an empty batch and the 100-user limit of bulk delete and bulk purge
   * - An admin demoted to member whose claims are refreshed: the stale Restore control shows the API refusal and nothing is
   *   restored, and the recycle bin then shows Access denied
   * - No securitypolicyviolation event and no style attribute on any document
   */
  test("should handle filters, bulk deletion, the recycle bin and users administration authorization", async ({ page, browser }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const ownerEmail = uniqueBlazorEmail();
    const adminEmail = `admin-${uniqueBlazorEmail()}`;
    const memberEmail = `member-${uniqueBlazorEmail()}`;
    const others = Array.from({ length: 8 }, (_, index) => `other-${index + 1}-${uniqueBlazorEmail()}`);
    const [other1, other2, other3, other4, other5, other6, other7, other8] = others;
    const idOf = async (email: string) => (await findUserThroughAccountApi(page, email)).id;
    const deletedIdOf = async (email: string) => (await getDeletedUsersThroughAccountApi(page)).find((user) => user.email === email)!.id;

    // === SEEDING ===
    await step("Sign up an owner, invite users and make one an admin through the account API & verify the users list")(async () => {
      await page.setViewportSize({ width: 1920, height: 1080 });
      await signUpThroughBlazor(page, ownerEmail);
      await inviteUsersThroughAccountApi(page, [adminEmail, memberEmail, ...others]);
      expect((await changeUserRoleThroughAccountApi(page, await idOf(adminEmail), "Admin")).status).toBe(200);

      await gotoUsersPage(page);

      await expectUsersListLoaded(page, 11);
    })();

    // === FILTERS ===
    await step("Show the filters and filter by role, status and modified date inline & verify the URL and the list")(async () => {
      await page.getByRole("button", { name: texts.showSearchFilters }).click();
      await page.getByRole("combobox", { name: texts.userRole, exact: true }).selectOption("Admin");
      await expect(page).toHaveURL((url) => url.searchParams.get("userRole") === "Admin");
      await expectUsersListLoaded(page, 1);

      await page.getByRole("combobox", { name: texts.userStatus, exact: true }).selectOption("Pending");
      await expect(page).toHaveURL((url) => url.searchParams.get("userStatus") === "Pending");
      await page.getByLabel(texts.startDate, { exact: true }).fill("2000-01-01");
      await page.getByLabel(texts.endDate, { exact: true }).fill("2999-12-31");

      await expect(page).toHaveURL((url) => url.searchParams.get("startDate") === "2000-01-01" && url.searchParams.get("endDate") === "2999-12-31");
      await expectUsersListLoaded(page, 1);
      await expect(userRow(page, adminEmail)).toBeVisible();
      await expect(page.getByRole("button", { name: texts.clearFilters, exact: true })).toBeVisible();
    })();

    await step("Resize below 54 rem and clear the filters in the dialog & verify the badge, the dialog and the full list")(async () => {
      await page.setViewportSize({ width: 800, height: 1000 });

      await expect(page.getByRole("combobox", { name: texts.userRole, exact: true })).toHaveCount(0);
      const filterButton = page.getByRole("button", { name: texts.showSearchFilters });
      await expect(filterButton).toContainText("3");
      await filterButton.click();
      const dialog = page.getByRole("dialog", { name: texts.filters, exact: true });
      await expect(dialog).toBeVisible();
      await expect(dialog.getByRole("combobox", { name: texts.userRole, exact: true })).toHaveValue("Admin");
      await dialog.getByRole("button", { name: texts.clear, exact: true }).click();

      await expect(dialog).toBeHidden();
      await expect(page).toHaveURL((url) => !url.searchParams.has("userRole") && !url.searchParams.has("userStatus") && !url.searchParams.has("startDate"));
      await expectUsersListLoaded(page, 11);
      await expect(filterButton).toHaveText(texts.showSearchFilters);
    })();

    await step("Filter by role in the dialog, press OK and resize to wide & verify the badge and the inline Clear filters")(async () => {
      await page.getByRole("button", { name: texts.showSearchFilters }).click();
      const dialog = page.getByRole("dialog", { name: texts.filters, exact: true });
      await dialog.getByRole("combobox", { name: texts.userRole, exact: true }).selectOption("Member");
      await expect(page).toHaveURL((url) => url.searchParams.get("userRole") === "Member");
      await dialog.getByRole("button", { name: texts.ok, exact: true }).click();

      await expect(dialog).toBeHidden();
      await expectUsersListLoaded(page, 9);
      await expect(page.getByRole("button", { name: texts.showSearchFilters })).toContainText("1");

      await page.setViewportSize({ width: 1920, height: 1080 });
      await expect(page.getByRole("combobox", { name: texts.userRole, exact: true })).toHaveValue("Member");
      await page.getByRole("button", { name: texts.clearFilters, exact: true }).click();
      await expect(page).toHaveURL((url) => !url.searchParams.has("userRole"));
      await expectUsersListLoaded(page, 11);
    })();

    // === BULK DELETE ===
    await step("Select three users with Ctrl or Cmd and delete them & verify the toast and the row count")(async () => {
      await selectRowsWithModifier(page, [userRow(page, other1), userRow(page, other2), userRow(page, other3)]);
      await page.getByRole("button", { name: texts.deleteCountUsers(3), exact: true }).click();
      const dialog = page.getByRole("alertdialog", { name: texts.deleteUsers, exact: true });
      await dialog.getByRole("button", { name: texts.delete, exact: true }).click();

      await expect(dialog).toBeHidden();
      await expect(blazorToast(page, texts.usersDeleted(3))).toBeVisible();
      await expectUsersListLoaded(page, 8);
      await expect(userRow(page, other1)).toHaveCount(0);
    })();

    // === RECYCLE BIN ===
    await step("Restore one deleted user from the recycle bin toolbar & verify the toast and the user back in the users list")(async () => {
      await usersTabs(page).getByRole("link", { name: texts.recycleBin, exact: true }).click();
      await expectDeletedUsersListLoaded(page, 3);
      await deletedUserRow(page, other1).getByRole("cell").nth(1).click();
      await page.getByRole("button", { name: texts.restore, exact: true }).click();

      await expect(blazorToast(page, texts.userRestored(other1))).toBeVisible();
      await expectDeletedUsersListLoaded(page, 2);
      expect(await getUserEmailsThroughAccountApi(page)).toContain(other1);
    })();

    await step("Permanently delete one user & verify Delete permanently, the toast and the user gone for good")(async () => {
      await deletedUserRow(page, other2).getByRole("cell").nth(1).click();
      await page.getByRole("button", { name: texts.delete, exact: true }).click();
      const dialog = page.getByRole("alertdialog", { name: texts.permanentlyDeleteUser, exact: true });
      await dialog.getByRole("button", { name: texts.deletePermanently, exact: true }).click();

      await expect(dialog).toBeHidden();
      await expect(blazorToast(page, texts.userPermanentlyDeleted(other2))).toBeVisible();
      await expectDeletedUsersListLoaded(page, 1);
      expect((await getDeletedUsersThroughAccountApi(page)).map((user) => user.email)).toEqual([other3]);
    })();

    await step("Permanently delete a selection of two users & verify the toast and the remaining deleted user")(async () => {
      expect((await bulkDeleteUsersThroughAccountApi(page, [await idOf(other4), await idOf(other5)])).status).toBe(200);
      await gotoRecycleBinPage(page);
      await expectDeletedUsersListLoaded(page, 3);

      await selectRowsWithModifier(page, [deletedUserRow(page, other3), deletedUserRow(page, other4)]);
      await page.getByRole("button", { name: texts.deleteCountUsers(2), exact: true }).click();
      const dialog = page.getByRole("alertdialog", { name: texts.permanentlyDeleteUsers, exact: true });
      await dialog.getByRole("button", { name: texts.deletePermanently, exact: true }).click();

      await expect(dialog).toBeHidden();
      await expect(blazorToast(page, texts.usersPermanentlyDeleted(2))).toBeVisible();
      await expectDeletedUsersListLoaded(page, 1);
      await expect(deletedUserRow(page, other5)).toBeVisible();
    })();

    await step("Empty the recycle bin & verify the toast and Recycle bin is empty")(async () => {
      await deleteUserThroughAccountApi(page, await idOf(other6));
      await gotoRecycleBinPage(page);
      await expectDeletedUsersListLoaded(page, 2);

      await page.getByRole("button", { name: texts.emptyRecycleBin, exact: true }).click();
      const dialog = page.getByRole("alertdialog", { name: texts.emptyRecycleBin, exact: true });
      await dialog.getByRole("button", { name: texts.emptyRecycleBin, exact: true }).click();

      await expect(dialog).toBeHidden();
      await expect(blazorToast(page, texts.usersPermanentlyDeleted(2))).toBeVisible();
      await expect(page.getByText(texts.recycleBinIsEmpty, { exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: texts.emptyRecycleBin, exact: true })).toHaveCount(0);
      expect(await getDeletedUsersThroughAccountApi(page)).toEqual([]);
      await expectNoPolicyViolations(page);
    })();

    // === ADMIN ===
    const adminContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const adminPage = await adminContext.newPage();
    const adminTestContext = createTestContext(adminPage);
    await trackPolicyViolations(adminPage);

    await step("Log in as the admin and open the recycle bin & verify restore and delete for one user but no Empty recycle bin")(async () => {
      await deleteUserThroughAccountApi(page, await idOf(other1));
      await logInInvitedUserThroughBlazor(adminPage, adminEmail, { firstName: "Ada", lastName: "Admin" });

      await gotoRecycleBinPage(adminPage);

      await expectDeletedUsersListLoaded(adminPage, 1);
      await expect(adminPage.getByRole("button", { name: texts.emptyRecycleBin, exact: true })).toHaveCount(0);
      await deletedUserRow(adminPage, other1).getByRole("cell").nth(1).click();
      await expect(adminPage.getByRole("button", { name: texts.restore, exact: true })).toBeVisible();
      await expect(adminPage.getByRole("button", { name: texts.delete, exact: true })).toBeVisible();
      await expectNoPolicyViolations(adminPage);
    })();

    await step("Call owner-only users endpoints through the account API as the admin & verify each refusal and no change")(async () => {
      const other1Id = await deletedIdOf(other1);
      const other7Id = await idOf(other7);

      expectAccountApiProblem(await bulkPurgeUsersThroughAccountApi(adminPage, [other1Id]), 403, accountApiMessages.onlyOwnersCanBulkPurgeUsers);
      expectAccountApiProblem(await emptyRecycleBinThroughAccountApi(adminPage), 403, accountApiMessages.onlyOwnersCanEmptyRecycleBin);
      expectAccountApiProblem(await inviteUserThroughAccountApi(adminPage, uniqueBlazorEmail()), 403, accountApiMessages.onlyOwnersCanInviteUsers);
      expectAccountApiProblem(await deleteUserResponseThroughAccountApi(adminPage, other7Id), 403, accountApiMessages.onlyOwnersCanDeleteUsers);
      expectAccountApiProblem(await bulkDeleteUsersThroughAccountApi(adminPage, [other7Id]), 403, accountApiMessages.onlyOwnersCanDeleteUsers);
      await expectNetworkErrors(adminTestContext, [403]);

      expect((await getDeletedUsersThroughAccountApi(page)).map((user) => user.email)).toEqual([other1]);
      expect(await getUserEmailsThroughAccountApi(page)).toContain(other7);
    })();

    await step("Restore one user and permanently delete another through the account API as the admin & verify both are allowed")(async () => {
      const other1Id = await deletedIdOf(other1);
      await deleteUserThroughAccountApi(page, await idOf(other8));
      const other8Id = await deletedIdOf(other8);

      expect((await restoreUserThroughAccountApi(adminPage, other1Id)).status).toBe(200);
      expect((await purgeUserThroughAccountApi(adminPage, other8Id)).status).toBe(200);

      expect(await getUserEmailsThroughAccountApi(page)).toContain(other1);
      expect(await getDeletedUsersThroughAccountApi(page)).toEqual([]);
      expect(await getUserEmailsThroughAccountApi(page)).not.toContain(other8);
      await deleteUserThroughAccountApi(page, other1Id);
      expect((await getDeletedUsersThroughAccountApi(page)).map((user) => user.email)).toEqual([other1]);
    })();

    // === MEMBER ===
    const memberContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const memberPage = await memberContext.newPage();
    const memberTestContext = createTestContext(memberPage);
    await trackPolicyViolations(memberPage);

    await step("Call users administration endpoints through the account API as a member & verify each refusal and no change")(async () => {
      const other1Id = await deletedIdOf(other1);
      const other7Id = await idOf(other7);
      await logInInvitedUserThroughBlazor(memberPage, memberEmail, { firstName: "Mel", lastName: "Member" });

      expectAccountApiProblem(await getDeletedUsersResponseThroughAccountApi(memberPage), 403, accountApiMessages.onlyOwnersAndAdminsCanViewDeletedUsers);
      expectAccountApiProblem(await restoreUserThroughAccountApi(memberPage, other1Id), 403, accountApiMessages.onlyOwnersAndAdminsCanRestoreUsers);
      expectAccountApiProblem(await purgeUserThroughAccountApi(memberPage, other1Id), 403, accountApiMessages.onlyOwnersAndAdminsCanPurgeUsers);
      expectAccountApiProblem(await bulkPurgeUsersThroughAccountApi(memberPage, [other1Id]), 403, accountApiMessages.onlyOwnersCanBulkPurgeUsers);
      expectAccountApiProblem(await emptyRecycleBinThroughAccountApi(memberPage), 403, accountApiMessages.onlyOwnersCanEmptyRecycleBin);
      expectAccountApiProblem(await inviteUserThroughAccountApi(memberPage, uniqueBlazorEmail()), 403, accountApiMessages.onlyOwnersCanInviteUsers);
      expectAccountApiProblem(await deleteUserResponseThroughAccountApi(memberPage, other7Id), 403, accountApiMessages.onlyOwnersCanDeleteUsers);
      expectAccountApiProblem(await bulkDeleteUsersThroughAccountApi(memberPage, [other7Id]), 403, accountApiMessages.onlyOwnersCanDeleteUsers);
      await expectNetworkErrors(memberTestContext, [403]);

      expect((await getDeletedUsersThroughAccountApi(page)).map((user) => user.email)).toEqual([other1]);
      expect(await getUserEmailsThroughAccountApi(page)).toContain(other7);
      await expectNoPolicyViolations(memberPage);
    })();

    // === ANOTHER ACCOUNT ===
    const otherContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const otherPage = await otherContext.newPage();
    const otherTestContext = createTestContext(otherPage);
    await trackPolicyViolations(otherPage);
    const foreignActiveEmail = `foreign-active-${uniqueBlazorEmail()}`;
    const foreignDeletedEmail = `foreign-deleted-${uniqueBlazorEmail()}`;

    await step("Send another account's user ids alone and mixed with own ids & verify not found, no disclosure and no change")(async () => {
      await signUpThroughBlazor(otherPage, uniqueBlazorEmail());
      await inviteUsersThroughAccountApi(otherPage, [foreignActiveEmail, foreignDeletedEmail]);
      const foreignActiveId = (await findUserThroughAccountApi(otherPage, foreignActiveEmail)).id;
      const foreignDeletedId = (await findUserThroughAccountApi(otherPage, foreignDeletedEmail)).id;
      await deleteUserThroughAccountApi(otherPage, foreignDeletedId);
      const other1Id = await deletedIdOf(other1);
      const other7Id = await idOf(other7);

      const responses = [
        await deleteUserResponseThroughAccountApi(page, foreignActiveId),
        await restoreUserThroughAccountApi(page, foreignDeletedId),
        await purgeUserThroughAccountApi(page, foreignDeletedId),
        await bulkDeleteUsersThroughAccountApi(page, [other7Id, foreignActiveId]),
        await bulkPurgeUsersThroughAccountApi(page, [other1Id, foreignDeletedId])
      ];
      expectAccountApiProblem(responses[0], 404, accountApiMessages.userNotFound(foreignActiveId));
      expectAccountApiProblem(responses[1], 404, accountApiMessages.deletedUserNotFound(foreignDeletedId));
      expectAccountApiProblem(responses[2], 404, accountApiMessages.deletedUserNotFound(foreignDeletedId));
      expectAccountApiProblem(responses[3], 404, accountApiMessages.usersNotFound([foreignActiveId]));
      expectAccountApiProblem(responses[4], 404, accountApiMessages.deletedUsersNotFound([foreignDeletedId]));
      await expectNetworkErrors(context, [404]);
      for (const response of responses) {
        expect(response.body).not.toContain(foreignActiveEmail);
        expect(response.body).not.toContain(foreignDeletedEmail);
      }

      expect(await getUserEmailsThroughAccountApi(page)).toContain(other7);
      expect((await getDeletedUsersThroughAccountApi(page)).map((user) => user.email)).toEqual([other1]);
      expect(await getUserEmailsThroughAccountApi(otherPage)).toContain(foreignActiveEmail);
      expect((await getDeletedUsersThroughAccountApi(otherPage)).map((user) => user.email)).toEqual([foreignDeletedEmail]);
      await expectNoPolicyViolations(otherPage);
    })();

    // === SELF AND BATCH LIMITS ===
    await step("Delete yourself alone and in a batch, send an empty batch and batches of 100 and 101 & verify each refusal")(async () => {
      const ownerId = await idOf(ownerEmail);
      const other7Id = await idOf(other7);
      const hundredIds = unknownUserIds(ownerId, 100);
      const hundredAndOneIds = unknownUserIds(ownerId, 101);

      expectAccountApiProblem(await deleteUserResponseThroughAccountApi(page, ownerId), 403, accountApiMessages.cannotDeleteYourself);
      expectAccountApiProblem(await bulkDeleteUsersThroughAccountApi(page, [ownerId, other7Id]), 403, accountApiMessages.cannotDeleteYourself);
      await expectNetworkErrors(context, [403]);
      expectAccountApiValidationProblem(await bulkDeleteUsersThroughAccountApi(page, []), "userIds", accountApiMessages.noUserSelected);
      expectAccountApiValidationProblem(await bulkPurgeUsersThroughAccountApi(page, []), "userIds", accountApiMessages.noUserSelected);
      expectAccountApiValidationProblem(await bulkDeleteUsersThroughAccountApi(page, hundredAndOneIds), "userIds", accountApiMessages.tooManyUsersSelected);
      expectAccountApiValidationProblem(await bulkPurgeUsersThroughAccountApi(page, hundredAndOneIds), "userIds", accountApiMessages.tooManyUsersSelected);
      await expectNetworkErrors(context, [400]);
      expectAccountApiProblem(await bulkDeleteUsersThroughAccountApi(page, hundredIds), 404, accountApiMessages.usersNotFound(hundredIds));
      expectAccountApiProblem(await bulkPurgeUsersThroughAccountApi(page, hundredIds), 404, accountApiMessages.deletedUsersNotFound(hundredIds));
      await expectNetworkErrors(context, [404]);

      expect(await getUserEmailsThroughAccountApi(page)).toContain(ownerEmail);
      expect(await getUserEmailsThroughAccountApi(page)).toContain(other7);
    })();

    // === ROLE CHANGE ===
    await step("Demote the admin, refresh the admin's claims and press the stale Restore & verify the refusal and nothing restored")(async () => {
      await deleteUserThroughAccountApi(page, await idOf(other7));
      await gotoRecycleBinPage(adminPage);
      await expectDeletedUsersListLoaded(adminPage, 2);
      await deletedUserRow(adminPage, other7).getByRole("cell").nth(1).click();
      const restoreButton = adminPage.getByRole("button", { name: texts.restore, exact: true });
      await expect(restoreButton).toBeVisible();

      expect((await changeUserRoleThroughAccountApi(page, await idOf(adminEmail), "Member")).status).toBe(200);
      await refreshClaimsByUpdatingProfileThroughAccountApi(adminPage, { firstName: "Ada", lastName: "Admin" });
      await restoreButton.click();

      const toast = blazorToast(adminPage, texts.somethingWentWrong);
      await expect(toast.getByText(accountApiMessages.onlyOwnersAndAdminsCanRestoreUsers, { exact: true })).toBeVisible();
      await expectNetworkErrors(adminTestContext, [403]);
      expect((await getDeletedUsersThroughAccountApi(page)).map((user) => user.email).sort()).toEqual([other1, other7].sort());
    })();

    await step("Reload the recycle bin as the demoted admin & verify Access denied and the API refusing the list")(async () => {
      await expectNoPolicyViolations(adminPage);
      await adminPage.reload();

      await expect(adminPage.getByRole("heading", { name: texts.accessDenied, exact: true })).toBeVisible();
      await expect(adminPage.getByText(texts.noPermissionToAccessPage, { exact: true })).toBeVisible();
      await expect(adminPage.getByRole("link", { name: texts.recycleBin, exact: true })).toHaveCount(0);
      expectAccountApiProblem(await getDeletedUsersResponseThroughAccountApi(adminPage), 403, accountApiMessages.onlyOwnersAndAdminsCanViewDeletedUsers);
      await expectNetworkErrors(adminTestContext, [403]);
      await expectNoPolicyViolations(adminPage);
    })();

    await expectNoPolicyViolations(page);
    await assertNoUnexpectedErrors(adminTestContext);
    await assertNoUnexpectedErrors(memberTestContext);
    await assertNoUnexpectedErrors(otherTestContext);
    await otherContext.close();
    await memberContext.close();
    await adminContext.close();
  });
});
