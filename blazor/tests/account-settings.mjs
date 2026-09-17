// Account settings on the Blazor account settings page, through the gateway against the running stack, in one browser and
// one culture (--culture en-US|da-DK). A new owner is signed up for the run and invites an admin and a member.
//
// 1. Rename and upload through the menu: "Change logo", then "Upload logo" opens the file dialog; the chosen image shows
//    as a blob: preview, Save uploads it before the name PUT, shows the success toast once, the stored logo is served
//    through the gateway, and the account name in the shell changes without a reload.
// 2. Drop: an image dropped on the picker replaces the selection, and the earlier preview URL is revoked.
// 3. Client-side refusal: an image over 2 MB and a file of another type show their messages under the picker and send no
//    upload request.
// 4. API messages: a file that passes the client check but is not an image shows the API's validation message as returned
//    in the form alert, and a 413 without a body shows the size message.
// 5. Partial save: when the upload commits and the name PUT fails, the form shows the API message and the partial-save
//    message without the success toast, the server holds the new logo, and Save again sends only the PUT.
// 6. Removal: "Remove logo" shows the initials, Save removes the stored logo and the server holds none.
// 7. Guard: an unsaved name blocks an in-app link with the "Unsaved changes" dialog, and Stay keeps the edit.
// 8. Admin: the read-only page with the explanatory text, no Save and no danger zone, and the account API refuses the
//    name PUT and the logo upload made directly from the page.
// 9. Member: the same read-only page, and the account API refuses the same two calls.
// 10. Danger zone: "Delete account" opens the notice dialog with the support address read-only, and Close dismisses it.
// Every case asserts zero content security policy violations and no page errors, and the cases without an expected error
// response also no console errors.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started.
// Run: dotnet run --project developer-cli -- blazor-harness account-settings --browser all --culture da-DK

import { deflateSync, crc32 } from "node:zlib";
import {
  baseUrl,
  launchBrowser,
  newContext,
  observeErrors,
  parseArguments,
  pathBase,
  policyViolationsOf,
  probeHostConfiguration,
  readOneTimePassword,
  signUpThroughBlazor,
  submitOneTimePasswordThroughBlazor,
  writeResult
} from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium", culture: "en-US" });
const settingsUrl = `${baseUrl}${pathBase}/account/settings`;
const currentTenantPath = "/api/account/tenants/current";
const updateLogoPath = "/api/account/tenants/current/update-logo";
const interactiveTimeoutMs = 60_000;
const settleMs = 500;
const expectedCaseCount = 10;

const texts = {
  "en-US": {
    tooLarge: "Image must be smaller than 2 MB.",
    wrongType: "Please select a JPEG, PNG, GIF, or WebP image.",
    partial: "Your account logo was saved, but your other changes were not. Try again.",
    readOnly: "Only account owners can modify the account name",
    dangerZone: "Danger zone",
    deleteAccount: "Delete account",
    deleteNotice: "To delete your account, please contact our support team."
  },
  "da-DK": {
    tooLarge: "Billedet skal være mindre end 2 MB.",
    wrongType: "Vælg et JPEG-, PNG-, GIF- eller WebP-billede.",
    partial: "Dit kontologo blev gemt, men dine øvrige ændringer blev ikke gemt. Prøv igen.",
    readOnly: "Kun kontoejere kan ændre kontonavnet",
    dangerZone: "Farezone",
    deleteAccount: "Slet konto",
    deleteNotice: "For at slette din konto bedes du kontakte vores supportteam."
  }
}[options.culture];
if (!texts) throw new Error(`Unsupported culture ${options.culture}.`);
const invalidImageMessage = "Image must be a valid JPEG, PNG, GIF, or WebP file.";
const ownerOnlyNameMessage = "Only owners are allowed to update tenant information.";
const ownerOnlyLogoMessage = "Only owners are allowed to update tenant logo.";

const browser = await launchBrowser(options.browser);
const hostConfiguration = await probeHostConfiguration(browser, options.browser);
const results = [];
// Lower case throughout: the account API stores an email lower cased, so a mixed-case address would not match back
const stamp = `${options.browser}-${options.culture}-${Date.now()}`.toLowerCase();
console.log("Signing up the owner...");
const signedUp = await signUpThroughBlazor(browser, options.browser, `account-settings-${stamp}@example.com`, options.culture);
console.log("Owner signed up.");

async function check(name, action) {
  try {
    const detail = await action();
    results.push({ name, passed: true, detail });
    console.log(`PASS ${name}${detail ? `: ${detail}` : ""}`);
  } catch (error) {
    // The first line only: a request failure's call log carries the session cookies
    const message = String(error.message).split("\n")[0];
    results.push({ name, passed: false, detail: message });
    console.log(`FAIL ${name}: ${message}`);
  }
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

const testId = (id) => `[data-testid="${id}"]`;

// A valid PNG of one solid color, with real chunk CRCs so the API's content inspection accepts it
function png(red, green, blue, size = 4) {
  const chunk = (type, data) => {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const typeAndData = Buffer.concat([Buffer.from(type, "ascii"), data]);
    const checksum = Buffer.alloc(4);
    checksum.writeUInt32BE(crc32(typeAndData));
    return Buffer.concat([length, typeAndData, checksum]);
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8;
  header[9] = 2;
  const row = Buffer.concat([Buffer.from([0]), Buffer.from(Array.from({ length: size }, () => [red, green, blue]).flat())]);
  const pixels = deflateSync(Buffer.concat(Array.from({ length: size }, () => row)));
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk("IHDR", header), chunk("IDAT", pixels), chunk("IEND", Buffer.alloc(0))]);
}

// A request to the account API from the page's document, with the page's session cookies and the antiforgery token the
// bootstrap endpoint issues for that session, exactly as the client's handler chain sends it
function sendAccountApiRequest(page, method, path, data) {
  return page.evaluate(
    async ({ method, path, data }) => {
      const bootstrap = await fetch("/api/account/bootstrap", { credentials: "same-origin" });
      const { antiforgeryToken } = await bootstrap.json();
      const response = await fetch(path, {
        method,
        credentials: "same-origin",
        headers: { "content-type": "application/json", "x-xsrf-token": antiforgeryToken },
        body: data === undefined ? undefined : JSON.stringify(data)
      });
      return { status: response.status, body: await response.text() };
    },
    { method, path, data }
  );
}

// A multipart logo upload from the page's document with the antiforgery token, the way the picker sends it
function sendLogoUploadRequest(page, bytes) {
  return page.evaluate(
    async ({ bytes, path }) => {
      const bootstrap = await fetch("/api/account/bootstrap", { credentials: "same-origin" });
      const { antiforgeryToken } = await bootstrap.json();
      const form = new FormData();
      form.append("file", new File([new Uint8Array(bytes)], "logo.png", { type: "image/png" }));
      const response = await fetch(path, { method: "POST", credentials: "same-origin", headers: { "x-xsrf-token": antiforgeryToken }, body: form });
      return { status: response.status, body: await response.text() };
    },
    { bytes: [...png(1, 2, 3)], path: updateLogoPath }
  );
}

// A signed-in page on the account settings, with the requests it sends to the tenant endpoints recorded
async function withSettings(storageState, action, { strictConsole = true } = {}) {
  const context = await newContext(browser, options.browser, storageState, options.culture);
  try {
    // Records every revoked object URL, since a browser may still show a revoked image from its image cache
    await context.addInitScript(() => {
      window.__revokedObjectUrls = [];
      const revokeObjectUrl = URL.revokeObjectURL.bind(URL);
      URL.revokeObjectURL = (url) => {
        window.__revokedObjectUrls.push(url);
        revokeObjectUrl(url);
      };
    });
    const page = await context.newPage();
    const observations = observeErrors(page);
    const tenantRequests = [];
    page.on("request", (request) => {
      const path = new URL(request.url()).pathname;
      if (path.startsWith("/api/account/tenants/current") && request.method() !== "GET") tenantRequests.push(`${request.method()} ${path}`);
    });
    await page.goto(settingsUrl, { waitUntil: "load" });
    await page.locator(testId("account-settings-form")).waitFor({ timeout: interactiveTimeoutMs });
    await page.waitForTimeout(settleMs);
    const lang = await page.locator("html").getAttribute("lang");
    assert(lang === options.culture, `The account settings rendered in ${lang}, not ${options.culture}.`);
    let detail;
    try {
      detail = await action(page, context, tenantRequests);
    } catch (error) {
      const formErrors = await page.locator(testId("form-error-message")).allTextContents().catch(() => []);
      const consoleErrors = observations.consoleErrors.map((text) => text.replace(/\s+/g, " ").slice(0, 200));
      throw new Error(`${String(error.message).split("\n")[0]} (requests: ${tenantRequests.join(",")}; form errors: ${formErrors.join(" | ")}; console: ${consoleErrors.join(" | ")}; page errors: ${observations.pageErrors.join(" | ")})`);
    }
    const violations = policyViolationsOf(context);
    assert(violations.length === 0, `${violations.length} content security policy violations: ${JSON.stringify(violations)}`);
    assert(observations.pageErrors.length === 0, `Page errors: ${observations.pageErrors.join(" | ")}`);
    if (strictConsole) assert(observations.consoleErrors.length === 0, `Console errors: ${observations.consoleErrors.join(" | ")}`);
    return detail;
  } finally {
    await context.close();
  }
}

const withOwner = (action, settings) => withSettings(signedUp.storageState, action, settings);

// Server reads from inside the page, through the gateway with the page's own session cookies
async function readFromPage(page, path) {
  return page.evaluate(async (url) => {
    const response = await fetch(url, { cache: "no-store" });
    return { status: response.status, contentType: response.headers.get("content-type"), json: response.headers.get("content-type")?.includes("json") ? await response.json() : null };
  }, path);
}

async function currentTenant(page) {
  const response = await readFromPage(page, currentTenantPath);
  assert(response.status === 200, `Reading the current account returned ${response.status}.`);
  return response.json;
}

async function pickThroughMenu(page, file) {
  await page.locator(testId("logo-menu-trigger")).click();
  const chooser = page.waitForEvent("filechooser", { timeout: 10_000 });
  await page.locator(testId("upload-logo")).click();
  await (await chooser).setFiles(file);
}

async function drop(page, file) {
  const dataTransfer = await page.evaluateHandle(({ bytes, name, mimeType }) => {
    const transfer = new DataTransfer();
    transfer.items.add(new File([new Uint8Array(bytes)], name, { type: mimeType }));
    return transfer;
  }, { bytes: [...file.buffer], name: file.name, mimeType: file.mimeType });
  const zone = page.locator(testId("logo-drop-zone"));
  await zone.dispatchEvent("dragover", { dataTransfer });
  await zone.dispatchEvent("drop", { dataTransfer });
}

const pickerImage = (page) => page.locator(`${testId("logo-drop-zone")} img.tenant-logo-image`);

async function previewUrl(page) {
  await page.waitForFunction((selector) => document.querySelector(selector)?.getAttribute("src")?.startsWith("blob:"), `${testId("logo-drop-zone")} img.tenant-logo-image`);
  return pickerImage(page).getAttribute("src");
}

async function saveAndExpectToast(page) {
  await page.locator(testId("save-account-settings")).click();
  await page.locator(testId("account-settings-updated-toast")).waitFor({ timeout: 15_000 });
}

// Signs an invited user in through the Blazor login page with the mailed code and completes the profile step of the
// welcome setup, which is all an invited user is asked for; returns the signed-in storage state
async function logInInvitedUser(email, lastName) {
  const context = await newContext(browser, options.browser, undefined, options.culture);
  try {
    const page = await context.newPage();
    await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
    await page.locator(testId("email")).fill(email);
    const sentAfter = Date.now();
    await page.locator(testId("submit")).click();
    await page.waitForURL(/\/blazor\/login\/verify\?/);
    console.log(`  verification page reached for ${email}`);
    const oneTimePassword = await readOneTimePassword(email, sentAfter);
    console.log("  one-time password read");
    await submitOneTimePasswordThroughBlazor(page, oneTimePassword);
    await page.waitForURL(/\/blazor\/welcome/);
    console.log("  welcome page reached");
    await page.locator(testId("first-name")).fill("Harness");
    await page.locator(testId("last-name")).fill(lastName);
    await page.locator(testId("continue")).click();
    await page.waitForURL(`${baseUrl}${pathBase}/app`);
    // Awaited here, not returned: the finally below closes the context, which would cancel a pending read
    const storageState = await context.storageState();
    return storageState;
  } finally {
    await context.close();
  }
}

// The read-only page every non-owner sees, and the account API's own refusal of the two writes it hides
async function expectReadOnlySettings(storageState, role) {
  return withSettings(storageState, async (page) => {
    await page.locator(testId("account-name-description")).filter({ hasText: texts.readOnly }).waitFor();
    assert(await page.locator(testId("account-name")).evaluate((element) => element.readOnly), "The account name is editable.");
    assert((await page.locator(testId("save-account-settings")).count()) === 0, "The Save button is shown.");
    assert((await page.locator(testId("danger-zone")).count()) === 0, "The danger zone is shown.");
    assert((await page.locator(testId("logo-menu-trigger")).count()) === 0, "The logo picker is shown.");
    assert((await page.locator(testId("logo-readonly")).count()) === 1, "The logo is not shown.");

    const before = await currentTenant(page);
    const rename = await sendAccountApiRequest(page, "PUT", currentTenantPath, { name: `Forced by ${role}` });
    assert(rename.status === 403 && rename.body.includes(ownerOnlyNameMessage), `The rename returned ${rename.status}: ${rename.body}`);
    const upload = await sendLogoUploadRequest(page);
    assert(upload.status === 403 && upload.body.includes(ownerOnlyLogoMessage), `The logo upload returned ${upload.status}: ${upload.body}`);
    const after = await currentTenant(page);
    assert(after.name === before.name && after.logoUrl === before.logoUrl, "A refused write changed the account.");
    return `${role}: read-only page, both writes refused 403`;
  }, { strictConsole: false });
}

const adminEmail = `admin-account-settings-${stamp}@example.com`;
const memberEmail = `member-account-settings-${stamp}@example.com`;

// The invitations and the role change happen through the account API from the owner's own page, because the Blazor
// edition's invite dialog covers only the users page
const invited = await withOwner(async (page) => {
  for (const email of [adminEmail, memberEmail]) {
    const response = await sendAccountApiRequest(page, "POST", "/api/account/users/invite", { email });
    assert(response.status === 200, `Inviting ${email} returned ${response.status}: ${response.body}`);
    console.log(`Invited ${email}.`);
  }
  const found = await sendAccountApiRequest(page, "GET", `/api/account/users?Search=${encodeURIComponent(adminEmail)}`);
  assert(found.status === 200, `Searching for the invited admin returned ${found.status}: ${found.body}`);
  const admin = JSON.parse(found.body).users.find((user) => user.email === adminEmail);
  assert(admin !== undefined, `No invited user with email ${adminEmail}.`);
  const promoted = await sendAccountApiRequest(page, "PUT", `/api/account/users/${admin.id}/change-user-role`, { userRole: "Admin" });
  assert(promoted.status === 200, `Promoting the admin returned ${promoted.status}: ${promoted.body}`);
  return { adminId: admin.id };
});
console.log("Invitations done; signing the admin in...");
const adminState = await logInInvitedUser(adminEmail, "Admin");
console.log("Admin signed in; signing the member in...");
const memberState = await logInInvitedUser(memberEmail, "Member");
console.log("Member signed in; running the cases...");

await check("rename and upload through the menu, preview, save, served logo and the shell name", () =>
  withOwner(async (page, context, tenantRequests) => {
    const name = `Acme ${Date.now() % 100000}`;
    await pickThroughMenu(page, { name: "red.png", mimeType: "image/png", buffer: png(255, 0, 0) });
    const preview = await previewUrl(page);
    await page.locator(testId("account-name")).fill(name);
    await saveAndExpectToast(page);
    assert((await page.locator(testId("account-settings-updated-toast")).count()) === 1, "The success toast was shown more than once.");
    assert(tenantRequests.join(",") === `POST ${updateLogoPath},PUT ${currentTenantPath}`, `Unexpected save order: ${tenantRequests.join(",")}`);
    const tenant = await currentTenant(page);
    assert(tenant.name === name, `The server holds the name ${tenant.name}.`);
    assert(tenant.logoUrl?.startsWith("/logos/"), `The server holds no logo: ${tenant.logoUrl}`);
    await page.waitForFunction((url) => document.querySelector("[data-testid=\"logo-drop-zone\"] img")?.getAttribute("src") === url, tenant.logoUrl);
    await page.locator("#shell-tenant-name").filter({ hasText: name }).waitFor({ timeout: 15_000 });
    const served = await readFromPage(page, tenant.logoUrl);
    assert(served.status === 200 && served.contentType === "image/png", `The logo was served ${served.status} ${served.contentType}.`);
    return `preview ${preview.slice(0, 5)}, stored ${tenant.logoUrl}, shell shows ${name}`;
  })
);

await check("drop replaces the selection and revokes the earlier preview", () =>
  withOwner(async (page) => {
    await drop(page, { name: "green.png", mimeType: "image/png", buffer: png(0, 255, 0) });
    const first = await previewUrl(page);
    await drop(page, { name: "blue.png", mimeType: "image/png", buffer: png(0, 0, 255) });
    await page.waitForFunction((url) => document.querySelector("[data-testid=\"logo-drop-zone\"] img")?.getAttribute("src") !== url, first);
    const second = await previewUrl(page);
    const revoked = await page.evaluate(() => window.__revokedObjectUrls);
    assert(revoked.includes(first), "The replaced preview URL was not revoked.");
    assert(!revoked.includes(second), "The current preview URL was revoked.");
    return `${first.slice(0, 5)} replaced by ${second.slice(0, 5)}`;
  })
);

await check("oversized and wrong-type files refused client-side without a request", () =>
  withOwner(async (page, _context, tenantRequests) => {
    const oversized = Buffer.concat([png(255, 255, 0), Buffer.alloc(2 * 1024 * 1024)]);
    await drop(page, { name: "large.png", mimeType: "image/png", buffer: oversized });
    await page.locator(testId("logo-error")).filter({ hasText: texts.tooLarge }).waitFor();
    await pickThroughMenu(page, { name: "notes.txt", mimeType: "text/plain", buffer: Buffer.from("not an image") });
    await page.locator(testId("logo-error")).filter({ hasText: texts.wrongType }).waitFor();
    const shown = (await pickerImage(page).count()) === 0 ? null : await pickerImage(page).getAttribute("src");
    assert(!shown?.startsWith("blob:"), "A refused file was previewed.");
    await saveAndExpectToast(page);
    assert(!tenantRequests.some((request) => request.includes("update-logo")), `A refused file was sent: ${tenantRequests.join(",")}`);
    return "both messages shown, no upload request";
  })
);

await check("API validation message and a body-less 413 shown in the form alert", () =>
  withOwner(async (page) => {
    await drop(page, { name: "fake.png", mimeType: "image/png", buffer: Buffer.from("<html>not an image</html>") });
    await previewUrl(page);
    await page.locator(testId("save-account-settings")).click();
    await page.locator(testId("form-error-message")).filter({ hasText: invalidImageMessage }).waitFor({ timeout: 15_000 });
    assert((await page.locator(testId("account-settings-updated-toast")).count()) === 0, "A success toast was shown after a rejected upload.");

    await page.route(`**${updateLogoPath}`, (route) => route.fulfill({ status: 413, body: "" }));
    await drop(page, { name: "pink.png", mimeType: "image/png", buffer: png(255, 0, 255) });
    await page.locator(testId("save-account-settings")).click();
    await page.locator(testId("form-error-message")).filter({ hasText: texts.tooLarge }).waitFor({ timeout: 15_000 });
    return "API message as returned, 413 as the size message";
  }, { strictConsole: false })
);

await check("partial save keeps the committed logo and retries only the name", () =>
  withOwner(async (page, context, tenantRequests) => {
    const before = await currentTenant(page);
    await page.route(`**${currentTenantPath}`, (route) =>
      route.request().method() === "PUT"
        ? route.fulfill({ status: 500, contentType: "application/problem+json", body: JSON.stringify({ title: "Internal Server Error", status: 500, detail: "Harness failure." }) })
        : route.continue()
    );
    await drop(page, { name: "teal.png", mimeType: "image/png", buffer: png(0, 128, 128) });
    await previewUrl(page);
    await page.locator(testId("account-name")).fill(`Harness ${Date.now() % 100000}`);
    await page.locator(testId("save-account-settings")).click();
    await page.locator(testId("form-error-message")).filter({ hasText: texts.partial }).waitFor({ timeout: 15_000 });
    await page.locator(testId("form-error-message")).filter({ hasText: "Harness failure." }).waitFor();
    assert((await page.locator(testId("account-settings-updated-toast")).count()) === 0, "A success toast was shown after a partial save.");
    const afterFailure = await currentTenant(page);
    assert(afterFailure.logoUrl && afterFailure.logoUrl !== before.logoUrl, "The committed logo is not on the server.");

    await page.unroute(`**${currentTenantPath}`);
    tenantRequests.length = 0;
    await saveAndExpectToast(page);
    assert(tenantRequests.join(",") === `PUT ${currentTenantPath}`, `The retry sent ${tenantRequests.join(",")}.`);
    return `logo committed once, retry sent ${tenantRequests.join(",")}`;
  }, { strictConsole: false })
);

await check("remove the logo, save, initials in the picker", () =>
  withOwner(async (page, context, tenantRequests) => {
    const before = await currentTenant(page);
    assert(before.logoUrl, "The earlier cases left no logo to remove.");
    await page.locator(testId("logo-menu-trigger")).click();
    await page.locator(testId("remove-logo")).click();
    await page.waitForFunction(() => !document.querySelector("[data-testid=\"logo-drop-zone\"] img"));
    await saveAndExpectToast(page);
    assert(tenantRequests.join(",") === `DELETE /api/account/tenants/current/remove-logo,PUT ${currentTenantPath}`, `Unexpected save order: ${tenantRequests.join(",")}`);
    assert((await currentTenant(page)).logoUrl === null, "The server still holds a logo.");
    return "removed on the server, initials in the picker";
  })
);

await check("the unsaved-changes guard blocks an in-app link and Stay keeps the edit", () =>
  withOwner(async (page) => {
    const edited = `Guarded ${Date.now() % 100000}`;
    await page.locator(testId("account-name")).fill(edited);
    await page.locator(`a.shell-nav-link[href="${pathBase}/user/profile"]`).first().click();
    const dialog = page.locator(`dialog${testId("unsaved-changes-dialog")}[open]`);
    await dialog.waitFor({ timeout: 15_000 });
    await dialog.locator(testId("unsaved-changes-stay")).click();
    await dialog.waitFor({ state: "hidden" });
    assert(page.url() === settingsUrl, `Stay navigated to ${page.url()}.`);
    assert((await page.locator(testId("account-name")).inputValue()) === edited, "The edit was not kept.");
    return "dialog opened, Stay kept the page and the edit";
  })
);

await check("an admin sees the read-only page and the account API refuses both writes", () => expectReadOnlySettings(adminState, "Admin"));

await check("a member sees the read-only page and the account API refuses both writes", () => expectReadOnlySettings(memberState, "Member"));

await check("the danger zone opens the delete-account notice and closes it", () =>
  withOwner(async (page) => {
    await page.locator(testId("danger-zone")).filter({ hasText: texts.dangerZone }).waitFor();
    await page.locator(testId("delete-account")).click();
    const dialog = page.locator(`dialog${testId("delete-account-dialog")}[open]`);
    await dialog.waitFor({ timeout: 15_000 });
    assert(await dialog.evaluate((element) => element.matches(":modal")), "The delete account dialog is not modal.");
    await dialog.filter({ hasText: texts.deleteNotice }).waitFor();
    assert(await dialog.locator(testId("delete-account-support-email")).evaluate((element) => element.readOnly), "The support address is editable.");
    await dialog.locator(testId("delete-account-close")).click();
    await dialog.waitFor({ state: "hidden" });
    assert(page.url() === settingsUrl, `Closing the notice navigated to ${page.url()}.`);
    return "notice shown with a read-only support address, closed without leaving";
  })
);

await browser.close();

const verdict = writeResult(`account-settings-${options.browser}-${options.culture}.json`, { browser: options.browser, culture: options.culture, invitedAdmin: invited.adminId, ...hostConfiguration, results }, expectedCaseCount);
console.log(`Result file: ${verdict.resultFile}`);
if (!verdict.passed) process.exit(1);
