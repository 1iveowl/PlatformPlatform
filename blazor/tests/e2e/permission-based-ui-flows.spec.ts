/// <reference types="node" />
import { readFileSync } from "node:fs";
import path from "node:path";
import { expect } from "@playwright/test";
import {
  changeUserRoleThroughAccountApi,
  deleteUserThroughAccountApi,
  expectAccountApiProblem,
  findUserThroughAccountApi,
  inviteUsersThroughAccountApi
} from "@blazor/e2e/account-api";
import { logInInvitedUserThroughBlazor, signUpThroughBlazor, test, userMenuButton } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, blazorUrl, expectBlazorUrl } from "@blazor/e2e/routes";
import {
  accountNameInput,
  chooseLogoFile,
  gotoAccountSettingsPage,
  logoImage,
  logoPickerButton,
  type LogoUploadFile,
  readCurrentTenantThroughAccountApi,
  removeLogoThroughPicker,
  removeTenantLogoThroughAccountApi,
  saveAccountSettingsButton,
  updateTenantNameThroughAccountApi,
  uploadTenantLogoThroughAccountApi
} from "@blazor/e2e/settings";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { accountApiMessages, blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { blazorToast, dismissBlazorToast } from "@blazor/e2e/toast";
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
import { assertNoUnexpectedErrors, createTestContext, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * The fixture image the account logo steps upload, the image the profile specification uploads as an avatar
 */
const logoFile = path.join(__dirname, "fixtures", "avatar.png");

/**
 * The same image as a multipart upload, for the direct posts an admin and a member are refused
 */
const logoUpload: LogoUploadFile = { name: "logo.png", mimeType: "image/png", buffer: readFileSync(logoFile) };

/**
 * The name the owner gives the account, which everyone else then sees in the read-only field
 */
const renamedAccountName = "Renamed account";

test.describe("@smoke", () => {
  /**
   * What the users pages and the account settings page show an owner, an admin and a member of one account, mirroring the
   * users and settings steps of the React permission-based UI test
   * - Owner: Invite user, the recycle bin tab, Delete and Change role disabled on the own row, "Delete 2 users" for a Ctrl or
   *   Cmd selection of two other users, disabled with its reason while the selection includes the owner
   * - Owner: the account settings page with the editable name, Save changes and the danger zone; renaming the account shows
   *   the toast and updates the shell header without a reload, and a logo is uploaded through the picker and removed again,
   *   each checked against the account the API stores
   * - Admin: no Invite user, only View profile in a row menu, no bulk delete for a selection, and the recycle bin with
   *   restore and delete for one user but no Empty recycle bin; the account API refuses a direct name, logo and logo
   *   removal write with its own message, and the stored account is unchanged
   * - Member: no Invite user, only View profile on the own row, no tabs, no bulk delete for a selection, and Access denied
   *   with Go to home on the recycle bin
   * - Member: the account settings page with the read-only name, its explanation, no Save changes, no logo picker and no
   *   danger zone; the same three account API writes are refused and the stored account is unchanged
   * - No securitypolicyviolation event and no style attribute on any document
   */
  test("should show users and account settings controls according to the owner, admin and member roles", async ({ page, browser }) => {
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

    await step("Open the account settings as the owner and rename the account & verify the toast and the header without a reload")(async () => {
      await gotoAccountSettingsPage(page);
      await expect(accountNameInput(page)).toBeEnabled();
      await expect(page.getByText(texts.onlyOwnersCanModifyAccountName, { exact: true })).toHaveCount(0);
      await expect(page.getByRole("heading", { name: texts.dangerZone, exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: texts.deleteAccount, exact: true })).toBeVisible();

      await accountNameInput(page).fill(renamedAccountName);
      await saveAccountSettingsButton(page).click();

      const toast = blazorToast(page, texts.accountSettingsUpdated);
      await expect(toast).toBeVisible();
      expect((await readCurrentTenantThroughAccountApi(page)).name).toBe(renamedAccountName);
      await expect(userMenuButton(page)).toHaveAccessibleDescription(`Blazor User ${renamedAccountName}`);
      await dismissBlazorToast(page, toast);
    })();

    await step("Upload an account logo through the picker and save & verify the stored logo and the picker image")(async () => {
      await chooseLogoFile(page, logoFile);

      await saveAccountSettingsButton(page).click();

      const toast = blazorToast(page, texts.accountSettingsUpdated);
      await expect(toast).toBeVisible();
      const logoUrl = (await readCurrentTenantThroughAccountApi(page)).logoUrl;
      expect(logoUrl!.startsWith("/logos/")).toBe(true);
      await expect(logoImage(logoPickerButton(page))).toHaveAttribute("src", logoUrl!);
      await dismissBlazorToast(page, toast);
    })();

    await step("Remove the account logo through the picker and save & verify the initials and no stored logo")(async () => {
      await removeLogoThroughPicker(page);
      await expect(logoImage(logoPickerButton(page))).toHaveCount(0);

      await saveAccountSettingsButton(page).click();

      const toast = blazorToast(page, texts.accountSettingsUpdated);
      await expect(toast).toBeVisible();
      expect((await readCurrentTenantThroughAccountApi(page)).logoUrl).toBeNull();
      await dismissBlazorToast(page, toast);
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

    await step("Send a name, a logo and a logo removal to the account API as the admin & verify each is refused and the account is unchanged")(async () => {
      const before = await readCurrentTenantThroughAccountApi(adminPage);

      expectAccountApiProblem(await updateTenantNameThroughAccountApi(adminPage, "Admin renamed"), 403, accountApiMessages.onlyOwnersCanUpdateTenantInformation);
      expectAccountApiProblem(await uploadTenantLogoThroughAccountApi(adminPage, logoUpload), 403, accountApiMessages.onlyOwnersCanUpdateTenantLogo);
      expectAccountApiProblem(await removeTenantLogoThroughAccountApi(adminPage), 403, accountApiMessages.onlyOwnersCanRemoveTenantLogo);

      expect(await readCurrentTenantThroughAccountApi(adminPage)).toEqual(before);
      await expectNetworkErrors(adminTestContext, [403]);
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

    await step("Open the account settings as the member & verify the read-only name, its explanation and no Save, picker or danger zone")(async () => {
      await gotoAccountSettingsPage(memberPage);

      await expect(accountNameInput(memberPage)).toHaveValue(renamedAccountName);
      await expect(accountNameInput(memberPage)).toHaveAttribute("readonly", "");
      await expect(memberPage.getByText(texts.onlyOwnersCanModifyAccountName, { exact: true })).toBeVisible();
      await expect(saveAccountSettingsButton(memberPage)).toHaveCount(0);
      await expect(logoPickerButton(memberPage)).toHaveCount(0);
      await expect(memberPage.getByRole("heading", { name: texts.dangerZone, exact: true })).toHaveCount(0);
      await expectNoPolicyViolations(memberPage);
    })();

    await step("Send a name, a logo and a logo removal to the account API as the member & verify each is refused and the account is unchanged")(async () => {
      const before = await readCurrentTenantThroughAccountApi(memberPage);

      expectAccountApiProblem(await updateTenantNameThroughAccountApi(memberPage, "Member renamed"), 403, accountApiMessages.onlyOwnersCanUpdateTenantInformation);
      expectAccountApiProblem(await uploadTenantLogoThroughAccountApi(memberPage, logoUpload), 403, accountApiMessages.onlyOwnersCanUpdateTenantLogo);
      expectAccountApiProblem(await removeTenantLogoThroughAccountApi(memberPage), 403, accountApiMessages.onlyOwnersCanRemoveTenantLogo);

      expect(await readCurrentTenantThroughAccountApi(memberPage)).toEqual(before);
      await expectNetworkErrors(memberTestContext, [403]);
    })();

    await expectNoPolicyViolations(page);
    await assertNoUnexpectedErrors(context);
    await assertNoUnexpectedErrors(adminTestContext);
    await assertNoUnexpectedErrors(memberTestContext);
    await memberContext.close();
    await adminContext.close();
  });
});
