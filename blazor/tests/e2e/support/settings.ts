import { expect, type Locator, type Page } from "@playwright/test";
import { type AccountApiResponse, sendAccountApiRequest } from "./account-api";
import { gotoBlazor } from "./routes";
import { blazorTexts } from "./texts";

/**
 * An account as the account API returns it for the settings page
 */
export interface AccountApiTenant {
  id: string;
  name: string;
  logoUrl: string | null;
}

/**
 * A file the logo upload is sent: its name, declared content type and bytes
 */
export interface LogoUploadFile {
  name: string;
  mimeType: string;
  buffer: Buffer;
}

/**
 * Open the account settings page and wait for the loaded account, which the page renders in the browser
 * @param page Playwright page instance of a signed-in user
 */
export async function gotoAccountSettingsPage(page: Page): Promise<void> {
  await gotoBlazor(page, "account/settings");

  await expect(page.getByRole("heading", { name: blazorTexts().accountSettings, exact: true })).toBeVisible();
  await expect(accountNameInput(page)).toBeVisible();
}

/**
 * The account name field, editable for the owner and read-only for everyone else
 * @param page Playwright page instance on the account settings page
 */
export function accountNameInput(page: Page): Locator {
  return page.getByLabel(blazorTexts().accountName, { exact: true });
}

/**
 * The Save changes button, rendered only for the owner
 * @param page Playwright page instance on the account settings page
 */
export function saveAccountSettingsButton(page: Page): Locator {
  return page.getByRole("button", { name: blazorTexts().saveChanges, exact: true });
}

/**
 * The logo picker's trigger, named "Change logo"; rendered only for the owner
 * @param page Playwright page instance on the account settings page
 */
export function logoPickerButton(page: Page): Locator {
  return page.getByRole("button", { name: blazorTexts().changeLogo, exact: true });
}

/**
 * The account logo shown inside a control; the image is decorative, so it has no role and is found inside the named control
 * @param container The control the logo sits in
 */
export function logoImage(container: Locator): Locator {
  return container.locator("img");
}

/**
 * Open the logo picker's menu and choose "Upload logo", answering the file dialog with the given file. The item is clicked
 * rather than dispatched, because the browser opens a file dialog only for a real user activation.
 * @param page Playwright page instance on the account settings page as the owner
 * @param file The path of the file to choose
 */
export async function chooseLogoFile(page: Page, file: string): Promise<void> {
  const texts = blazorTexts();
  await logoPickerButton(page).click();
  const menu = page.getByRole("menu", { name: texts.changeLogo, exact: true });
  await expect(menu).toBeVisible();
  const fileChooser = page.waitForEvent("filechooser");

  await menu.getByRole("menuitem", { name: texts.uploadLogo, exact: true }).click();

  await (await fileChooser).setFiles(file);
  await expect(menu).toBeHidden();
}

/**
 * Open the logo picker's menu and choose "Remove logo", which drops the shown logo until the form is saved
 * @param page Playwright page instance on the account settings page as the owner, with a logo shown
 */
export async function removeLogoThroughPicker(page: Page): Promise<void> {
  const texts = blazorTexts();
  await logoPickerButton(page).click();
  const menu = page.getByRole("menu", { name: texts.changeLogo, exact: true });
  await expect(menu).toBeVisible();

  await menu.getByRole("menuitem", { name: texts.removeLogo, exact: true }).dispatchEvent("click");

  await expect(menu).toBeHidden();
}

/**
 * The signed-in user's account as the account API returns it, the server's own record of the name and the logo
 * @param page Playwright page instance of a signed-in user
 */
export async function readCurrentTenantThroughAccountApi(page: Page): Promise<AccountApiTenant> {
  const response = await sendAccountApiRequest(page, "GET", "/api/account/tenants/current");
  expect(response.status, response.body).toBe(200);
  return JSON.parse(response.body) as AccountApiTenant;
}

/**
 * Rename the account through the account API and return the response, which the caller asserts
 * @param page Playwright page instance of a signed-in user
 * @param name The name to send
 */
export function updateTenantNameThroughAccountApi(page: Page, name: string): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "PUT", "/api/account/tenants/current", { name });
}

/**
 * Remove the account logo through the account API and return the response, which the caller asserts
 * @param page Playwright page instance of a signed-in user
 */
export function removeTenantLogoThroughAccountApi(page: Page): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "DELETE", "/api/account/tenants/current/remove-logo");
}

/**
 * Post a file to the logo upload endpoint as a browser multipart form from the page's document, with the session cookies
 * and the antiforgery token the bootstrap issues, bypassing the Blazor picker's client-side checks
 * @param page Playwright page instance of a signed-in user, on a page of the gateway's origin
 * @param file The file to send in the "file" field
 */
export function uploadTenantLogoThroughAccountApi(page: Page, file: LogoUploadFile): Promise<AccountApiResponse> {
  return page.evaluate(
    async ({ name, mimeType, bytes }) => {
      const bootstrap = await fetch("/api/account/bootstrap", { credentials: "same-origin" });
      const { antiforgeryToken } = (await bootstrap.json()) as { antiforgeryToken: string };
      const form = new FormData();
      form.append("file", new File([new Uint8Array(bytes)], name, { type: mimeType }));
      const response = await fetch("/api/account/tenants/current/update-logo", {
        method: "POST",
        credentials: "same-origin",
        headers: { "x-xsrf-token": antiforgeryToken },
        body: form
      });
      return { status: response.status, body: await response.text() };
    },
    { name: file.name, mimeType: file.mimeType, bytes: [...file.buffer] }
  );
}
