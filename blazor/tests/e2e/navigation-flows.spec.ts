import { expect } from "@playwright/test";
import {
  completeWelcomeThroughBlazor,
  logInInvitedUserThroughBlazor,
  logOutThroughBlazor,
  startEmailFlowThroughBlazor,
  test,
  userMenuButton
} from "@blazor/e2e/authentication";
import { submitOneTimePassword } from "@blazor/e2e/one-time-password";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, expectBlazorUrl, expectNewDocument, gotoBlazor, markDocument } from "@blazor/e2e/routes";
import { mainNavigation } from "@blazor/e2e/shell";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts } from "@blazor/e2e/texts";
import { blazorToast } from "@blazor/e2e/toast";
import { expectUsersListLoaded, gotoUsersPage, inviteUserThroughDialog, recycleBinRoute, userRow } from "@blazor/e2e/users";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

test.describe("@smoke", () => {
  /**
   * One journey across every delivered surface of this edition, the cross-surface coverage the React edition keeps in its
   * federated navigation specification:
   * - The public login and signup pages, then signup with its verification code and the welcome setup
   * - Every link of the shell's main navigation, each landing on a delivered surface: the workspace, the users list, the
   *   profile, the preferences, the sessions and the account settings
   * - The legal documents from inside the authenticated surface and the workspace again, each a new document
   * - Browser Back and Forward across those documents
   * - Inviting a member through the invite dialog, then logging out into a new document
   * - The member's own login with the profile step of the welcome setup, their profile and sessions, and the recycle bin
   *   refusing them
   * - No policy violation on any document of the journey
   */
  test("should walk the public, authenticated and legal surfaces for an owner and an invited member", async ({ page }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const ownerEmail = uniqueBlazorEmail();
    const memberEmail = uniqueBlazorEmail();
    await trackPolicyViolations(page);
    const navigationLink = (name: string) => mainNavigation(page).getByRole("link", { name, exact: true });

    // === THE PUBLIC PAGES ===
    await step("Open the login page & verify its heading and email field")(async () => {
      await gotoBlazor(page, "login");

      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
      await expect(page.getByLabel(texts.email, { exact: true })).toBeVisible();
      await expectNoPolicyViolations(page);
    })();

    await step("Open the signup page & verify its heading and email field")(async () => {
      await gotoBlazor(page, "signup");

      await expect(page.getByRole("heading", { name: texts.createYourAccount })).toBeVisible();
      await expect(page.getByLabel(texts.email, { exact: true })).toBeVisible();
      await expectNoPolicyViolations(page);
    })();

    // === SIGNUP, VERIFICATION AND WELCOME ===
    await step("Sign up with the verification code and complete the welcome setup & verify the workspace in a new document")(async () => {
      await startEmailFlowThroughBlazor(page, "signup", ownerEmail);
      await markDocument(page);

      await submitOneTimePassword(page);
      await completeWelcomeThroughBlazor(page, { accountName: "Navigation account", firstName: "Olive", lastName: "Owner" });

      await expect(page.getByRole("heading", { name: texts.yourWorkspace, exact: true, level: 1 })).toBeVisible();
      await expect(userMenuButton(page)).toBeVisible();
      await expectNewDocument(page);
    })();

    // === EVERY LINK OF THE SHELL'S MAIN NAVIGATION ===
    await step("Open the users page from the sidebar & verify the list of the new account")(async () => {
      await navigationLink(texts.users).click();

      await expectBlazorUrl(page, "account/users");
      await expect(page.getByRole("heading", { name: texts.users, exact: true, level: 1 })).toBeVisible();
      await expectUsersListLoaded(page, 1);
    })();

    await step("Open the profile page from the sidebar & verify the profile surface")(async () => {
      await navigationLink(texts.profile).click();

      await expectBlazorUrl(page, "user/profile");
      await expect(page.getByRole("heading", { name: texts.profile, exact: true, level: 1 })).toBeVisible();
      await expect(page.getByLabel(texts.firstName, { exact: true })).toHaveValue("Olive");
    })();

    await step("Open the preferences page from the sidebar & verify the preference groups")(async () => {
      await navigationLink(texts.preferences).click();

      await expectBlazorUrl(page, "user/preferences");
      await expect(page.getByRole("heading", { name: texts.userPreferences, exact: true, level: 1 })).toBeVisible();
      await expect(page.getByRole("radiogroup", { name: texts.theme, exact: true })).toBeVisible();
    })();

    await step("Open the sessions page from the sidebar & verify this device is listed")(async () => {
      await navigationLink(texts.sessions).click();

      await expectBlazorUrl(page, "user/sessions");
      await expect(page.getByRole("heading", { name: texts.userSessions, exact: true, level: 1 })).toBeVisible();
      await expect(page.getByText(texts.thisDevice, { exact: true })).toBeVisible();
    })();

    await step("Open the account settings from the sidebar & verify the account name of the new account")(async () => {
      await navigationLink(texts.settings).click();

      await expectBlazorUrl(page, "account/settings");
      await expect(page.getByRole("heading", { name: texts.accountSettings, exact: true, level: 1 })).toBeVisible();
      await expect(page.getByLabel(texts.accountName, { exact: true })).toHaveValue("Navigation account");
      await expectNoPolicyViolations(page);
    })();

    // === THE LEGAL DOCUMENTS FROM THE AUTHENTICATED SURFACE ===
    await step("Open the terms document from the authenticated surface & verify a new public document")(async () => {
      await markDocument(page);

      await page.goto(blazorPath("legal/terms"));

      await expectBlazorUrl(page, "legal/terms");
      await expect(page.getByRole("heading", { name: texts.termsOfService, level: 1 })).toBeVisible();
      await expectNewDocument(page);
      await expectNoPolicyViolations(page);
    })();

    await step("Open the privacy document & verify a new public document")(async () => {
      await markDocument(page);

      await page.goto(blazorPath("legal/privacy"));

      await expectBlazorUrl(page, "legal/privacy");
      await expect(page.getByRole("heading", { name: texts.privacyPolicy, level: 1 })).toBeVisible();
      await expectNewDocument(page);
    })();

    await step("Return to the workspace & verify the shell renders in a new document")(async () => {
      await markDocument(page);

      await page.goto(blazorPath("app"));

      await expect(page.getByRole("heading", { name: texts.yourWorkspace, exact: true, level: 1 })).toBeVisible();
      await expect(userMenuButton(page)).toBeVisible();
      await expectNewDocument(page);
    })();

    // === BROWSER HISTORY ACROSS THE SURFACES ===
    await step("Go back twice through the browser history & verify the two legal documents render")(async () => {
      await page.goBack();

      await expectBlazorUrl(page, "legal/privacy");
      await expect(page.getByRole("heading", { name: texts.privacyPolicy, level: 1 })).toBeVisible();

      await page.goBack();

      await expectBlazorUrl(page, "legal/terms");
      await expect(page.getByRole("heading", { name: texts.termsOfService, level: 1 })).toBeVisible();
    })();

    await step("Go forward twice through the browser history & verify the workspace renders again")(async () => {
      await page.goForward();

      await expectBlazorUrl(page, "legal/privacy");
      await expect(page.getByRole("heading", { name: texts.privacyPolicy, level: 1 })).toBeVisible();

      await page.goForward();

      await expectBlazorUrl(page, "app");
      await expect(userMenuButton(page)).toBeVisible();
      await expect(page.getByRole("heading", { name: texts.yourWorkspace, exact: true, level: 1 })).toBeVisible();
    })();

    // === INVITING A MEMBER AND LEAVING ===
    await step("Invite a member through the invite dialog & verify the toast and the row in the list")(async () => {
      await gotoUsersPage(page);

      await inviteUserThroughDialog(page, memberEmail);

      await expect(blazorToast(page, texts.userInvited)).toBeVisible();
      await expectUsersListLoaded(page, 2);
      await expect(userRow(page, memberEmail)).toBeVisible();
    })();

    await step("Log out from the user menu & verify the login page in a new document")(async () => {
      await markDocument(page);

      await logOutThroughBlazor(page);

      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
      await expect(userMenuButton(page)).toHaveCount(0);
      await expectNewDocument(page);
    })();

    // === THE INVITED MEMBER ===
    await step("Log in as the invited member and complete the profile step & verify the workspace")(async () => {
      await logInInvitedUserThroughBlazor(page, memberEmail, { firstName: "Mel", lastName: "Member" });

      await expect(page.getByRole("heading", { name: texts.yourWorkspace, exact: true, level: 1 })).toBeVisible();
      await expect(navigationLink(texts.settings)).toHaveCount(0);
    })();

    await step("Open the member's profile and sessions from the sidebar & verify both surfaces render")(async () => {
      await navigationLink(texts.profile).click();

      await expectBlazorUrl(page, "user/profile");
      await expect(page.getByLabel(texts.firstName, { exact: true })).toHaveValue("Mel");

      await navigationLink(texts.sessions).click();

      await expectBlazorUrl(page, "user/sessions");
      await expect(page.getByRole("heading", { name: texts.userSessions, exact: true, level: 1 })).toBeVisible();
      await expect(page.getByText(texts.thisDevice, { exact: true })).toBeVisible();
    })();

    await step("Open the recycle bin as the member & verify access denied and the way back to the workspace")(async () => {
      await expectNoPolicyViolations(page);
      await page.goto(blazorPath(recycleBinRoute));

      await expect(page.getByRole("heading", { name: texts.accessDenied, exact: true })).toBeVisible();
      await expect(page.getByText(texts.noPermissionToAccessPage, { exact: true })).toBeVisible();
      await expectNoPolicyViolations(page);

      await page.getByRole("link", { name: texts.goToHome, exact: true }).click();

      await expectBlazorUrl(page, "app");
      await expect(page.getByRole("heading", { name: texts.yourWorkspace, exact: true, level: 1 })).toBeVisible();
    })();
  });
});
