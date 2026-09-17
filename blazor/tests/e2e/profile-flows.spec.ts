/// <reference types="node" />
import { readFileSync } from "node:fs";
import path from "node:path";
import { expect, type Locator, type Page } from "@playwright/test";
import {
  type AvatarUploadFile,
  getAvatarUrlThroughAccountApi,
  readAntiforgeryToken,
  sendAccountApiRequest,
  uploadAvatarThroughAccountApi
} from "@blazor/e2e/account-api";
import { signUpThroughBlazor, test, userMenuButton } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, blazorUrl, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { accountApiMessages, blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { blazorToast, dismissBlazorToast } from "@blazor/e2e/toast";
import { gotoUsersPage, userRow } from "@blazor/e2e/users";
import { expectBlazorValidationMessage } from "@blazor/e2e/validation";
import { blurActiveElement, createTestContext, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

declare global {
  interface Window {
    __documentBeforeSave?: boolean;
  }
}

/**
 * The first name and last name limit of UpdateCurrentUserCommand's validator, plus one
 */
const tooLongName = "a".repeat(31);

/**
 * The title limit of UpdateCurrentUserCommand's validator, plus one
 */
const tooLongTitle = "t".repeat(51);

/**
 * The fixture images and files the avatar steps upload, from blazor/tests/e2e/fixtures
 */
const fixturesFolder = path.join(__dirname, "fixtures");
const avatarFile = path.join(fixturesFolder, "avatar.png");
const replacementAvatarFile = path.join(fixturesFolder, "avatar-replacement.png");
const forgedImageFile = path.join(fixturesFolder, "forged-image.png");
const notAnImageFile = path.join(fixturesFolder, "not-an-image.txt");

/**
 * A valid PNG padded past the 1 MiB avatar limit of UpdateAvatarCommand, built from the fixture rather than stored
 */
const oversizedAvatar: AvatarUploadFile = {
  name: "oversized.png",
  mimeType: "image/png",
  buffer: Buffer.concat([readFileSync(avatarFile), Buffer.alloc(1024 * 1024)])
};

/**
 * The avatar image shown inside a control; the image is decorative, so it has no role and is found inside the named control
 */
function avatarImage(container: Locator): Locator {
  return container.locator("img");
}

/**
 * The avatar picker's trigger on the profile page, named "Change profile picture"
 */
function avatarPickerButton(page: Page): Locator {
  return page.getByRole("button", { name: blazorTexts().changeProfilePicture, exact: true });
}

/**
 * Open the avatar picker's menu and choose "Upload profile picture", answering the file dialog with the given file. The
 * item is clicked rather than dispatched, because the browser opens a file dialog only for a real user activation.
 */
async function chooseAvatarFile(page: Page, file: string | AvatarUploadFile): Promise<void> {
  await avatarPickerButton(page).click();
  const menu = page.getByRole("menu", { name: blazorTexts().changeProfilePicture, exact: true });
  await expect(menu).toBeVisible();
  const fileChooser = page.waitForEvent("filechooser");

  await menu.getByRole("menuitem", { name: blazorTexts().uploadProfilePicture, exact: true }).click();

  await (await fileChooser).setFiles(file);
  await expect(menu).toBeHidden();
}

/**
 * Expect the avatar picker to show a blob: preview of a chosen file rather than a stored avatar
 */
async function expectAvatarPreview(page: Page): Promise<void> {
  await expect.poll(async () => (await avatarImage(avatarPickerButton(page)).getAttribute("src")) ?? "").toContain("blob:");
}

test.describe("@smoke", () => {
  /**
   * The profile page, the React edition's profile form with the avatar; identity verification has its own specification.
   * - The page from the header shows email and role and the saved first name, last name and title
   * - Saving trimmed values shows "Profile updated successfully" and updates the header's name without a reload; the
   *   saved values survive the next page load
   * - Too long names, an empty last name, a whitespace-only first name and a too long title show the backend's limits as
   *   field messages and send no request; the account API rejects the same values itself
   * - A fixture PNG under 1 MB chosen through "Change profile picture" and "Upload profile picture" shows as a blob: preview;
   *   saving stores it, and the header and the users list show the stored avatar; "Remove profile picture" and saving
   *   remove it from the server and the header
   * - Leaving with unsaved edits opens "Unsaved changes": Stay keeps the page and the edits, Leave navigates and discards
   * - No securitypolicyviolation event and no style attribute on any profile page document
   */
  test("should edit the profile and avatar with validation, toast and unsaved changes guard", async ({ page }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const email = uniqueBlazorEmail();
    const firstNameInput = page.getByLabel(texts.firstName, { exact: true });
    const lastNameInput = page.getByLabel(texts.lastName, { exact: true });
    const titleInput = page.getByLabel(texts.title, { exact: true });
    const saveButton = page.getByRole("button", { name: texts.saveChanges, exact: true });
    const workspaceLink = page.getByRole("link", { name: texts.home, exact: true });

    // === PROFILE ===
    await step("Sign up and open the profile from the header & verify email, role and saved names")(async () => {
      await signUpThroughBlazor(page, email);

      await page.getByRole("link", { name: texts.profile, exact: true }).click();

      await expectBlazorUrl(page, "user/profile");
      await expect(page.getByRole("heading", { name: texts.profile, exact: true })).toBeVisible();
      await expect(page.getByText(email, { exact: true })).toBeVisible();
      await expect(page.getByText(texts.owner, { exact: true })).toBeVisible();
      await expect(firstNameInput).toHaveValue("Blazor");
      await expect(lastNameInput).toHaveValue("User");
      await expect(userMenuButton(page)).toHaveAccessibleDescription(/^Blazor User /);
    })();

    await step("Update first name, last name and title & verify the toast and the header name without a reload")(async () => {
      await page.evaluate(() => {
        window.__documentBeforeSave = true;
      });
      await firstNameInput.fill("  Ada ");
      await lastNameInput.fill("Lovelace");
      await titleInput.fill("Engineer");

      await saveButton.click();

      const toast = blazorToast(page, texts.profileUpdated);
      await expect(toast).toBeVisible();
      await expect(userMenuButton(page)).toHaveAccessibleDescription(/^Ada Lovelace /);
      await expect(firstNameInput).toHaveValue("Ada");
      expect(await page.evaluate(() => window.__documentBeforeSave)).toBe(true);
      await dismissBlazorToast(page, toast);
    })();

    await step("Reload the profile page & verify the saved values and header name persist")(async () => {
      await expectNoPolicyViolations(page);

      await page.reload();

      await expect(firstNameInput).toHaveValue("Ada");
      await expect(lastNameInput).toHaveValue("Lovelace");
      await expect(titleInput).toHaveValue("Engineer");
      await expect(userMenuButton(page)).toHaveAccessibleDescription(/^Ada Lovelace /);
    })();

    // === AVATAR ===
    await step("Upload a profile picture through the picker menu & verify the preview before saving")(async () => {
      await chooseAvatarFile(page, avatarFile);

      await expectAvatarPreview(page);
      expect(await getAvatarUrlThroughAccountApi(page)).toBeNull();
      await expect(avatarImage(userMenuButton(page))).toHaveCount(0);
    })();

    await step("Save the chosen profile picture & verify the toast, the stored avatar and the header without a reload")(async () => {
      await saveButton.click();

      const toast = blazorToast(page, texts.profileUpdated);
      await expect(toast).toBeVisible();
      const avatarUrl = await getAvatarUrlThroughAccountApi(page);
      expect(avatarUrl!.startsWith("/avatars/")).toBe(true);
      await expect(avatarImage(avatarPickerButton(page))).toHaveAttribute("src", avatarUrl!);
      await expect(avatarImage(userMenuButton(page))).toHaveAttribute("src", avatarUrl!);
      await dismissBlazorToast(page, toast);
    })();

    await step("Open the users list & verify the signed-in user's row shows the stored avatar")(async () => {
      const avatarUrl = await getAvatarUrlThroughAccountApi(page);

      await gotoUsersPage(page);

      await expect(avatarImage(userRow(page, email))).toHaveAttribute("src", avatarUrl!);
    })();

    await step("Remove the profile picture and save & verify the initials in the picker and the header and no stored avatar")(async () => {
      await gotoBlazor(page, "user/profile");
      await expect(avatarImage(avatarPickerButton(page))).toHaveCount(1);
      await avatarPickerButton(page).click();
      const menu = page.getByRole("menu", { name: texts.changeProfilePicture, exact: true });
      await expect(menu).toBeVisible();
      await menu.getByRole("menuitem", { name: texts.removeProfilePicture, exact: true }).dispatchEvent("click");
      await expect(avatarImage(avatarPickerButton(page))).toHaveCount(0);

      await saveButton.click();

      const toast = blazorToast(page, texts.profileUpdated);
      await expect(toast).toBeVisible();
      await expect(avatarImage(userMenuButton(page))).toHaveCount(0);
      expect(await getAvatarUrlThroughAccountApi(page)).toBeNull();
      await dismissBlazorToast(page, toast);
      await expectNoPolicyViolations(page);
    })();

    // === VALIDATION ===
    await step("Submit too long names and title & verify the field messages and no request")(async () => {
      const requests: string[] = [];
      page.on("request", (request) => {
        if (request.method() === "PUT") requests.push(request.url());
      });
      await firstNameInput.fill(tooLongName);
      await lastNameInput.fill(tooLongName);
      await titleInput.fill(tooLongTitle);
      await blurActiveElement(page);

      await saveButton.click();

      await expectBlazorValidationMessage(page, texts.firstNameLength);
      await expectBlazorValidationMessage(page, texts.lastNameLength);
      await expectBlazorValidationMessage(page, texts.titleTooLong);
      expect(requests).toEqual([]);
    })();

    await step("Submit a whitespace-only first name and an empty last name & verify the field messages")(async () => {
      await firstNameInput.fill("   ");
      await lastNameInput.fill("");
      await titleInput.fill("Engineer");
      await blurActiveElement(page);
      await expect(page.getByText(texts.titleTooLong)).toHaveCount(0);

      await saveButton.click();

      await expectBlazorValidationMessage(page, texts.firstNameLength);
      await expectBlazorValidationMessage(page, texts.lastNameLength);
      await expect(page.getByText(texts.titleTooLong)).toHaveCount(0);
    })();

    await step("Send too long values to the account API & verify the server rejects them")(async () => {
      const response = await sendAccountApiRequest(page, "PUT", "/api/account/users/me", { firstName: tooLongName, lastName: "   ", title: tooLongTitle });

      expect(response.status).toBe(400);
      expect(response.body).toContain("First name must be between 1 and 30 characters.");
      expect(response.body).toContain("Last name must be between 1 and 30 characters.");
      expect(response.body).toContain("Title must be no longer than 50 characters.");
      await expectNetworkErrors(context, [400]);
    })();

    // === UNSAVED CHANGES ===
    await step("Navigate away with unsaved edits and choose Stay & verify the page and edits are kept")(async () => {
      await firstNameInput.fill("Grace");
      await lastNameInput.fill("Hopper");

      await workspaceLink.click();

      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeVisible();
      await page.getByRole("button", { name: texts.stay }).click();
      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeHidden();
      await expectBlazorUrl(page, "user/profile");
      await expect(firstNameInput).toHaveValue("Grace");
    })();

    await step("Navigate away with unsaved edits and choose Leave & verify the edits are discarded")(async () => {
      await workspaceLink.click();
      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeVisible();

      await page.getByRole("button", { name: texts.leave }).click();

      await expectBlazorUrl(page, "app");
      await expect(userMenuButton(page)).toHaveAccessibleDescription(/^Ada Lovelace /);
      await expectNoPolicyViolations(page);
    })();
  });
});

test.describe("@comprehensive", () => {
  /**
   * The avatar's refusals, partial save and cancellation, each checked against the server's stored avatar:
   * - An image over 1 MB and a file of another type are refused under the picker with their localized messages, send no
   *   upload request and keep the saved avatar
   * - A forged image (HTML in a .png declared as image/png) passes the client check, and the account API's message is
   *   shown in the form alert as returned; the saved avatar is kept on the server and after a reload
   * - Posted directly to the account API: the forged image, a wrong content type and an oversized image are refused, and a
   *   valid image without an antiforgery token or with a token issued to another identity is refused; none changes the
   *   stored avatar
   * - When the upload commits and the profile PUT fails, the form shows the partial-save message and the API message
   *   without the success toast, the server holds the new avatar but not the other edits, and saving again sends only the
   *   profile PUT
   * - Leaving the page with Leave while an upload is still in flight cancels the request in the browser, and the server keeps
   *   the avatar it had
   * - No securitypolicyviolation event on any profile document
   */
  test("should refuse invalid avatars client-side and through the API and recover from a partial or cancelled save", async ({ page, browser }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const saveButton = page.getByRole("button", { name: texts.saveChanges, exact: true });
    const titleInput = page.getByLabel(texts.title, { exact: true });
    const uploadRequests: string[] = [];
    page.on("request", (request) => {
      if (new URL(request.url()).pathname === "/api/account/users/me/update-avatar") uploadRequests.push(request.method());
    });
    let savedAvatarUrl = "";

    await step("Sign up and save a profile picture & verify the stored avatar")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail());
      await gotoBlazor(page, "user/profile");
      await chooseAvatarFile(page, avatarFile);
      await expectAvatarPreview(page);

      await saveButton.click();

      const toast = blazorToast(page, texts.profileUpdated);
      await expect(toast).toBeVisible();
      savedAvatarUrl = (await getAvatarUrlThroughAccountApi(page))!;
      await expect(avatarImage(avatarPickerButton(page))).toHaveAttribute("src", savedAvatarUrl);
      await dismissBlazorToast(page, toast);
    })();

    // === CLIENT-SIDE REFUSAL ===
    await step("Choose an image over 1 MB & verify the size message, no upload request and the saved avatar kept")(async () => {
      uploadRequests.length = 0;

      await chooseAvatarFile(page, oversizedAvatar);

      await expect(page.getByText(texts.avatarTooLarge, { exact: true })).toBeVisible();
      await expect(avatarImage(avatarPickerButton(page))).toHaveAttribute("src", savedAvatarUrl);
      expect(uploadRequests).toEqual([]);
    })();

    await step("Choose a text file & verify the type message, no upload request and the saved avatar kept")(async () => {
      await chooseAvatarFile(page, notAnImageFile);

      await expect(page.getByText(texts.avatarTypeInvalid, { exact: true })).toBeVisible();
      await expect(page.getByText(texts.avatarTooLarge, { exact: true })).toHaveCount(0);
      await expect(avatarImage(avatarPickerButton(page))).toHaveAttribute("src", savedAvatarUrl);
      expect(uploadRequests).toEqual([]);
    })();

    // === API REFUSAL THROUGH THE FORM ===
    await step("Save a forged image that passes the client check & verify the API message and the saved avatar kept")(async () => {
      await chooseAvatarFile(page, forgedImageFile);
      await expectAvatarPreview(page);

      await saveButton.click();

      await expect(page.getByRole("alert").getByText(accountApiMessages.invalidImageContent, { exact: true })).toBeVisible();
      await expectNetworkErrors(context, [400]);
      await expect(blazorToast(page, texts.profileUpdated)).toHaveCount(0);
      expect(uploadRequests).toEqual(["POST"]);
      expect(await getAvatarUrlThroughAccountApi(page)).toBe(savedAvatarUrl);
    })();

    await step("Reload the profile after the refused upload & verify the saved avatar is shown")(async () => {
      await page.reload();

      await expect(avatarImage(avatarPickerButton(page))).toHaveAttribute("src", savedAvatarUrl);
      await expect(avatarImage(userMenuButton(page))).toHaveAttribute("src", savedAvatarUrl);
    })();

    // === API REFUSAL OF DIRECT UPLOADS ===
    await step("Post a forged image, a wrong content type and an oversized image to the account API & verify each is refused")(async () => {
      const forged = { name: "forged.png", mimeType: "image/png", buffer: readFileSync(forgedImageFile) };
      const wrongType = { name: "avatar.txt", mimeType: "text/plain", buffer: readFileSync(avatarFile) };

      const forgedResponse = await uploadAvatarThroughAccountApi(page, forged, { kind: "session" });
      const wrongTypeResponse = await uploadAvatarThroughAccountApi(page, wrongType, { kind: "session" });
      const oversizedResponse = await uploadAvatarThroughAccountApi(page, oversizedAvatar, { kind: "session" });

      expect(forgedResponse.status).toBe(400);
      expect(forgedResponse.body).toContain(accountApiMessages.invalidImageContent);
      expect(wrongTypeResponse.status).toBe(400);
      expect(wrongTypeResponse.body).toContain(accountApiMessages.invalidImageType);
      expect(oversizedResponse.status).toBe(400);
      await expectNetworkErrors(context, [400]);
      expect(await getAvatarUrlThroughAccountApi(page)).toBe(savedAvatarUrl);
    })();

    await step("Post a valid image without a token and with another identity's token & verify antiforgery refuses both")(async () => {
      const anonymousContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
      const anonymousPage = await anonymousContext.newPage();
      await anonymousPage.goto(blazorPath("login"));
      const otherIdentityToken = await readAntiforgeryToken(anonymousPage);
      await anonymousContext.close();
      const valid = { name: "avatar.png", mimeType: "image/png", buffer: readFileSync(replacementAvatarFile) };

      const withoutTokenResponse = await uploadAvatarThroughAccountApi(page, valid, { kind: "none" });
      const otherTokenResponse = await uploadAvatarThroughAccountApi(page, valid, { kind: "other", token: otherIdentityToken });

      expect(withoutTokenResponse.status).toBe(400);
      expect(withoutTokenResponse.body).toContain(accountApiMessages.antiforgeryValidationFailed);
      expect(otherTokenResponse.status).toBe(400);
      expect(otherTokenResponse.body).toContain(accountApiMessages.antiforgeryValidationFailed);
      await expectNetworkErrors(context, [400]);
      expect(await getAvatarUrlThroughAccountApi(page)).toBe(savedAvatarUrl);
    })();

    // === PARTIAL SAVE ===
    await step("Save a new picture and title while the profile update fails & verify the partial-save message and the committed avatar")(async () => {
      await page.route("**/api/account/users/me", (route) =>
        route.request().method() === "PUT"
          ? route.fulfill({ status: 500, contentType: "application/problem+json", body: JSON.stringify({ title: "Internal Server Error", status: 500, detail: "Profile update failed." }) })
          : route.fallback()
      );
      uploadRequests.length = 0;
      await chooseAvatarFile(page, replacementAvatarFile);
      await expectAvatarPreview(page);
      await titleInput.fill("Partially saved");

      await saveButton.click();

      await expect(page.getByRole("alert").getByText(texts.profilePartiallySaved, { exact: true })).toBeVisible();
      await expect(page.getByRole("alert").getByText("Profile update failed.", { exact: true })).toBeVisible();
      await expectNetworkErrors(context, [500]);
      await expect(blazorToast(page, texts.profileUpdated)).toHaveCount(0);
      const committedAvatarUrl = await getAvatarUrlThroughAccountApi(page);
      expect(committedAvatarUrl).not.toBe(savedAvatarUrl);
      expect(committedAvatarUrl!.startsWith("/avatars/")).toBe(true);
      const profile = await sendAccountApiRequest(page, "GET", "/api/account/users/me");
      expect(JSON.parse(profile.body).title).not.toBe("Partially saved");
      await expect(titleInput).toHaveValue("Partially saved");
      savedAvatarUrl = committedAvatarUrl!;
    })();

    await step("Save again once the profile update works & verify only the profile is sent and the toast shows once")(async () => {
      await page.unroute("**/api/account/users/me");
      uploadRequests.length = 0;

      await saveButton.click();

      const toast = blazorToast(page, texts.profileUpdated);
      await expect(toast).toHaveCount(1);
      expect(uploadRequests).toEqual([]);
      const profile = await sendAccountApiRequest(page, "GET", "/api/account/users/me");
      expect(JSON.parse(profile.body)).toMatchObject({ title: "Partially saved", avatarUrl: savedAvatarUrl });
      await dismissBlazorToast(page, toast);
    })();

    // === CANCELLATION ===
    await step("Leave the page while a picture upload is in flight & verify the upload is discarded and the avatar kept")(async () => {
      const heldUploads: string[] = [];
      await page.route("**/api/account/users/me/update-avatar", (route) => {
        heldUploads.push(route.request().url());
      });
      await chooseAvatarFile(page, avatarFile);
      await expectAvatarPreview(page);
      await saveButton.click();
      await expect.poll(() => heldUploads.length).toBe(1);

      await page.getByRole("link", { name: texts.home, exact: true }).click();
      await expect(page.getByRole("alertdialog", { name: texts.unsavedChanges })).toBeVisible();
      const abortedUpload = page.waitForEvent("requestfailed", (request) => new URL(request.url()).pathname === "/api/account/users/me/update-avatar");
      await page.getByRole("button", { name: texts.leave }).click();

      await expectBlazorUrl(page, "app");
      await abortedUpload;
      await page.unrouteAll({ behavior: "ignoreErrors" });
      expect(await getAvatarUrlThroughAccountApi(page)).toBe(savedAvatarUrl);
      await gotoBlazor(page, "user/profile");
      await expect(avatarImage(avatarPickerButton(page))).toHaveAttribute("src", savedAvatarUrl);
      await expect(blazorToast(page, texts.profileUpdated)).toHaveCount(0);
      await expectNoPolicyViolations(page);
    })();
  });
});
