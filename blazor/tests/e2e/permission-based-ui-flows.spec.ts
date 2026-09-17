import { expect } from "@playwright/test";
import { changeUserRoleThroughAccountApi, deleteUserThroughAccountApi, findUserThroughAccountApi, inviteUsersThroughAccountApi } from "@blazor/e2e/account-api";
import { logInInvitedUserThroughBlazor, signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, blazorUrl, expectBlazorUrl } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import {
  deletedUserRow,
  expectDeletedUsersListLoaded,
  gotoRecycleBinPage,
  gotoUsersPage,
  openUserActions,
  recycleBinRoute,
  selectRowsWithModifier,
  userRow,
  usersTabs
} from "@blazor/e2e/users";
import { assertNoUnexpectedErrors, createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

test.describe("@smoke", () => {
  /**
   * What the users pages show an owner, an admin and a member of one account, mirroring the users steps of the React
   * permission-based UI test; the account settings steps belong to the settings specification
   * - Owner: Invite user, the recycle bin tab, Delete and Change role disabled on the own row, "Delete 2 users" for a Ctrl or
   *   Cmd selection of two other users, disabled with its reason while the selection includes the owner
   * - Admin: no Invite user, only View profile in a row menu, no bulk delete for a selection, and the recycle bin with
   *   restore and delete for one user but no Empty recycle bin
   * - Member: no Invite user, only View profile on the own row, no tabs, no bulk delete for a selection, and Access denied
   *   with Go to home on the recycle bin
   * - No securitypolicyviolation event and no style attribute on any document
   */
  test("should show users administration controls according to the owner, admin and member roles", async ({ page, browser }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const ownerEmail = uniqueBlazorEmail();
    const adminEmail = `admin-${uniqueBlazorEmail()}`;
    const memberEmail = `member-${uniqueBlazorEmail()}`;
    const firstOtherEmail = `other-1-${uniqueBlazorEmail()}`;
    const secondOtherEmail = `other-2-${uniqueBlazorEmail()}`;
    const deletedEmail = `deleted-${uniqueBlazorEmail()}`;

    // === OWNER ===
    await step("Sign up an owner, invite users and open the users page & verify Invite user, the tabs and the own row's disabled actions")(async () => {
      await signUpThroughBlazor(page, ownerEmail);
      await inviteUsersThroughAccountApi(page, [adminEmail, memberEmail, firstOtherEmail, secondOtherEmail, deletedEmail]);
      expect((await changeUserRoleThroughAccountApi(page, (await findUserThroughAccountApi(page, adminEmail)).id, "Admin")).status).toBe(200);
      await deleteUserThroughAccountApi(page, (await findUserThroughAccountApi(page, deletedEmail)).id);

      await gotoUsersPage(page);

      await expect(page.getByRole("button", { name: texts.inviteUser, exact: true })).toBeVisible();
      await expect(usersTabs(page).getByRole("link", { name: texts.allUsers, exact: true })).toBeVisible();
      await expect(usersTabs(page).getByRole("link", { name: texts.recycleBin, exact: true })).toBeVisible();
      const menu = await openUserActions(page, ownerEmail);
      await expect(menu.getByRole("menuitem", { name: texts.viewProfile, exact: true })).toBeEnabled();
      await expect(menu.getByRole("menuitem", { name: texts.changeRole, exact: true })).toBeDisabled();
      await expect(menu.getByRole("menuitem", { name: texts.delete, exact: true })).toBeDisabled();
      await page.keyboard.press("Escape");
      await expect(menu).toBeHidden();
    })();

    await step("Select two other users with Ctrl or Cmd, then the owner too & verify bulk delete and its disabled reason")(async () => {
      await selectRowsWithModifier(page, [userRow(page, firstOtherEmail), userRow(page, secondOtherEmail)]);

      const bulkDelete = page.getByRole("button", { name: texts.deleteCountUsers(2), exact: true });
      await expect(bulkDelete).toBeEnabled();
      await expect(page.getByRole("button", { name: texts.inviteUser, exact: true })).toHaveCount(0);

      await userRow(page, ownerEmail).getByRole("cell").nth(1).click({ modifiers: ["ControlOrMeta"] });
      const bulkDeleteWithOwner = page.getByRole("button", { name: texts.deleteCountUsers(3), exact: true });
      await expect(bulkDeleteWithOwner).toBeDisabled();
      await expect(bulkDeleteWithOwner).toHaveAccessibleDescription(texts.cannotDeleteYourself);
      await expectNoPolicyViolations(page);
    })();

    // === ADMIN ===
    const adminContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const adminPage = await adminContext.newPage();
    const adminTestContext = createTestContext(adminPage);
    await trackPolicyViolations(adminPage);

    await step("Log in as the admin and open a row menu and a selection & verify no Invite user, only View profile and no bulk delete")(async () => {
      await logInInvitedUserThroughBlazor(adminPage, adminEmail, { firstName: "Ada", lastName: "Admin" });

      await gotoUsersPage(adminPage);

      await expect(adminPage.getByRole("button", { name: texts.inviteUser, exact: true })).toHaveCount(0);
      await expect(usersTabs(adminPage).getByRole("link", { name: texts.recycleBin, exact: true })).toBeVisible();
      const menu = await openUserActions(adminPage, memberEmail);
      await expect(menu.getByRole("menuitem")).toHaveCount(1);
      await expect(menu.getByRole("menuitem", { name: texts.viewProfile, exact: true })).toBeVisible();
      await adminPage.keyboard.press("Escape");
      await expect(menu).toBeHidden();

      await selectRowsWithModifier(adminPage, [userRow(adminPage, firstOtherEmail), userRow(adminPage, secondOtherEmail)]);
      await expect(adminPage.getByRole("button", { name: texts.deleteCountUsers(2), exact: true })).toHaveCount(0);
    })();

    await step("Open the recycle bin as the admin and select a deleted user & verify restore and delete but no Empty recycle bin")(async () => {
      await gotoRecycleBinPage(adminPage);

      await expectDeletedUsersListLoaded(adminPage, 1);
      await expect(adminPage.getByRole("button", { name: texts.emptyRecycleBin, exact: true })).toHaveCount(0);
      await deletedUserRow(adminPage, deletedEmail).getByRole("cell").nth(1).click();
      await expect(adminPage.getByRole("button", { name: texts.restore, exact: true })).toBeVisible();
      await expect(adminPage.getByRole("button", { name: texts.delete, exact: true })).toBeVisible();
      await expectNoPolicyViolations(adminPage);
    })();

    // === MEMBER ===
    const memberContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const memberPage = await memberContext.newPage();
    const memberTestContext = createTestContext(memberPage);
    await trackPolicyViolations(memberPage);

    await step("Log in as the member and open the own row menu and a selection & verify only View profile, no tabs and no bulk delete")(async () => {
      await logInInvitedUserThroughBlazor(memberPage, memberEmail, { firstName: "Mel", lastName: "Member" });

      await gotoUsersPage(memberPage);

      await expect(memberPage.getByRole("button", { name: texts.inviteUser, exact: true })).toHaveCount(0);
      await expect(memberPage.getByRole("link", { name: texts.allUsers, exact: true })).toHaveCount(0);
      await expect(memberPage.getByRole("link", { name: texts.recycleBin, exact: true })).toHaveCount(0);
      const menu = await openUserActions(memberPage, memberEmail);
      await expect(menu.getByRole("menuitem")).toHaveCount(1);
      await expect(menu.getByRole("menuitem", { name: texts.viewProfile, exact: true })).toBeVisible();
      await memberPage.keyboard.press("Escape");
      await expect(menu).toBeHidden();

      await selectRowsWithModifier(memberPage, [userRow(memberPage, firstOtherEmail), userRow(memberPage, secondOtherEmail)]);
      await expect(memberPage.getByRole("button", { name: texts.deleteCountUsers(2), exact: true })).toHaveCount(0);
    })();

    await step("Open the recycle bin as the member and go home & verify Access denied and the way back to the workspace")(async () => {
      await expectNoPolicyViolations(memberPage);
      await memberPage.goto(blazorPath(recycleBinRoute));

      await expect(memberPage.getByRole("heading", { name: texts.accessDenied, exact: true })).toBeVisible();
      await expect(memberPage.getByText(texts.noPermissionToAccessPage, { exact: true })).toBeVisible();
      await expect(memberPage.getByRole("button", { name: texts.restore, exact: true })).toHaveCount(0);
      await expectNoPolicyViolations(memberPage);
      await memberPage.getByRole("link", { name: texts.goToHome, exact: true }).click();
      await expectBlazorUrl(memberPage, "app");
    })();

    await expectNoPolicyViolations(page);
    await assertNoUnexpectedErrors(context);
    await assertNoUnexpectedErrors(adminTestContext);
    await assertNoUnexpectedErrors(memberTestContext);
    await memberContext.close();
    await adminContext.close();
  });
});
