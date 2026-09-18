// The shared SidePane of the Blazor edition, on the users page, in one browser.
//
// The pane is one <dialog> element in two modes, chosen by ViewportState from the medium breakpoint (48rem):
//
// 1. Docked at 1280 by 720: a labelled region beside the list. The element carries no open attribute, so it is not modal
//    and traps no focus; Tab from inside it reaches the list again, the page still scrolls, and Escape closes it and puts
//    focus back on the row, which is the list's own Escape scope.
// 2. Full-screen at 390 by 844 and at 375 by 667: opened with showModal, so it is a modal dialog with aria-modal, an inert
//    background and the browser's focus containment. Tab cycles inside it, <html> carries the scroll lock class, the
//    backdrop button is named "Close side panel", and Escape closes the pane and gives focus back to the row it was
//    opened from.
// 3. Focus return when the row is gone: with the row removed from the page while the pane is open, closing gives focus to
//    the control the page names as the fallback, the search box, instead of losing it to the document body.
// 4. Crossing the breakpoint with the pane open: the mode is reconciled on the same element, so the pane stays open, the
//    modality and the scroll lock follow the width, and focus stays on a connected element.
// 5. Two modal layers: a destructive or editing dialog opened from inside a full-screen pane is the topmost modal and owns
//    the focus trap; Escape closes it first and the pane second.
// 6. Repeated navigation: leaving the users page and coming back several times leaves one pane element, no scroll lock and
//    a pane that still opens, so the viewport listeners are not multiplied by navigation.
// 7. Dialogs on phones: a modal dialog fills the screen below the small breakpoint and is centred above it, and the row
//    menu trigger, the menu items and the mobile navigation button are at least 44 pixels on phone widths.
//
// Every case asserts zero content security policy violations, no style attribute anywhere in the document, and no page
// errors.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started in
// Development.
// Run: dotnet run --project developer-cli -- blazor-harness side-pane --browser all

import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { baseUrl, launchBrowser, newContext, observeErrors, parseArguments, pathBase, resultsFolder, signUpThroughBlazor } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
const usersUrl = `${baseUrl}${pathBase}/account/users`;
const homeUrl = `${baseUrl}${pathBase}/app`;
const invitedUsers = 3;
const interactiveTimeoutMs = 60_000;
const settleMs = 400;
const tapTargetPixels = 44;

const desktopViewport = { viewport: { width: 1280, height: 720 } };
const phoneViewport = { viewport: { width: 390, height: 844 }, hasTouch: true };
const smallPhoneViewport = { viewport: { width: 375, height: 667 }, hasTouch: true };

const browser = await launchBrowser(options.browser);
const results = [];
const stamp = `${options.browser}-${Date.now()}`;

const testId = (id) => `[data-testid="${id}"]`;
const pane = testId("profile-pane");
const usersGrid = testId("users-grid");
const searchBox = "#users-search";

async function check(name, action) {
  try {
    const detail = await action();
    results.push({ name, passed: true, detail });
    console.log(`PASS ${name}${detail ? `: ${JSON.stringify(detail)}` : ""}`);
  } catch (error) {
    results.push({ name, passed: false, detail: String(error.stack ?? error.message).slice(0, 1_500) });
    console.log(`FAIL ${name}: ${error.message}`);
  }
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

const owner = await signUpThroughBlazor(browser, options.browser, `side-pane-owner-${stamp}@example.com`);
await seedUsers();

// Node cannot resolve app.dev.localhost, so the account API is called from a page on the gateway origin
async function seedUsers() {
  const context = await newContext(browser, options.browser, owner.storageState);
  const page = await context.newPage();
  await page.goto(homeUrl, { waitUntil: "load" });
  const outcome = await page.evaluate(
    async ({ stamp, invitedUsers }) => {
      const bootstrap = await (await fetch("/api/account/bootstrap", { credentials: "same-origin" })).json();
      const send = (method, url, body) =>
        fetch(url, { method, credentials: "same-origin", headers: { "content-type": "application/json", "x-xsrf-token": bootstrap.antiforgeryToken }, body: JSON.stringify(body) });
      const describe = async (response) => (response.ok ? null : `${response.status} ${(await response.text()).slice(0, 300)}`);
      const failures = [await describe(await send("PUT", "/api/account/tenants/current", { name: "Side pane fixture" }))];
      for (let index = 0; index < invitedUsers; index++) {
        failures.push(await describe(await send("POST", "/api/account/users/invite", { email: `side-pane-${stamp}-${index}@example.com` })));
      }
      return failures.filter((failure) => failure !== null);
    },
    { stamp, invitedUsers }
  );
  await context.close();
  if (outcome.length > 0) throw new Error(`Seeding failed: ${outcome.slice(0, 3).join(" | ")}`);
}

async function withUsers(contextOptions, action) {
  const context = await newContext(browser, options.browser, owner.storageState, "en-US", contextOptions);
  const page = await context.newPage();
  const observations = observeErrors(page);
  try {
    await page.goto(usersUrl, { waitUntil: "load" });
    await waitList(page);
    const detail = await action(page);
    await assertCleanDocument(page, observations);
    return detail;
  } finally {
    await context.close();
  }
}

async function waitList(page) {
  await page.locator(`${usersGrid}[data-list-state="ready"]`).waitFor({ timeout: interactiveTimeoutMs });
  await page.waitForTimeout(settleMs);
}

async function assertCleanDocument(page, observations) {
  await page.waitForTimeout(settleMs);
  const violations = await page.evaluate(() => window.__policyViolations);
  assert(violations.length === 0, `Policy violations: ${JSON.stringify(violations)}`);
  // As in shell-policy.mjs, the document element is left out: the framework writes its own load-percentage properties there
  const styled = await page.evaluate(() => [...document.body.querySelectorAll("[style]")].map((element) => `${element.tagName}.${element.className}`));
  assert(styled.length === 0, `Elements carrying a style attribute: ${styled.join(", ")}`);
  assert(observations.pageErrors.length === 0, `Page errors: ${observations.pageErrors.join(" | ")}`);
  assert(observations.errorResponses.length === 0, `Error responses: ${observations.errorResponses.join(" | ")}`);
  assert(observations.consoleErrors.length === 0, `Console errors: ${observations.consoleErrors.join(" | ")}`);
}

const rows = (page) => page.locator(`${usersGrid} tbody tr.data-list-row`);

// A closed dialog is never "visible" to Playwright, so the closed states are waited for as document conditions
const waitPaneClosed = (page) => page.waitForFunction((selector) => document.querySelector(selector)?.hasAttribute("hidden") === true, pane, { timeout: interactiveTimeoutMs });

const waitDialogClosed = (page, selector) =>
  page.waitForFunction((value) => document.querySelector(value)?.getAttribute("data-open") === "false", selector, { timeout: interactiveTimeoutMs });

// Opens the pane the way a user does, from a row, and waits for the mode the width asks for
async function openPane(page, index, mode) {
  await rows(page).nth(index).locator("[data-user-row]").click();
  await page.waitForURL(/userId=usr_/);
  await page.locator(`${pane}[data-side-pane-mode="${mode}"]:not([hidden])`).waitFor({ timeout: interactiveTimeoutMs });
  await page.waitForTimeout(settleMs);
}

const paneState = (page) =>
  page.evaluate((selector) => {
    const element = document.querySelector(selector);
    const active = document.activeElement;
    return {
      mode: element.getAttribute("data-side-pane-mode"),
      isOpenAttribute: element.hasAttribute("open"),
      isModal: element.matches(":modal"),
      role: element.getAttribute("role"),
      ariaModal: element.getAttribute("aria-modal"),
      labelledBy: element.getAttribute("aria-labelledby"),
      isHidden: element.hasAttribute("hidden"),
      scrollLocked: document.documentElement.classList.contains("scroll-locked"),
      rootClasses: document.documentElement.className,
      activeInsidePane: element.contains(active),
      activeIsRow: active instanceof HTMLElement && active.matches("tr.data-list-row"),
      activeTestId: active instanceof HTMLElement ? (active.getAttribute("data-testid") ?? active.id ?? active.tagName) : null,
      activeIsBody: active === document.body
    };
  }, pane);

await check("docked at 1280: a labelled region, not modal, no scroll lock, focus stays with the list", () =>
  withUsers(desktopViewport, async (page) => {
    await openPane(page, 1, "docked");
    const state = await paneState(page);
    assert(state.role === "region", `The docked pane's role is ${state.role}.`);
    assert(state.ariaModal === null, "The docked pane claims aria-modal.");
    assert(!state.isOpenAttribute && !state.isModal, "The docked pane is a modal dialog.");
    assert(state.labelledBy !== null, "The docked pane has no accessible name.");
    assert(!state.scrollLocked, "The docked pane locked the page scroll.");
    assert(!state.activeInsidePane, "The docked pane took focus from the list.");
    assert((await page.locator(`${pane} [data-side-pane-backdrop]`).count()) === 0, "The docked pane renders a backdrop.");

    // Tab from the pane's last focusable element moves on instead of wrapping to its first, which is what a trap does.
    // Where focus lands is the browser's business: with links out of the tab order it may even be the browser's own chrome.
    const focusable = `${pane} a[href], ${pane} button:not([disabled]), ${pane} input:not([disabled])`;
    const focusableCount = await page.locator(focusable).count();
    await page.locator(focusable).nth(focusableCount - 1).focus();
    await page.keyboard.press("Tab");
    const wrapped = await page.evaluate((selector) => {
      const element = document.querySelector(selector);
      const [first] = element.querySelectorAll("a[href], button:not([disabled]), input:not([disabled])");
      return document.activeElement === first;
    }, pane);
    assert(!wrapped, "Tab wrapped back to the start of the docked pane, so it is trapped.");

    // Escape belongs to the list's Escape scope here, which closes the pane and puts focus back on the row
    await rows(page).nth(1).focus();
    await page.keyboard.press("Escape");
    await page.waitForURL((url) => !url.searchParams.has("userId"));
    await waitPaneClosed(page);
    assert((await paneState(page)).activeIsRow, "Escape did not put focus back on a row.");
    return { mode: "docked" };
  })
);

for (const [label, contextOptions] of [
  ["390 by 844", phoneViewport],
  ["375 by 667", smallPhoneViewport]
]) {
  await check(`full-screen at ${label}: a modal dialog with the scroll lock, the focus trap and the named backdrop`, () =>
    withUsers(contextOptions, async (page) => {
      await openPane(page, 1, "fullscreen");
      const state = await paneState(page);
      assert(state.isOpenAttribute && state.isModal, "The full-screen pane is not a modal dialog.");
      assert(state.ariaModal === "true", `The full-screen pane's aria-modal is ${state.ariaModal}.`);
      assert(state.role === null, `The full-screen pane keeps role=${state.role} instead of its dialog role.`);
      assert(state.labelledBy !== null, "The full-screen pane has no accessible name.");
      assert(state.scrollLocked, `The full-screen pane did not lock the page scroll; <html> carries "${state.rootClasses}".`);
      assert(state.activeInsidePane, "The full-screen pane did not take focus.");
      assert(!(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)), "The page scrolls horizontally.");

      const backdrop = page.locator(`${pane} [data-side-pane-backdrop]`);
      assert((await backdrop.count()) === 1, "The full-screen pane renders no backdrop.");
      assert((await backdrop.getAttribute("aria-label")) === "Close side panel", "The backdrop has no accessible name.");

      // Tab cycles inside the modal: after more presses than the pane has focusable elements, focus is still inside it
      for (let presses = 0; presses < 15; presses++) {
        await page.keyboard.press("Tab");
        assert((await paneState(page)).activeInsidePane, `Tab left the full-screen pane after ${presses + 1} presses.`);
      }

      await page.keyboard.press("Escape");
      await page.waitForURL((url) => !url.searchParams.has("userId"));
      await waitPaneClosed(page);
      const closed = await paneState(page);
      assert(!closed.scrollLocked, "The scroll lock outlived the pane.");
      assert(closed.activeIsRow, `Escape left focus on ${closed.activeTestId} instead of the row.`);
      return { viewport: label };
    })
  );
}

await check("full-screen: closing with the row gone gives focus to the search box", () =>
  withUsers(phoneViewport, async (page) => {
    await openPane(page, 1, "fullscreen");
    // The row a delete would have removed, taken out of the page while the pane is open
    await page.evaluate((selector) => document.querySelectorAll(`${selector} tbody tr.data-list-row`)[1].remove(), usersGrid);
    await page.locator(testId("profile-close")).click();
    await waitPaneClosed(page);
    const state = await paneState(page);
    assert(!state.activeIsBody, "Closing the pane lost focus to the document body.");
    assert(state.activeTestId === "users-search" || (await page.locator(searchBox).evaluate((element) => element.contains(document.activeElement))), `Focus went to ${state.activeTestId}.`);
    return { fallback: state.activeTestId };
  })
);

await check("crossing the breakpoint keeps the pane open and reconciles modality, scroll lock and focus", () =>
  withUsers(desktopViewport, async (page) => {
    await openPane(page, 1, "docked");

    await page.setViewportSize({ width: 390, height: 844 });
    await page.locator(`${pane}[data-side-pane-mode="fullscreen"]`).waitFor({ timeout: interactiveTimeoutMs });
    await page.waitForTimeout(settleMs);
    const narrow = await paneState(page);
    assert(!narrow.isHidden, "Crossing the breakpoint closed the pane.");
    assert(narrow.isModal && narrow.scrollLocked, "The pane did not become a modal dialog with the scroll lock.");

    await page.setViewportSize({ width: 1280, height: 720 });
    await page.locator(`${pane}[data-side-pane-mode="docked"]`).waitFor({ timeout: interactiveTimeoutMs });
    await page.waitForTimeout(settleMs);
    const wide = await paneState(page);
    assert(!wide.isHidden, "Crossing back closed the pane.");
    assert(!wide.isModal && !wide.scrollLocked, "The pane stayed modal or kept the scroll lock when it docked again.");
    assert(!wide.activeIsBody, "Crossing the breakpoint lost focus to the document body.");
    assert(new URL(page.url()).searchParams.has("userId"), "Crossing the breakpoint dropped the activated row.");
    return { activeAfterResize: wide.activeTestId };
  })
);

await check("a dialog opened over a full-screen pane is the topmost modal and Escape closes it first", () =>
  withUsers(phoneViewport, async (page) => {
    await openPane(page, 1, "fullscreen");
    await page.locator(testId("profile-role")).click();
    const roleDialog = page.locator(testId("change-role-dialog"));
    await roleDialog.waitFor();
    await page.waitForTimeout(settleMs);
    const layers = await page.evaluate(() => [...document.querySelectorAll("dialog:modal")].map((dialog) => dialog.dataset.testid ?? dialog.className));
    assert(layers.length === 2, `Expected the pane and the dialog to be modal, found ${JSON.stringify(layers)}.`);
    assert(await roleDialog.evaluate((dialog) => dialog.contains(document.activeElement)), "The topmost dialog did not take focus.");

    await page.keyboard.press("Escape");
    await waitDialogClosed(page, testId("change-role-dialog"));
    await page.waitForTimeout(settleMs);
    const afterFirst = await paneState(page);
    assert(afterFirst.isModal && !afterFirst.isHidden, "Escape closed the pane instead of the dialog above it.");

    await page.keyboard.press("Escape");
    await page.waitForURL((url) => !url.searchParams.has("userId"));
    await waitPaneClosed(page);
    assert(!(await paneState(page)).scrollLocked, "The scroll lock outlived the two layers.");
    return { layers };
  })
);

await check("repeated navigation leaves one pane, no scroll lock and a pane that still opens", () =>
  withUsers(phoneViewport, async (page) => {
    for (let round = 0; round < 3; round++) {
      await openPane(page, 0, "fullscreen");
      await page.keyboard.press("Escape");
      await waitPaneClosed(page);
      await page.locator(`${usersGrid} [data-list-sort="Name"]`).click();
      await waitList(page);
    }

    const panes = await page.locator("dialog.side-pane").count();
    assert(panes === 1, `The page holds ${panes} side panes.`);
    await openPane(page, 0, "fullscreen");
    const state = await paneState(page);
    assert(state.isModal && state.scrollLocked, "The pane stopped becoming modal after repeated navigation.");
    await page.keyboard.press("Escape");
    await waitPaneClosed(page);
    assert(!(await paneState(page)).scrollLocked, "A scroll lock was left behind.");
    return { panes };
  })
);

// The pane closes when the page itself changes, not when the list writes the activated row into the query string
await check("navigating to another page closes the pane", () =>
  withUsers(desktopViewport, async (page) => {
    await openPane(page, 1, "docked");
    await page.locator(`.app-sidebar a.shell-nav-link[href="${pathBase}/app"]`).click();
    await page.waitForURL(`${homeUrl}`);
    await page.waitForTimeout(settleMs);
    assert((await page.locator(`${pane}:not([hidden])`).count()) === 0, "The pane survived the navigation.");
    return { closed: true };
  })
);

const dialogBox = (page, selector) => page.locator(selector).evaluate((dialog) => ({ width: dialog.getBoundingClientRect().width, height: dialog.getBoundingClientRect().height }));

await check("a modal dialog fills the screen on a phone and is centred on a desktop", async () => {
  // The invite dialog is the same ModalDialog every dialog of the edition uses, and it opens at both widths, which the
  // filter dialog does not: from 54 rem the filters render inline in the toolbar instead
  const openInvite = async (page) => {
    await page.locator(testId("invite-user")).click();
    await page.locator(testId("invite-user-dialog")).waitFor();
    await page.waitForTimeout(settleMs);
  };

  const phone = await withUsers(phoneViewport, async (page) => {
    await openInvite(page);
    const box = await dialogBox(page, testId("invite-user-dialog"));
    const viewport = page.viewportSize();
    assert(Math.abs(box.width - viewport.width) < 2, `The dialog is ${box.width} wide in a ${viewport.width} viewport.`);
    assert(box.height >= viewport.height - 2, `The dialog is ${box.height} tall in a ${viewport.height} viewport.`);

    const small = await page.evaluate(
      (minimum) =>
        [...document.querySelectorAll(".data-list-row-menu-trigger, .mobile-menu-button, dialog[open] button")]
          .filter((element) => element.getBoundingClientRect().width > 0)
          .filter((element) => element.getBoundingClientRect().width < minimum || element.getBoundingClientRect().height < minimum)
          .map((element) => `${element.className || element.tagName}: ${Math.round(element.getBoundingClientRect().width)}x${Math.round(element.getBoundingClientRect().height)}`),
      tapTargetPixels
    );
    assert(small.length === 0, `Tap targets under ${tapTargetPixels} pixels: ${small.join(", ")}`);
    await page.keyboard.press("Escape");
    await waitDialogClosed(page, testId("invite-user-dialog"));
    return box;
  });

  const desktop = await withUsers(desktopViewport, async (page) => {
    await openInvite(page);
    const box = await dialogBox(page, testId("invite-user-dialog"));
    const viewport = page.viewportSize();
    assert(box.width < viewport.width, `The dialog fills the width of a ${viewport.width} viewport.`);
    assert(box.height < viewport.height, `The dialog fills the height of a ${viewport.height} viewport.`);
    await page.keyboard.press("Escape");
    await waitDialogClosed(page, testId("invite-user-dialog"));
    return box;
  });

  return { phone, desktop };
});

await browser.close();

mkdirSync(resultsFolder, { recursive: true });
const resultFile = path.join(resultsFolder, `side-pane-${options.browser}.json`);
writeFileSync(resultFile, JSON.stringify({ browser: options.browser, finishedAt: new Date().toISOString(), results }, null, 2));
const failed = results.filter((result) => !result.passed);
console.log(`${options.browser}: ${results.length - failed.length} of ${results.length} passed. Result file: ${resultFile}`);
process.exitCode = failed.length === 0 ? 0 : 1;
