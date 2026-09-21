// Avatar upload and removal on the Blazor profile page, through the gateway against the running stack, in one browser and
// one culture (--culture en-US|da-DK). A new owner is signed up in that culture for the run.
//
// 1. Upload through the menu: "Change profile picture", then "Upload profile picture" opens the file dialog; the chosen
//    image shows as a blob: preview, Save uploads it before the profile PUT, shows the success toast once, and the stored
//    avatar is served through the gateway, shown in the header without a reload and in the users list.
// 2. Drop: an image dropped on the picker replaces the selection, and the earlier preview URL is revoked.
// 3. Client-side refusal: an image over 1 MB and a file of another type show their messages under the picker and send no
//    upload request.
// 4. API messages: a file that passes the client check but is not an image shows the API's validation message as returned
//    in the form alert, and a 413 without a body shows the size message.
// 5. Partial save: when the upload commits and the profile PUT fails, the form shows the API message and the partial-save
//    message without the success toast, the server holds the new avatar, and Save again sends only the PUT.
// 6. Removal: "Remove profile picture" shows the initials, Save removes the stored avatar and the header shows initials.
// 7. Reload guard: an edit typed on the first name, the last name or the title raises the browser's beforeunload prompt
//    whether the field was left or still has the focus, and dismissing the prompt keeps the typed text.
// 8. Typing cost: the edited fields commit as they are typed, so the keystroke's own task is measured and recorded.
// Every case asserts zero content security policy violations and no page errors, and the cases without an expected error
// response also no console errors.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started.
// Run: dotnet run --project developer-cli -- blazor-harness profile-avatar --browser all --culture da-DK

import { deflateSync, crc32 } from "node:zlib";
import { baseUrl, launchBrowser, newContext, observeErrors, parseArguments, pathBase, policyViolationsOf, probeHostConfiguration, signUpThroughBlazor, writeResult } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium", culture: "en-US" });
const profileUrl = `${baseUrl}${pathBase}/user/profile`;
const usersUrl = `${baseUrl}${pathBase}/account/users`;
const currentUserPath = "/api/account/users/me";
const interactiveTimeoutMs = 60_000;
const settleMs = 500;
const expectedCaseCount = 8;

const texts = {
  "en-US": {
    tooLarge: "Image must be smaller than 1 MB.",
    wrongType: "Please select a JPEG, PNG, GIF, or WebP image.",
    partial: "Your profile picture was saved, but your other changes were not. Try again."
  },
  "da-DK": {
    tooLarge: "Billedet skal være mindre end 1 MB.",
    wrongType: "Vælg et JPEG-, PNG-, GIF- eller WebP-billede.",
    partial: "Dit profilbillede blev gemt, men dine øvrige ændringer blev ikke gemt. Prøv igen."
  }
}[options.culture];
if (!texts) throw new Error(`Unsupported culture ${options.culture}.`);
const invalidImageMessage = "Image must be a valid JPEG, PNG, GIF, or WebP file.";

const browser = await launchBrowser(options.browser);
const hostConfiguration = await probeHostConfiguration(browser, options.browser);
const results = [];
const stamp = `${options.browser}-${options.culture}-${Date.now()}`;
const signedUp = await signUpThroughBlazor(browser, options.browser, `profile-avatar-${stamp}@example.com`, options.culture);

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

// A fresh signed-in page on the profile, with the picker attached and its requests to the avatar endpoints recorded
async function withProfile(action, { strictConsole = true } = {}) {
  const context = await newContext(browser, options.browser, signedUp.storageState, options.culture);
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
    const avatarRequests = [];
    page.on("request", (request) => {
      const path = new URL(request.url()).pathname;
      if (path.startsWith("/api/account/users/me") && request.method() !== "GET") avatarRequests.push(`${request.method()} ${path}`);
    });
    await page.goto(profileUrl, { waitUntil: "load" });
    await page.locator(testId("profile-form")).waitFor({ timeout: interactiveTimeoutMs });
    await page.waitForTimeout(settleMs);
    const lang = await page.locator("html").getAttribute("lang");
    assert(lang === options.culture, `The profile rendered in ${lang}, not ${options.culture}.`);
    let detail;
    try {
      detail = await action(page, context, avatarRequests);
    } catch (error) {
      const formErrors = await page.locator(testId("form-error-message")).allTextContents().catch(() => []);
      const consoleErrors = observations.consoleErrors.map((text) => text.replace(/\s+/g, " ").slice(0, 200));
      throw new Error(`${String(error.message).split("\n")[0]} (requests: ${avatarRequests.join(",")}; form errors: ${formErrors.join(" | ")}; console: ${consoleErrors.join(" | ")}; page errors: ${observations.pageErrors.join(" | ")})`);
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

// Server reads from inside the page, through the gateway with the page's own session cookies
async function readFromPage(page, path) {
  return page.evaluate(async (url) => {
    const response = await fetch(url, { cache: "no-store" });
    return { status: response.status, contentType: response.headers.get("content-type"), json: response.headers.get("content-type")?.includes("json") ? await response.json() : null };
  }, path);
}

async function currentUser(page) {
  const response = await readFromPage(page, currentUserPath);
  assert(response.status === 200, `Reading the current user returned ${response.status}.`);
  return response.json;
}

async function pickThroughMenu(page, file) {
  await page.locator(testId("avatar-menu-trigger")).click();
  const chooser = page.waitForEvent("filechooser", { timeout: 10_000 });
  await page.locator(testId("upload-avatar")).click();
  await (await chooser).setFiles(file);
}

async function drop(page, file) {
  const dataTransfer = await page.evaluateHandle(({ bytes, name, mimeType }) => {
    const transfer = new DataTransfer();
    transfer.items.add(new File([new Uint8Array(bytes)], name, { type: mimeType }));
    return transfer;
  }, { bytes: [...file.buffer], name: file.name, mimeType: file.mimeType });
  const zone = page.locator(testId("avatar-drop-zone"));
  await zone.dispatchEvent("dragover", { dataTransfer });
  await zone.dispatchEvent("drop", { dataTransfer });
}

const pickerImage = (page) => page.locator(`${testId("avatar-drop-zone")} img.user-avatar-image`);

async function previewUrl(page) {
  await page.waitForFunction((selector) => document.querySelector(selector)?.getAttribute("src")?.startsWith("blob:"), `${testId("avatar-drop-zone")} img.user-avatar-image`);
  return pickerImage(page).getAttribute("src");
}

async function saveAndExpectToast(page) {
  await page.locator(testId("save-profile")).click();
  await page.locator(testId("profile-updated-toast")).waitFor({ timeout: 15_000 });
}

await check("upload through the menu, preview, save, header and users list", () =>
  withProfile(async (page, context, avatarRequests) => {
    await pickThroughMenu(page, { name: "red.png", mimeType: "image/png", buffer: png(255, 0, 0) });
    const preview = await previewUrl(page);
    await saveAndExpectToast(page);
    assert((await page.locator(testId("profile-updated-toast")).count()) === 1, "The success toast was shown more than once.");
    assert(avatarRequests.join(",") === "POST /api/account/users/me/update-avatar,PUT /api/account/users/me", `Unexpected save order: ${avatarRequests.join(",")}`);
    const user = await currentUser(page);
    assert(user.avatarUrl?.startsWith("/avatars/"), `The server holds no avatar: ${user.avatarUrl}`);
    await page.waitForFunction((url) => document.querySelector("[data-testid=\"avatar-drop-zone\"] img")?.getAttribute("src") === url, user.avatarUrl);
    await page.locator(`.user-menu img.user-avatar-image[src="${user.avatarUrl}"]`).waitFor({ timeout: 15_000 });
    const served = await readFromPage(page, user.avatarUrl);
    assert(served.status === 200 && served.contentType === "image/png", `The avatar was served ${served.status} ${served.contentType}.`);
    await page.goto(usersUrl, { waitUntil: "load" });
    await page.locator(`img.user-avatar-image[src="${user.avatarUrl}"]`).first().waitFor({ timeout: interactiveTimeoutMs });
    return `preview ${preview.slice(0, 5)}, stored ${user.avatarUrl}, header and users list show it`;
  })
);

await check("drop replaces the selection and revokes the earlier preview", () =>
  withProfile(async (page) => {
    await drop(page, { name: "green.png", mimeType: "image/png", buffer: png(0, 255, 0) });
    const first = await previewUrl(page);
    await drop(page, { name: "blue.png", mimeType: "image/png", buffer: png(0, 0, 255) });
    await page.waitForFunction((url) => document.querySelector("[data-testid=\"avatar-drop-zone\"] img")?.getAttribute("src") !== url, first);
    const second = await previewUrl(page);
    const revoked = await page.evaluate(() => window.__revokedObjectUrls);
    assert(revoked.includes(first), "The replaced preview URL was not revoked.");
    assert(!revoked.includes(second), "The current preview URL was revoked.");
    return `${first.slice(0, 5)} replaced by ${second.slice(0, 5)}`;
  })
);

await check("oversized and wrong-type files refused client-side without a request", () =>
  withProfile(async (page, _context, avatarRequests) => {
    const oversized = Buffer.concat([png(255, 255, 0), Buffer.alloc(1024 * 1024)]);
    await drop(page, { name: "large.png", mimeType: "image/png", buffer: oversized });
    await page.locator(testId("avatar-error")).filter({ hasText: texts.tooLarge }).waitFor();
    await pickThroughMenu(page, { name: "notes.txt", mimeType: "text/plain", buffer: Buffer.from("not an image") });
    await page.locator(testId("avatar-error")).filter({ hasText: texts.wrongType }).waitFor();
    const shown = (await pickerImage(page).count()) === 0 ? null : await pickerImage(page).getAttribute("src");
    assert(!shown?.startsWith("blob:"), "A refused file was previewed.");
    await page.locator(testId("save-profile")).click();
    await page.locator(testId("profile-updated-toast")).waitFor({ timeout: 15_000 });
    assert(!avatarRequests.some((request) => request.includes("update-avatar")), `A refused file was sent: ${avatarRequests.join(",")}`);
    return "both messages shown, no upload request";
  })
);

await check("API validation message and a body-less 413 shown in the form alert", () =>
  withProfile(async (page) => {
    await drop(page, { name: "fake.png", mimeType: "image/png", buffer: Buffer.from("<html>not an image</html>") });
    await previewUrl(page);
    await page.locator(testId("save-profile")).click();
    await page.locator(testId("form-error-message")).filter({ hasText: invalidImageMessage }).waitFor({ timeout: 15_000 });
    assert((await page.locator(testId("profile-updated-toast")).count()) === 0, "A success toast was shown after a rejected upload.");

    await page.route("**/api/account/users/me/update-avatar", (route) => route.fulfill({ status: 413, body: "" }));
    await drop(page, { name: "pink.png", mimeType: "image/png", buffer: png(255, 0, 255) });
    await page.locator(testId("save-profile")).click();
    await page.locator(testId("form-error-message")).filter({ hasText: texts.tooLarge }).waitFor({ timeout: 15_000 });
    return "API message as returned, 413 as the size message";
  }, { strictConsole: false })
);

await check("partial save keeps the committed avatar and retries only the PUT", () =>
  withProfile(async (page, context, avatarRequests) => {
    const before = await currentUser(page);
    await page.route("**/api/account/users/me", (route) =>
      route.request().method() === "PUT"
        ? route.fulfill({ status: 500, contentType: "application/problem+json", body: JSON.stringify({ title: "Internal Server Error", status: 500, detail: "Harness failure." }) })
        : route.continue()
    );
    await drop(page, { name: "teal.png", mimeType: "image/png", buffer: png(0, 128, 128) });
    await previewUrl(page);
    await page.locator(testId("title")).fill(`Harness ${Date.now()}`);
    await page.locator(testId("save-profile")).click();
    await page.locator(testId("form-error-message")).filter({ hasText: texts.partial }).waitFor({ timeout: 15_000 });
    await page.locator(testId("form-error-message")).filter({ hasText: "Harness failure." }).waitFor();
    assert((await page.locator(testId("profile-updated-toast")).count()) === 0, "A success toast was shown after a partial save.");
    const afterFailure = await currentUser(page);
    assert(afterFailure.avatarUrl && afterFailure.avatarUrl !== before.avatarUrl, "The committed avatar is not on the server.");

    await page.unroute("**/api/account/users/me");
    avatarRequests.length = 0;
    await saveAndExpectToast(page);
    assert(avatarRequests.join(",") === "PUT /api/account/users/me", `The retry sent ${avatarRequests.join(",")}.`);
    return `avatar committed once, retry sent ${avatarRequests.join(",")}`;
  }, { strictConsole: false })
);

await check("remove the avatar, save, initials in the header", () =>
  withProfile(async (page, context, avatarRequests) => {
    const before = await currentUser(page);
    assert(before.avatarUrl, "The earlier cases left no avatar to remove.");
    await page.locator(testId("avatar-menu-trigger")).click();
    await page.locator(testId("remove-avatar")).click();
    await page.waitForFunction(() => !document.querySelector("[data-testid=\"avatar-drop-zone\"] img"));
    await saveAndExpectToast(page);
    assert(avatarRequests.join(",") === "DELETE /api/account/users/me/remove-avatar,PUT /api/account/users/me", `Unexpected save order: ${avatarRequests.join(",")}`);
    assert((await currentUser(page)).avatarUrl === null, "The server still holds an avatar.");
    await page.waitForFunction(() => !document.querySelector(".user-menu img.user-avatar-image"), undefined, { timeout: 15_000 });
    return "removed on the server, initials in picker and header";
  })
);

// One cell of the reload table: a fresh page, an edit appended to the field, either left or still holding the focus,
// then a document reload. The cell records whether the browser asked before it discarded the edit and whether the edit
// survived a dismissed prompt. A page of its own per cell, so the field is clean when the cell starts.
async function reloadAfterEdit(fieldTestId, { keepFocus }) {
  return withProfile(async (page) => {
    const field = page.locator(testId(fieldTestId));
    const original = await field.inputValue();
    const dialogs = [];
    page.on("dialog", async (dialog) => {
      dialogs.push(dialog.type());
      await dialog.dismiss();
    });
    await field.click();
    await field.press("End");
    await field.pressSequentially("Zed");
    if (!keepFocus) await field.press("Tab");
    // The dirty state reaches the browser's listeners on the render that follows the commit
    await page.waitForTimeout(settleMs);
    await page.evaluate(() => setTimeout(() => location.reload(), 0));
    await page.waitForTimeout(2_000);
    return { asked: dialogs.length === 1 && dialogs[0] === "beforeunload", kept: (await field.inputValue()) === `${original}Zed`, dialogs };
  });
}

// The cost of one keystroke on a field that commits as it is typed: the milliseconds the input event's own task takes,
// timed from before the framework's delegated listener to the next macrotask, which covers the render the keystroke
// causes. The first keystroke is the warm-up. The number is recorded with its conditions rather than turned into a
// budget: a timing budget is set on the trimmed Release publish (the measurements rule), and the harness runs this
// against whichever host it was pointed at. The ceiling asserted is the perceptual one, a keystroke a user would see
// lag behind, not a baseline figure.
const keystrokeCeilingMs = 100;

async function measureKeystrokeCost(page, fieldTestId, text) {
  const field = page.locator(testId(fieldTestId));
  await field.click();
  await field.press("End");
  await page.evaluate((id) => {
    window.__keystrokeCosts = [];
    document.querySelector(`[data-testid="${id}"]`).addEventListener("input", () => {
      const started = performance.now();
      setTimeout(() => window.__keystrokeCosts.push(performance.now() - started), 0);
    });
  }, fieldTestId);
  await field.pressSequentially(text, { delay: 60 });
  await page.waitForTimeout(settleMs);
  return page.evaluate((expected) => {
    const observed = window.__keystrokeCosts.length;
    const samples = window.__keystrokeCosts.slice(1).sort((first, second) => first - second);
    const round = (value) => Math.round(value * 10) / 10;
    return {
      observed,
      expected,
      samples: samples.length,
      median: round(samples[Math.floor(samples.length / 2)]),
      min: round(samples[0]),
      max: round(samples[samples.length - 1])
    };
  }, text.length);
}

await check("an edit typed on a profile field is guarded on a reload whether or not the field was left", async () => {
  const cells = [];
  for (const field of ["first-name", "last-name", "title"]) {
    for (const keepFocus of [false, true]) {
      cells.push({ field, state: keepFocus ? "typed with the focus kept" : "typed and committed", ...(await reloadAfterEdit(field, { keepFocus })) });
    }
  }

  const table = cells.map((cell) => `${cell.field} ${cell.state}: ${cell.asked ? "asks" : "silent"}, the edit was ${cell.kept ? "kept" : "lost"}`).join("; ");
  assert(cells.every((cell) => cell.asked && cell.kept), `A reload discarded an edit without asking: ${table}`);
  return table;
});

await check("typing on the first name costs one keystroke's render and stays under the perceptual ceiling", () =>
  withProfile(async (page) => {
    const cost = await measureKeystrokeCost(page, "first-name", "Typing cost sample");
    assert(cost.observed === cost.expected, `${cost.observed} of ${cost.expected} keystrokes reached the field as input events.`);
    assert(cost.median < keystrokeCeilingMs, `The median keystroke took ${cost.median} ms, over the ${keystrokeCeilingMs} ms ceiling.`);
    return `median ${cost.median} ms, min ${cost.min} ms, max ${cost.max} ms over ${cost.samples} keystrokes after one warm-up`;
  })
);

await browser.close();

const verdict = writeResult(`profile-avatar-${options.browser}-${options.culture}.json`, { browser: options.browser, culture: options.culture, ...hostConfiguration, results }, expectedCaseCount);
console.log(`Result file: ${verdict.resultFile}`);
if (!verdict.passed) process.exit(1);
