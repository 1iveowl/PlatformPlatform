// Unsaved-changes guard and dirty dialog of the Blazor edition, on the Development-only forms fixture, in one browser.
//
// The fixture page /blazor/development/form-errors/interactive is server-rendered and hosts the WebAssembly fixture
// component. Every case starts from a fresh page and makes the page dirty by typing, which is also the user gesture
// browsers require before they show a beforeunload prompt.
//
// 1. In-app links: a clean page follows an enhanced link without a dialog; a dirty page asks with "Unsaved changes",
//    Stay keeps the page and the edit, Leave follows the link once (enhanced links keep the document, links with
//    data-enhance-nav="false" load a new one); a fragment link on the page is not guarded.
// 2. Navigation from .NET (NavigationManager.NavigateTo) is guarded through the location-changing handler.
// 3. Back and Forward inside the document are guarded; Stay returns to the edited entry, Leave completes the traversal.
// 4. Document unload (reload, navigation to another address, and Back to an entry of another document) raises the browser's beforeunload prompt only while dirty;
//    dismissing it keeps the edit, accepting it loads the new document.
// 5. A successful save clears the guard; a failed save shows a toast and keeps the edit guarded.
// 6. An authentication loss leaves for login with a full document navigation and no prompt of either kind.
// 7. Mounting the guard again repeatedly leaves one working guard: one dialog, and Leave is not blocked by a stale listener.
// 8. The dirty dialog: Escape, the close button and the backdrop close a clean dialog at once and ask for a dirty one;
//    Stay keeps the dialog and its edit, Leave closes it once, and the next open starts clean.
// 9. A page reached by enhanced navigation rather than by a document load is guarded the same way, and the two reload
//    prompts a refused write raises are navigations away like any other: the form alert's link, which loads a new
//    document, and the toast's action, which forces one from .NET.
// Every case asserts zero content security policy violations, no style attribute in the toast region or any dialog, and
// no console or page errors.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started in
// Development.
// Run: dotnet run --project developer-cli -- blazor-harness unsaved-changes --browser all

import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { baseUrl, launchBrowser, newContext, observeErrors, parseArguments, pathBase, resultsFolder, signUpThroughBlazor } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
const interactiveUrl = `${baseUrl}${pathBase}/development/form-errors/interactive`;
const staticUrl = `${baseUrl}${pathBase}/development/form-errors/static`;
const interactiveTimeoutMs = 60_000;
const settleMs = 500;

const browser = await launchBrowser(options.browser);
const results = [];
const stamp = `${options.browser}-${Date.now()}`;

async function check(name, action) {
  try {
    const detail = await action();
    results.push({ name, passed: true, detail });
    console.log(`PASS ${name}${detail ? `: ${JSON.stringify(detail)}` : ""}`);
  } catch (error) {
    results.push({ name, passed: false, detail: error.message });
    console.log(`FAIL ${name}: ${error.message}`);
  }
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

const testId = (id) => `[data-testid="${id}"]`;
const signedUp = await signUpThroughBlazor(browser, options.browser, `unsaved-changes-${stamp}@example.com`);

// A fresh context and page per case, with the dialogs the browser raises recorded instead of answered by default
async function withFixture(action) {
  const context = await newContext(browser, options.browser, signedUp.storageState);
  const page = await context.newPage();
  const observations = observeErrors(page);
  const nativeDialogs = [];
  let answerNativeDialog = "dismiss";
  page.on("dialog", async (dialog) => {
    nativeDialogs.push(dialog.type());
    await (answerNativeDialog === "accept" ? dialog.accept() : dialog.dismiss());
  });
  const fixture = {
    page,
    nativeDialogs,
    answerNativeDialogsWith: (answer) => {
      answerNativeDialog = answer;
    },
    open: async (url = interactiveUrl) => {
      await page.goto(url, { waitUntil: "load" });
      if (url === interactiveUrl) await waitInteractive(page);
    }
  };
  try {
    const detail = await action(fixture);
    await assertCleanDocument(page, observations);
    return detail;
  } finally {
    await context.close();
  }
}

async function waitInteractive(page) {
  await page.locator(testId("fixture-interactive"), { hasText: "Interactive: True" }).waitFor({ timeout: interactiveTimeoutMs });
}

async function makeDirty(page, text = "unsaved edit") {
  await page.locator(testId("guard-input")).click();
  await page.locator(testId("guard-input")).pressSequentially(text);
  await page.locator(testId("guard-dirty"), { hasText: "dirty" }).waitFor();
  // The dirty state reaches the browser listeners after the render
  await page.waitForTimeout(settleMs);
}

// The page guard and the dirty dialog's guard each render one; only the open one is asked about
function unsavedChangesDialog(page) {
  return page.locator(`dialog${testId("unsaved-changes-dialog")}[open]`);
}

async function openUnsavedChangesDialogs(page) {
  return page.locator(`dialog${testId("unsaved-changes-dialog")}[open]`).count();
}

async function expectUnsavedChangesDialog(page) {
  const dialog = unsavedChangesDialog(page);
  await dialog.locator("h2", { hasText: "Unsaved changes" }).waitFor({ state: "visible" });
  assert(await dialog.evaluate((element) => element.open && element.matches(":modal")), "The unsaved changes dialog is not modal.");
  assert((await dialog.locator(testId("unsaved-changes-stay")).textContent()) === "Stay", "No Stay action.");
  assert((await dialog.locator(testId("unsaved-changes-leave")).textContent()) === "Leave", "No Leave action.");
  assert((await page.locator("dialog[open]").count()) >= 1, "No open dialog.");
}

async function expectNoUnsavedChangesDialog(page) {
  await page.waitForTimeout(settleMs);
  assert((await openUnsavedChangesDialogs(page)) === 0, "The unsaved changes dialog opened.");
}

async function stay(page) {
  await unsavedChangesDialog(page).locator(testId("unsaved-changes-stay")).click();
  await page.waitForTimeout(settleMs);
  assert((await openUnsavedChangesDialogs(page)) === 0, "The dialog stayed open after Stay.");
}

async function leave(page) {
  await unsavedChangesDialog(page).locator(testId("unsaved-changes-leave")).click();
}

function documentOrigin(page) {
  return page.evaluate(() => performance.timeOrigin);
}

// The reload prompts load the page again at the same address, so the new document is what the wait is on, not the URL
function waitForNewDocument(page, origin) {
  return page.waitForFunction((previous) => performance.timeOrigin !== previous, origin, { timeout: interactiveTimeoutMs });
}

// The fixture reaches the interactive page from the static one, which is an enhanced navigation inside the document
async function openThroughEnhancedNavigation(page, open) {
  await open(staticUrl);
  await page.locator(testId("interactive-fixture-link")).click();
  await page.waitForURL(interactiveUrl);
  await waitInteractive(page);
}

// The form-level prompt a refused write leaves on the form, whose link loads the page again as a new document
async function showReloadPrompt(page) {
  await page.locator(testId("guard-reload-prompt")).click();
  await page.locator(testId("form-error-reload")).waitFor();
}

async function assertEditKept(page) {
  assert(page.url() === interactiveUrl, `The page moved to ${page.url()}.`);
  assert((await page.locator(testId("guard-input")).inputValue()) === "unsaved edit", "The edit was not kept.");
  assert((await page.locator(testId("guard-dirty")).textContent()) === "dirty", "The page is no longer dirty.");
  const violations = await page.evaluate(() => window.__policyViolations);
  assert(violations.length === 0, `Policy violations on the edited page: ${JSON.stringify(violations)}.`);
}

async function assertCleanDocument(page, observations) {
  if (page.isClosed()) return;
  const state = await page.evaluate(() => ({
    violations: window.__policyViolations ?? [],
    styleAttributes: document.querySelectorAll('[data-testid="toast-region"] [style], [data-testid="toast-region"][style], dialog[style], dialog [style]').length
  }));
  assert(state.violations.length === 0, `Policy violations: ${JSON.stringify(state.violations)}.`);
  assert(state.styleAttributes === 0, `${state.styleAttributes} style attributes in the toast region or a dialog.`);
  assert(observations.pageErrors.length === 0, `Page errors: ${observations.pageErrors.join(" | ")}.`);
  assert(observations.errorResponses.length === 0, `Error responses: ${observations.errorResponses.join(" | ")}.`);
  assert(observations.consoleErrors.length === 0, `Console errors: ${observations.consoleErrors.join(" | ")}.`);
}

await check("clean page follows an in-app link without a dialog", () =>
  withFixture(async ({ page, open, nativeDialogs }) => {
    await open();
    await page.locator(testId("guard-link-enhanced")).click();
    await page.waitForURL(staticUrl);
    assert(nativeDialogs.length === 0, `Native dialogs: ${nativeDialogs.join(", ")}.`);
  })
);

await check("dirty page asks before an enhanced link, Stay keeps the edit and Leave follows it in the same document", () =>
  withFixture(async ({ page, open }) => {
    await open();
    const origin = await documentOrigin(page);
    await makeDirty(page);
    await page.locator(testId("guard-link-enhanced")).click();
    await expectUnsavedChangesDialog(page);
    await stay(page);
    await assertEditKept(page);
    await page.locator(testId("guard-link-enhanced")).click();
    await expectUnsavedChangesDialog(page);
    await leave(page);
    await page.waitForURL(staticUrl);
    await page.locator(testId("static-form")).waitFor();
    assert((await documentOrigin(page)) === origin, "The enhanced link loaded a new document.");
  })
);

await check("dirty page asks before a full navigation link, and Leave loads a new document once", () =>
  withFixture(async ({ page, open, nativeDialogs }) => {
    await open();
    const origin = await documentOrigin(page);
    await makeDirty(page);
    await page.locator(testId("guard-link-full")).click();
    await expectUnsavedChangesDialog(page);
    await stay(page);
    await assertEditKept(page);
    await page.locator(testId("guard-link-full")).click();
    await expectUnsavedChangesDialog(page);
    await leave(page);
    await page.waitForURL(staticUrl, { waitUntil: "load" });
    assert((await documentOrigin(page)) !== origin, "The full navigation link kept the document.");
    assert(nativeDialogs.length === 0, `Native dialogs after Leave: ${nativeDialogs.join(", ")}.`);
  })
);

await check("a fragment link on the dirty page is not guarded", () =>
  withFixture(async ({ page, open }) => {
    await open();
    await makeDirty(page);
    await page.locator(testId("guard-link-fragment")).click();
    await expectNoUnsavedChangesDialog(page);
    assert(page.url() === `${interactiveUrl}#guard-cases-anchor`, `Fragment link went to ${page.url()}.`);
  })
);

await check("dirty page asks before a navigation started from .NET", () =>
  withFixture(async ({ page, open, nativeDialogs }) => {
    await open();
    await makeDirty(page);
    await page.locator(testId("guard-navigate-code")).click();
    await expectUnsavedChangesDialog(page);
    await stay(page);
    await assertEditKept(page);
    await page.locator(testId("guard-navigate-code")).click();
    await expectUnsavedChangesDialog(page);
    await leave(page);
    await page.waitForURL(staticUrl, { waitUntil: "load" });
    assert(nativeDialogs.length === 0, `Native dialogs after Leave: ${nativeDialogs.join(", ")}.`);
  })
);

await check("Back inside the document is guarded", () =>
  withFixture(async ({ page, open }) => {
    await open(staticUrl);
    await page.locator(testId("interactive-fixture-link")).click();
    await page.waitForURL(interactiveUrl);
    await waitInteractive(page);
    const origin = await documentOrigin(page);
    await makeDirty(page);
    await page.goBack({ waitUntil: "commit", timeout: 5_000 }).catch(() => null);
    await expectUnsavedChangesDialog(page);
    await page.waitForURL(interactiveUrl);
    await stay(page);
    await assertEditKept(page);
    await page.goBack({ waitUntil: "commit", timeout: 5_000 }).catch(() => null);
    await expectUnsavedChangesDialog(page);
    await leave(page);
    await page.waitForURL(staticUrl);
    await page.locator(testId("static-form")).waitFor();
    assert((await documentOrigin(page)) === origin, "Back loaded a new document.");
    await page.goForward({ waitUntil: "commit", timeout: 5_000 }).catch(() => null);
    await page.waitForURL(interactiveUrl);
    await waitInteractive(page);
  })
);

await check("Forward inside the document is guarded", () =>
  withFixture(async ({ page, open }) => {
    await open();
    await page.locator(testId("guard-link-enhanced")).click();
    await page.waitForURL(staticUrl);
    await page.locator(testId("static-form")).waitFor();
    await page.goBack({ waitUntil: "commit" });
    await page.waitForURL(interactiveUrl);
    await waitInteractive(page);
    await makeDirty(page);
    await page.goForward({ waitUntil: "commit", timeout: 5_000 }).catch(() => null);
    await expectUnsavedChangesDialog(page);
    await page.waitForURL(interactiveUrl);
    await stay(page);
    await assertEditKept(page);
    await page.goForward({ waitUntil: "commit", timeout: 5_000 }).catch(() => null);
    await expectUnsavedChangesDialog(page);
    await leave(page);
    await page.waitForURL(staticUrl);
    await page.locator(testId("static-form")).waitFor();
  })
);

await check("document reload raises the browser prompt only while dirty", () =>
  withFixture(async ({ page, open, nativeDialogs, answerNativeDialogsWith }) => {
    await open();
    await makeDirty(page);
    await page.evaluate(() => setTimeout(() => location.reload(), 0));
    await page.waitForTimeout(2_000);
    assert(nativeDialogs.length === 1 && nativeDialogs[0] === "beforeunload", `Native dialogs after a dismissed reload: ${JSON.stringify(nativeDialogs)}.`);
    await assertEditKept(page);
    answerNativeDialogsWith("accept");
    const origin = await documentOrigin(page);
    await page.evaluate(() => setTimeout(() => location.assign(`${location.pathname}?reloaded=1`), 0));
    await page.waitForURL(`${interactiveUrl}?reloaded=1`, { waitUntil: "load" });
    assert(nativeDialogs.length === 2, `Native dialogs after an accepted navigation: ${JSON.stringify(nativeDialogs)}.`);
    assert((await documentOrigin(page)) !== origin, "Accepting the prompt did not load a new document.");
    await waitInteractive(page);
    assert((await page.locator(testId("guard-input")).inputValue()) === "", "The new document kept the edit.");
    await page.reload({ waitUntil: "load" });
    assert(nativeDialogs.length === 2, `A clean page raised a prompt: ${JSON.stringify(nativeDialogs)}.`);
    return { nativeDialogs };
  })
);

await check("Back to another document raises the browser prompt while dirty", () =>
  withFixture(async ({ page, open, nativeDialogs, answerNativeDialogsWith }) => {
    await open(staticUrl);
    await open();
    await makeDirty(page);
    await page.goBack({ waitUntil: "commit", timeout: 5_000 }).catch(() => null);
    await page.waitForTimeout(2_000);
    assert(nativeDialogs.length === 1 && nativeDialogs[0] === "beforeunload", `Native dialogs after a dismissed Back: ${JSON.stringify(nativeDialogs)}.`);
    assert((await openUnsavedChangesDialogs(page)) === 0, "The in-app dialog opened for a traversal to another document.");
    await assertEditKept(page);
    answerNativeDialogsWith("accept");
    await page.goBack({ waitUntil: "load", timeout: 10_000 }).catch(() => null);
    await page.waitForURL(staticUrl, { waitUntil: "load" });
    assert(nativeDialogs.length === 2, `Native dialogs after an accepted Back: ${JSON.stringify(nativeDialogs)}.`);
    return { nativeDialogs };
  })
);

await check("successful save clears the guard and a failed save keeps it", () =>
  withFixture(async ({ page, open }) => {
    await open();
    await makeDirty(page);
    await page.locator(testId("guard-save-failure")).click();
    await page.locator(testId("api-failure-toast")).waitFor();
    await assertEditKept(page);
    await page.locator(testId("guard-link-enhanced")).click();
    await expectUnsavedChangesDialog(page);
    await stay(page);
    await page.locator(testId("guard-save")).click();
    await page.locator(testId("guard-dirty"), { hasText: "clean" }).waitFor();
    await page.waitForTimeout(settleMs);
    await page.locator(testId("guard-link-enhanced")).click();
    await page.waitForURL(staticUrl);
  })
);

await check("authentication loss leaves for login without a prompt", () =>
  withFixture(async ({ page, open, nativeDialogs }) => {
    await open();
    const origin = await documentOrigin(page);
    await makeDirty(page);
    await page.locator(testId("guard-authentication-loss")).click();
    await page.waitForURL(new RegExp(`${pathBase}/login\\?returnPath=`), { waitUntil: "load" });
    assert((await documentOrigin(page)) !== origin, "Authentication loss kept the document.");
    assert(nativeDialogs.length === 0, `Native dialogs: ${JSON.stringify(nativeDialogs)}.`);
  })
);

await check("mounting the guard again leaves one working guard", () =>
  withFixture(async ({ page, open, nativeDialogs }) => {
    await open();
    for (let generation = 1; generation <= 5; generation++) {
      await page.locator(testId("guard-remount")).click();
      await page.locator(testId("guard-generation"), { hasText: String(generation) }).waitFor();
    }
    await makeDirty(page);
    await page.locator(testId("guard-link-enhanced")).click();
    await expectUnsavedChangesDialog(page);
    const openDialogs = await page.locator("dialog[open]").count();
    assert(openDialogs === 1, `${openDialogs} dialogs opened.`);
    await stay(page);
    await page.locator(testId("guard-link-full")).click();
    await expectUnsavedChangesDialog(page);
    await leave(page);
    await page.waitForURL(staticUrl, { waitUntil: "load" });
    assert(nativeDialogs.length === 0, `Native dialogs: ${JSON.stringify(nativeDialogs)}.`);
  })
);

await check("dirty dialog guards Escape, the close button and the backdrop", () =>
  withFixture(async ({ page, open }) => {
    await open();
    const dirtyDialog = page.locator(`dialog${testId("dirty-dialog")}`);
    const closeCount = page.locator(testId("dialog-close-count"));
    const isOpen = () => dirtyDialog.evaluate((element) => element.open);
    const openDialog = async () => {
      await page.locator(testId("dialog-open")).click();
      await dirtyDialog.locator(testId("dialog-input")).waitFor({ state: "visible" });
      assert(await dirtyDialog.evaluate((element) => element.matches(":modal")), "The dirty dialog is not modal.");
    };
    const typeInDialog = async () => {
      await dirtyDialog.locator(testId("dialog-input")).pressSequentially("dialog edit");
      await page.waitForTimeout(settleMs);
    };
    const clickBackdrop = () => page.mouse.click(5, 5);
    const closed = async (count) => {
      await closeCount.filter({ hasText: String(count) }).waitFor();
      await page.waitForTimeout(settleMs);
      assert(!(await isOpen()), "The dirty dialog is still open.");
    };

    // Clean: every way out closes at once
    await openDialog();
    await page.keyboard.press("Escape");
    await closed(1);
    await openDialog();
    await dirtyDialog.locator(testId("dialog-close")).click();
    await closed(2);
    await openDialog();
    await clickBackdrop();
    await closed(3);
    await expectNoUnsavedChangesDialog(page);

    // Dirty: each way out asks; Stay keeps the dialog and the edit, Leave closes it once
    const dismissals = [
      ["Escape", () => page.keyboard.press("Escape")],
      ["close button", () => dirtyDialog.locator(testId("dialog-close")).click()],
      ["backdrop", clickBackdrop]
    ];
    let expectedCloses = 3;
    for (const [name, dismiss] of dismissals) {
      await openDialog();
      await typeInDialog();
      await dismiss();
      await expectUnsavedChangesDialog(page);
      await stay(page);
      assert(await isOpen(), `${name}: Stay closed the dirty dialog.`);
      assert((await dirtyDialog.locator(testId("dialog-input")).inputValue()) === "dialog edit", `${name}: Stay lost the edit.`);
      await dismiss();
      await expectUnsavedChangesDialog(page);
      await leave(page);
      expectedCloses++;
      await closed(expectedCloses);
    }

    // Escape on the unsaved changes dialog is Stay
    await openDialog();
    await typeInDialog();
    await page.keyboard.press("Escape");
    await expectUnsavedChangesDialog(page);
    await page.keyboard.press("Escape");
    await page.waitForTimeout(settleMs);
    assert((await openUnsavedChangesDialogs(page)) === 0, "Escape did not dismiss the unsaved changes dialog.");
    assert(await isOpen(), "Escape on the unsaved changes dialog closed the dirty dialog.");
    await dirtyDialog.locator(testId("dialog-cancel")).click();
    await expectUnsavedChangesDialog(page);
    await leave(page);
    await closed(expectedCloses + 1);

    // The next open starts clean
    await openDialog();
    assert((await dirtyDialog.locator(testId("dialog-input")).inputValue()) === "", "The reopened dialog kept the discarded edit.");
    return { closes: expectedCloses + 1 };
  })
);

await check("a page reached by enhanced navigation is guarded like a freshly loaded document", () =>
  withFixture(async ({ page, open, nativeDialogs }) => {
    await openThroughEnhancedNavigation(page, open);
    await makeDirty(page);
    await page.locator(testId("guard-link-enhanced")).click();
    await expectUnsavedChangesDialog(page);
    await stay(page);
    await assertEditKept(page);
    await page.locator(testId("guard-link-full")).click();
    await expectUnsavedChangesDialog(page);
    await leave(page);
    await page.waitForURL(staticUrl, { waitUntil: "load" });
    assert(nativeDialogs.length === 0, `Native dialogs: ${JSON.stringify(nativeDialogs)}.`);
  })
);

await check("the form alert's reload link asks before it loads the page again", () =>
  withFixture(async ({ page, open, nativeDialogs }) => {
    await open();
    const origin = await documentOrigin(page);
    await makeDirty(page);
    await showReloadPrompt(page);
    await page.locator(testId("form-error-reload")).click();
    await expectUnsavedChangesDialog(page);
    await stay(page);
    await assertEditKept(page);
    await page.locator(testId("form-error-reload")).click();
    await expectUnsavedChangesDialog(page);
    await leave(page);
    await waitForNewDocument(page, origin);
    await waitInteractive(page);
    assert(page.url() === interactiveUrl, `The reload link went to ${page.url()}.`);
    assert((await page.locator(testId("guard-input")).inputValue()) === "", "The new document kept the edit.");
    assert(nativeDialogs.length === 0, `Native dialogs: ${JSON.stringify(nativeDialogs)}.`);
  })
);

await check("the form alert's reload link is guarded after an enhanced navigation too", () =>
  withFixture(async ({ page, open }) => {
    await openThroughEnhancedNavigation(page, open);
    await makeDirty(page);
    await showReloadPrompt(page);
    await page.locator(testId("form-error-reload")).click();
    await expectUnsavedChangesDialog(page);
    await stay(page);
    await assertEditKept(page);
  })
);

await check("the toast's reload action asks before it loads the page again", () =>
  withFixture(async ({ page, open, nativeDialogs }) => {
    await open();
    const origin = await documentOrigin(page);
    await makeDirty(page);
    await page.locator(testId("present-antiforgery")).click();
    const reloadAction = page.locator(`${testId("antiforgery-recovery-toast")} ${testId("toast-action")}`);
    await reloadAction.waitFor();
    await reloadAction.click();
    await expectUnsavedChangesDialog(page);
    await stay(page);
    await assertEditKept(page);
    await reloadAction.click();
    await expectUnsavedChangesDialog(page);
    await leave(page);
    await waitForNewDocument(page, origin);
    await waitInteractive(page);
    assert(page.url() === interactiveUrl, `The reload action went to ${page.url()}.`);
    assert((await page.locator(testId("guard-input")).inputValue()) === "", "The new document kept the edit.");
    assert(nativeDialogs.length === 0, `Native dialogs: ${JSON.stringify(nativeDialogs)}.`);
  })
);

await browser.close();

mkdirSync(resultsFolder, { recursive: true });
writeFileSync(path.join(resultsFolder, `unsaved-changes-${options.browser}.json`), JSON.stringify({ browser: options.browser, results }, null, 2));
if (results.some((result) => !result.passed)) process.exit(1);
