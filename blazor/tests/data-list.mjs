// The shared DataList of the Blazor edition, on the Development-only list fixture, in one browser.
//
// The fixture page /blazor/development/data-list hosts two lists over the tenant's users: the primary list with multiple
// selection, a search filter, the userId parameter and a side pane, and the secondary list with single selection and
// prefixed URL parameters. The harness signs up an owner through the Blazor pages and invites users through the account
// API, so the primary list has two pages of 25 rows.
//
// 1. First load: 25 rows, "Page 1 of 2", the total count, previous disabled and next enabled.
// 2. Sort: a header click writes orderBy and sortOrder, never QuickGrid's sort, direction or page; the sorted state
//    survives a reload and Back returns to the previous sort.
// 3. Paging: next and previous write pageOffset with history entries; a second visit to a page makes no users request.
// 4. Selection: a row checkbox makes the header checkbox indeterminate, select-all selects the page and raises the
//    multiple-selection event, and a page change clears the selection.
// 5. Keyboard and clicks: click activates (userId in the URL, pane open); arrows, Home and End move focus; Space toggles;
//    Shift+Arrow extends; Enter activates; Escape closes the pane and returns focus to the active row; Ctrl or Cmd click
//    toggles and Shift click selects a range.
// 6. Keys in interactive descendants (the search box, a row menu button) are not intercepted.
// 7. Two lists on one page keep their own selection, focus and URL parameters.
// 8. Deep links: a stale pageOffset shows the last page without an error and replaces the URL; malformed values render
//    the defaults.
// 9. Empty result: the empty state with both paging buttons disabled; typing a search replaces the history entry.
// 10. A mutation stand-in invalidates the list and the current page is requested again.
// 11. Phone, on the users page at 390 by 844 with touch: one visible data column with the email and pending badge in the
//     name cell; a long-press opens the row menu without activating the row (a touch long-press through the DevTools
//     protocol in Chromium only, because page.touchscreen can only tap; a right-click in every browser); a tap opens the
//     pane, which is a full-screen modal dialog at this width, so Escape closes it and gives focus back to the row before
//     arrows move focus without activating and Enter activates. The pane's own modes are covered by side-pane.mjs.
// 12. Phone loading: no paginator; scrolling the sentinel into view appends the next page and replaces pageOffset; the
//     load more button works from the keyboard with one request however the sentinel and the button race; the status
//     announces that every row is loaded; Back restores the loaded range from the page cache and a reload restores it.
// Every case asserts zero content security policy violations, no style attribute inside the lists, and no page errors.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started in
// Development.
// Run: dotnet run --project developer-cli -- blazor-harness data-list --browser all

import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { baseUrl, launchBrowser, newContext, observeErrors, parseArguments, pathBase, resultsFolder, signUpThroughBlazor } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
const fixtureUrl = `${baseUrl}${pathBase}/development/data-list`;
const invitedUsers = 29;
const interactiveTimeoutMs = 60_000;
const settleMs = 400;
const usersRequestPattern = /\/api\/account\/users(\?|$)/;

const browser = await launchBrowser(options.browser);
const results = [];
const stamp = `${options.browser}-${Date.now()}`;

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

const testId = (id) => `[data-testid="${id}"]`;
const primary = testId("primary-list");
const secondary = testId("secondary-list");

const owner = await signUpThroughBlazor(browser, options.browser, `data-list-owner-${stamp}@example.com`);
await seedUsers();

// Node cannot resolve app.dev.localhost, so the account API is called from a page on the gateway origin
async function seedUsers() {
  const context = await newContext(browser, options.browser, owner.storageState);
  const page = await context.newPage();
  await page.goto(`${baseUrl}${pathBase}/app`, { waitUntil: "load" });
  const outcome = await page.evaluate(
    async ({ stamp, invitedUsers }) => {
      const bootstrap = await (await fetch("/api/account/bootstrap", { credentials: "same-origin" })).json();
      const send = (method, url, body) =>
        fetch(url, { method, credentials: "same-origin", headers: { "content-type": "application/json", "x-xsrf-token": bootstrap.antiforgeryToken }, body: JSON.stringify(body) });
      const describe = async (response) => (response.ok ? null : `${response.status} ${(await response.text()).slice(0, 300)}`);
      const failures = [await describe(await send("PUT", "/api/account/tenants/current", { name: "Data list fixture" }))];
      for (let index = 0; index < invitedUsers; index++) {
        failures.push(await describe(await send("POST", "/api/account/users/invite", { email: `data-list-${stamp}-${String(index).padStart(2, "0")}@example.com` })));
      }
      return failures.filter((failure) => failure !== null);
    },
    { stamp, invitedUsers }
  );
  await context.close();
  if (outcome.length > 0) throw new Error(`Seeding failed: ${outcome.slice(0, 3).join(" | ")}`);
}

// expected: an error response the case causes on purpose, with the console line the browser logs for it
async function withFixture(action, url = fixtureUrl, expected = null) {
  const context = await newContext(browser, options.browser, owner.storageState);
  const page = await context.newPage();
  const observations = observeErrors(page);
  const usersRequests = [];
  page.on("request", (request) => {
    if (usersRequestPattern.test(request.url())) usersRequests.push(request.url());
  });
  try {
    await page.goto(url, { waitUntil: "load" });
    await page.locator(testId("fixture-interactive"), { hasText: "Interactive: True" }).waitFor({ timeout: interactiveTimeoutMs });
    const detail = await action({ page, usersRequests });
    await assertCleanDocument(page, observations, expected);
    return detail;
  } finally {
    await context.close();
  }
}

async function assertCleanDocument(page, observations, expected, lists = [primary, secondary]) {
  await page.waitForTimeout(settleMs);
  const violations = await page.evaluate(() => window.__policyViolations);
  assert(violations.length === 0, `Policy violations: ${JSON.stringify(violations)}`);
  const styled = await page.locator(lists.flatMap((list) => [`${list} [style]`, `${list}[style]`]).join(", ")).count();
  assert(styled === 0, `${styled} elements inside the lists carry a style attribute.`);
  assert(observations.pageErrors.length === 0, `Page errors: ${observations.pageErrors.join(" | ")}`);
  const errorResponses = observations.errorResponses.filter((response) => !expected?.response.test(response));
  assert(errorResponses.length === 0, `Error responses: ${errorResponses.join(" | ")}`);
  const expectedCount = observations.errorResponses.filter((response) => expected?.response.test(response)).length;
  const consoleErrors = observations.consoleErrors;
  const unexpectedConsole = consoleErrors.filter((message) => !expected?.console.test(message));
  const expectedConsole = consoleErrors.length - unexpectedConsole.length;
  assert(unexpectedConsole.length === 0 && expectedConsole <= expectedCount, `Console errors: ${consoleErrors.join(" | ")}`);
}

async function waitList(page, list, { state = "ready", pageOffset } = {}) {
  const offset = pageOffset === undefined ? "" : `[data-list-page-offset="${pageOffset}"]`;
  await page.locator(`${list}[data-list-state="${state}"]${offset}`).waitFor({ timeout: interactiveTimeoutMs });
  await page.waitForTimeout(settleMs);
}

const rows = (page, list) => page.locator(`${list} tbody tr.data-list-row`);
const rowEmails = (page, list) => rows(page, list).evaluateAll((all) => all.map((row) => row.querySelector("td:nth-child(2)")?.textContent.trim() ?? row.querySelector("td")?.textContent.trim()));
const pageText = (page, list) => page.locator(`${list} [data-list-page]`).textContent();
const focusedRowIndex = (page, list) =>
  page.evaluate((selector) => [...document.querySelectorAll(`${selector} tbody tr.data-list-row`)].indexOf(document.activeElement), list);
const selectionOf = async (page) => ((await page.locator(testId("primary-selection")).textContent()) || "").split(",").filter(Boolean);

function assertNoQuickGridParameters(url) {
  const parameters = new URL(url).searchParams;
  for (const name of ["sort", "direction", "page"]) assert(!parameters.has(name), `The URL carries QuickGrid's ${name} parameter: ${url}`);
}

await check("first load renders the first server page with the localized paginator", () =>
  withFixture(async ({ page, usersRequests }) => {
    await waitList(page, primary);
    const count = await rows(page, primary).count();
    assert(count === 25, `Expected 25 rows, found ${count}.`);
    assert((await pageText(page, primary)) === "Page 1 of 2", `Paginator text: ${await pageText(page, primary)}`);
    assert((await page.locator(`${primary} [data-list-total-count]`).textContent()) === `${invitedUsers + 1} total`, "Wrong total count text.");
    assert(await page.locator(`${primary} [data-list-previous]`).isDisabled(), "Previous is enabled on the first page.");
    assert(await page.locator(`${primary} [data-list-next]`).isEnabled(), "Next is disabled on the first page.");
    assert((await page.locator(`${primary} [data-list-next]`).textContent()).trim() === "Next page", "The next button has no accessible name.");
    return { usersRequests: usersRequests.length };
  })
);

await check("sort by column updates the URL, survives reload and Back", () =>
  withFixture(async ({ page }) => {
    await waitList(page, primary);
    await page.locator(`${primary} [data-list-sort="Email"]`).click();
    await page.waitForURL(/orderBy=Email/);
    await waitList(page, primary);
    const ascending = await rowEmails(page, primary);
    assert(!page.url().includes("sortOrder="), `Ascending sort wrote sortOrder: ${page.url()}`);

    await page.locator(`${primary} [data-list-sort="Email"]`).click();
    await page.waitForURL(/sortOrder=Descending/);
    await waitList(page, primary);
    const descending = await rowEmails(page, primary);
    assert(descending[0] !== ascending[0], "Descending sort shows the same first row.");
    assertNoQuickGridParameters(page.url());

    // Back is checked before the reload: in Playwright's Firefox, Back after a reload does not leave the reloaded entry,
    // whether through page.goBack or history.back, so that order is not asserted
    await page.goBack({ waitUntil: "commit" });
    await page.waitForURL((url) => url.searchParams.get("orderBy") === "Email" && !url.searchParams.has("sortOrder"), { waitUntil: "commit" });
    await waitList(page, primary);
    await page.waitForFunction((selector) => document.querySelector(`${selector} th.data-list-sorted-ascending [data-list-sort="Email"]`) !== null, primary);
    assert((await rowEmails(page, primary))[0] === ascending[0], "Back did not restore the ascending sort.");

    await page.goForward({ waitUntil: "commit" });
    await page.waitForURL(/sortOrder=Descending/, { waitUntil: "commit" });
    await waitList(page, primary);
    await page.waitForFunction((selector) => document.querySelector(`${selector} th.data-list-sorted-descending [data-list-sort="Email"]`) !== null, primary);

    await page.reload({ waitUntil: "load" });
    await waitList(page, primary);
    assert((await rowEmails(page, primary))[0] === descending[0], "Reload lost the descending sort.");
    assert((await page.locator(`${primary} th.data-list-sorted-descending [data-list-sort="Email"]`).count()) === 1, "The header does not show the descending sort.");
    assertNoQuickGridParameters(page.url());
    return { url: page.url() };
  })
);

await check("next and previous page write pageOffset, and a second visit makes no request", () =>
  withFixture(async ({ page, usersRequests }) => {
    await waitList(page, primary);
    await page.locator(`${primary} [data-list-next]`).click();
    await page.waitForURL(/pageOffset=1/);
    await waitList(page, primary, { pageOffset: 1 });
    assert((await rows(page, primary).count()) === invitedUsers + 1 - 25, "The second page has the wrong row count.");
    assert((await pageText(page, primary)) === "Page 2 of 2", "Wrong paginator text on the last page.");
    assert(await page.locator(`${primary} [data-list-next]`).isDisabled(), "Next is enabled on the last page.");

    await page.locator(`${primary} [data-list-previous]`).click();
    await page.waitForURL((url) => !url.searchParams.has("pageOffset"));
    await waitList(page, primary, { pageOffset: 0 });
    const requestsAfterFirstVisits = usersRequests.length;

    await page.locator(`${primary} [data-list-next]`).click();
    await waitList(page, primary, { pageOffset: 1 });
    await page.locator(`${primary} [data-list-previous]`).click();
    await waitList(page, primary, { pageOffset: 0 });
    assert(usersRequests.length === requestsAfterFirstVisits, `Second visits made ${usersRequests.length - requestsAfterFirstVisits} users requests.`);

    await page.goBack({ waitUntil: "load" });
    await page.waitForURL(/pageOffset=1/);
    await waitList(page, primary, { pageOffset: 1 });
    assertNoQuickGridParameters(page.url());
    return { usersRequests: usersRequests.length };
  })
);

await check("select-all, indeterminate header and selection cleared on page change", () =>
  withFixture(async ({ page }) => {
    await waitList(page, primary);
    const header = page.locator(`${primary} input[data-list-select-all]`);
    await rows(page, primary).nth(1).locator("input[data-list-select]").check();
    await page.waitForFunction((selector) => document.querySelector(`${selector} input[data-list-select-all]`).indeterminate, primary);
    assert(!(await header.isChecked()), "The header is checked with one row selected.");

    await header.click();
    await page.locator(`${primary}[data-list-selected-count="25"]`).waitFor();
    await page.waitForTimeout(settleMs);
    assert(await header.isChecked(), "The header is not checked with the page selected.");
    assert(!(await header.evaluate((element) => element.indeterminate)), "The header stays indeterminate with the page selected.");
    assert((await page.locator(testId("primary-multiple-count")).textContent()) !== "0", "No multiple-selection event.");

    await page.locator(`${primary} [data-list-next]`).click();
    await waitList(page, primary, { pageOffset: 1 });
    assert((await page.locator(`${primary}`).getAttribute("data-list-selected-count")) === "0", "The selection survived the page change.");
    assert((await selectionOf(page)).length === 0, "The page still reports a selection.");
  })
);

await check("click, arrows, Home, End, Space, Shift+Arrow, Enter and Escape with focus return", () =>
  withFixture(async ({ page }) => {
    await waitList(page, primary);
    const emails = await rowEmails(page, primary);

    await rows(page, primary).nth(0).locator("td:nth-child(2)").click();
    await page.locator(testId("primary-activated"), { hasText: emails[0] }).waitFor();
    await page.waitForURL(/userId=usr_/);
    assert(await page.locator(testId("fixture-pane")).isVisible(), "The pane did not open on click.");

    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("ArrowDown");
    assert((await focusedRowIndex(page, primary)) === 2, `ArrowDown moved focus to ${await focusedRowIndex(page, primary)}.`);
    await page.keyboard.press("ArrowUp");
    assert((await focusedRowIndex(page, primary)) === 1, "ArrowUp did not move focus.");
    await page.keyboard.press("End");
    assert((await focusedRowIndex(page, primary)) === 24, "End did not move focus to the last row.");
    await page.keyboard.press("Home");
    assert((await focusedRowIndex(page, primary)) === 0, "Home did not move focus to the first row.");
    await page.keyboard.press("Control+End");
    assert((await focusedRowIndex(page, primary)) === 24, "Ctrl+End did not move focus to the last row.");
    await page.keyboard.press("Control+Home");
    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("ArrowDown");

    await page.keyboard.press("Space");
    await page.locator(`${primary}[data-list-selected-count="2"]`).waitFor();
    await page.keyboard.press("Shift+ArrowDown");
    await page.locator(`${primary}[data-list-selected-count="2"]`).waitFor();
    await page.waitForTimeout(settleMs);
    const extended = await selectionOf(page);
    assert((await focusedRowIndex(page, primary)) === 3, "Shift+ArrowDown did not move focus.");
    assert(extended.length === 2, `Shift+ArrowDown selection: ${extended.join(",")}`);

    await page.keyboard.press("Enter");
    await page.locator(testId("primary-activated"), { hasText: emails[3] }).waitFor();
    await page.keyboard.press("ArrowUp");
    await page.keyboard.press("Escape");
    await page.locator(testId("primary-closed-count"), { hasText: "1" }).waitFor();
    await page.waitForURL((url) => !url.searchParams.has("userId"));
    assert(!(await page.locator(testId("fixture-pane")).isVisible()), "Escape did not close the pane.");
    await page.waitForFunction((selector) => [...document.querySelectorAll(`${selector} tbody tr.data-list-row`)].indexOf(document.activeElement) === 3, primary);

    await rows(page, primary).nth(5).locator("td:nth-child(2)").click();
    await rows(page, primary).nth(7).locator("td:nth-child(2)").click({ modifiers: ["ControlOrMeta"] });
    await page.locator(`${primary}[data-list-selected-count="2"]`).waitFor();
    await rows(page, primary).nth(9).locator("td:nth-child(2)").click({ modifiers: ["Shift"] });
    await page.locator(`${primary}[data-list-selected-count="3"]`).waitFor();

    await page.locator(testId("fixture-pane-close")).click();
    await page.locator(testId("primary-closed-count"), { hasText: "2" }).waitFor();
    await page.waitForFunction((selector) => [...document.querySelectorAll(`${selector} tbody tr.data-list-row`)].indexOf(document.activeElement) === 5, primary);
    assertNoQuickGridParameters(page.url());
  })
);

await check("keys in the search box and a row menu button are not intercepted", () =>
  withFixture(async ({ page }) => {
    await waitList(page, primary);
    const menu = rows(page, primary).nth(2).locator(testId("row-menu"));
    await menu.focus();
    await page.keyboard.press("Enter");
    await page.keyboard.press("Space");
    await page.keyboard.press("ArrowDown");
    await page.waitForTimeout(settleMs);
    assert((await page.locator(testId("primary-activated")).textContent()) === "", "Enter on the row menu activated the row.");
    assert((await selectionOf(page)).length === 0, "Space on the row menu toggled the row.");
    assert(await menu.evaluate((element) => element === document.activeElement), "ArrowDown moved focus away from the row menu.");

    const search = page.locator(testId("list-search"));
    await search.focus();
    await page.keyboard.type("a b");
    await page.keyboard.press("Home");
    await page.keyboard.press("End");
    await page.keyboard.type("c");
    assert((await search.inputValue()) === "a bc", `The search box lost keys: '${await search.inputValue()}'.`);
  })
);

await check("two lists on one page keep their own selection, focus and URL state", () =>
  withFixture(async ({ page }) => {
    await waitList(page, primary);
    await waitList(page, secondary);
    await rows(page, primary).nth(0).locator("input[data-list-select]").check();
    await page.locator(`${primary}[data-list-selected-count="1"]`).waitFor();

    await rows(page, secondary).nth(1).locator("td").first().click();
    await page.locator(`${secondary}[data-list-selected-count="1"]`).waitFor();
    await page.keyboard.press("ArrowDown");
    assert((await focusedRowIndex(page, secondary)) === 2, "ArrowDown in the secondary list did not move its focus.");
    assert((await page.locator(primary).getAttribute("data-list-selected-count")) === "1", "The secondary click changed the primary selection.");
    assert((await page.locator(testId("primary-activated")).textContent()) === "", "The secondary click activated the primary list.");

    await page.locator(`${secondary} [data-list-sort="Email"]`).click();
    await page.waitForURL(/secondOrderBy=Email/);
    await waitList(page, secondary);
    const url = new URL(page.url());
    assert(!url.searchParams.has("orderBy"), `The secondary sort wrote the primary parameter: ${page.url()}`);
    assert((await page.locator(primary).getAttribute("data-list-selected-count")) === "1", "The secondary sort cleared the primary selection.");
    return { url: page.url() };
  })
);

await check("a stale deep link falls back to the last page, malformed values to the defaults", async () => {
  const stale = await withFixture(
    async ({ page }) => {
      await waitList(page, primary, { pageOffset: 1 });
      await page.waitForURL((url) => url.searchParams.get("pageOffset") === "1");
      assert((await page.locator(`${primary} [data-list-error]`).count()) === 0, "The stale offset shows an error.");
      assert((await page.locator("[role='alert']").count()) === 0, "The stale offset shows an alert.");
      assert((await pageText(page, primary)) === "Page 2 of 2", "The stale offset did not land on the last page.");
      assert(new URL(page.url()).searchParams.get("tab") === "keep", "An unrelated parameter was dropped.");
      return page.url();
    },
    `${fixtureUrl}?tab=keep&pageOffset=9&orderBy=Email&sortOrder=Descending`,
    // The account API answers the stale offset with 400 once; the list recovers from it without showing an error
    { response: /^400 .*\/api\/account\/users\?.*PageOffset=9(&|$)/, console: /^Failed to load resource: the server responded with a status of 400/ }
  );
  const malformed = await withFixture(
    async ({ page }) => {
      await waitList(page, primary, { pageOffset: 0 });
      assert((await pageText(page, primary)) === "Page 1 of 2", "Malformed values did not render the first page.");
      assert((await page.locator(`${primary} th.data-list-sorted-ascending [data-list-sort="Name"]`).count()) === 1, "Malformed values did not render the default sort.");
      return page.url();
    },
    `${fixtureUrl}?pageOffset=-3&orderBy=zzz&sortOrder=sideways&userRole=nobody&page=2&sort=Email`
  );
  return { stale, malformed };
});

await check("an empty search result shows the empty state and typing replaces the history entry", () =>
  withFixture(async ({ page }) => {
    await waitList(page, primary);
    const historyLength = await page.evaluate(() => history.length);
    await page.locator(testId("list-search")).fill("no-such-user");
    await page.waitForURL(/search=no-such-user/);
    await waitList(page, primary, { state: "empty", pageOffset: 0 });
    assert((await page.locator(`${primary} [data-list-empty]`).textContent()).trim() === "No items found.", "No empty state text.");
    assert(await page.locator(`${primary} [data-list-previous]`).isDisabled(), "Previous is enabled on an empty result.");
    assert(await page.locator(`${primary} [data-list-next]`).isDisabled(), "Next is enabled on an empty result.");
    assert((await page.locator(`${primary} input[data-list-select-all]`).count()) <= 1, "Unexpected select-all markup.");
    assert((await page.evaluate(() => history.length)) === historyLength, "Search typing added a history entry.");
  })
);

await check("invalidation requests the current page again", () =>
  withFixture(async ({ page, usersRequests }) => {
    await waitList(page, primary);
    await rows(page, primary).nth(0).locator("input[data-list-select]").check();
    const before = usersRequests.length;
    await page.locator(testId("invalidate")).click();
    await page.waitForTimeout(settleMs);
    await waitList(page, primary);
    assert(usersRequests.length === before + 1, `Invalidation made ${usersRequests.length - before} users requests.`);
    assert((await page.locator(primary).getAttribute("data-list-selected-count")) === "0", "Invalidation kept the selection.");
  })
);

const phoneOptions = { viewport: { width: 390, height: 844 }, hasTouch: true };
const usersUrl = `${baseUrl}${pathBase}/account/users`;
const usersGrid = testId("users-grid");
const openMenu = `${usersGrid} [role="menu"]`;

async function withPhone(action, url = usersUrl) {
  const context = await newContext(browser, options.browser, owner.storageState, "en-US", phoneOptions);
  const page = await context.newPage();
  const observations = observeErrors(page);
  const usersRequests = [];
  page.on("request", (request) => {
    if (usersRequestPattern.test(request.url())) usersRequests.push(request.url());
  });
  try {
    await page.goto(url, { waitUntil: "load" });
    const detail = await action({ page, usersRequests });
    await assertCleanDocument(page, observations, null, [usersGrid]);
    return detail;
  } finally {
    await context.close();
  }
}

// The infinite load mode is set only once data-list.js reported the phone width, so this also waits for interactivity
async function waitPhoneList(page, { loadedCount, pageOffset } = {}) {
  const loaded = loadedCount === undefined ? "" : `[data-list-loaded-count="${loadedCount}"]`;
  const offset = pageOffset === undefined ? "" : `[data-list-page-offset="${pageOffset}"]`;
  await page.locator(`${usersGrid}[data-list-state="ready"][data-list-load-mode="infinite"]${loaded}${offset}`).waitFor({ timeout: interactiveTimeoutMs });
  await page.waitForTimeout(settleMs);
}

const nameCell = (page, index) => rows(page, usersGrid).nth(index).locator("[data-user-row]");
const pageOffsetRequests = (usersRequests, pageOffset) => usersRequests.filter((url) => new URL(url).searchParams.get("PageOffset") === String(pageOffset)).length;

// A touch user closes the menu by tapping outside it
async function closeRowMenu(page) {
  await page.locator(`${usersGrid} .data-list-row-menu-backdrop`).click({ position: { x: 5, y: 5 } });
  await page.locator(openMenu).waitFor({ state: "detached" });
}

await check("phone: one column, long-press and right-click open the row menu, tap opens the pane, arrows and Enter", () =>
  withPhone(async ({ page }) => {
    await waitPhoneList(page);
    const visibleColumns = await page.evaluate(
      (selector) =>
        [...document.querySelectorAll(`${selector} thead th`)].filter((header) => getComputedStyle(header).display !== "none" && !header.classList.contains("data-list-menu")).length,
      usersGrid
    );
    assert(visibleColumns === 1, `Expected one visible data column, found ${visibleColumns}.`);
    assert(await rows(page, usersGrid).nth(0).locator(testId("user-phone-email")).isVisible(), "The name cell does not show the email.");
    assert((await page.locator(`${usersGrid} ${testId("user-pending-badge")}`).first().isVisible()), "No pending badge on an invited user.");
    assert((await page.locator(`${usersGrid} [data-list-page]`).count()) === 0, "The paginator renders on a phone.");
    assert(!(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)), "The page scrolls horizontally.");

    let longPress = "right-click only";
    if (options.browser === "chromium") {
      const box = await nameCell(page, 1).boundingBox();
      const cdp = await page.context().newCDPSession(page);
      const point = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
      await cdp.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [point] });
      await page.waitForTimeout(800);
      await cdp.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
      await page.locator(openMenu).waitFor();
      await page.waitForTimeout(settleMs);
      assert((await rows(page, usersGrid).nth(1).locator(testId("user-actions")).getAttribute("aria-expanded")) === "true", "The long-press opened another row's menu.");
      assert(!new URL(page.url()).searchParams.has("userId"), "The long-press activated the row.");
      await closeRowMenu(page);

      // A touch that moves is a scroll, not a long-press
      await cdp.send("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [point] });
      await cdp.send("Input.dispatchTouchEvent", { type: "touchMove", touchPoints: [{ x: point.x, y: point.y + 40 }] });
      await page.waitForTimeout(800);
      await cdp.send("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
      await page.waitForTimeout(settleMs);
      assert((await page.locator(openMenu).count()) === 0, "A moving touch opened the row menu.");
      longPress = "touch long-press and right-click";
    }

    await nameCell(page, 2).click({ button: "right" });
    await page.locator(openMenu).waitFor();
    assert((await rows(page, usersGrid).nth(2).locator(testId("user-actions")).getAttribute("aria-expanded")) === "true", "The right-click opened another row's menu.");
    assert(!new URL(page.url()).searchParams.has("userId"), "The right-click activated the row.");
    await closeRowMenu(page);

    await nameCell(page, 3).tap();
    await page.waitForURL(/userId=usr_/);
    await page.locator(`${testId("profile-pane")}:not([hidden])`).waitFor();
    const tapped = new URL(page.url()).searchParams.get("userId");

    // The pane is a modal dialog at this width, so the rows behind it are inert until Escape closes it and returns focus
    await page.keyboard.press("Escape");
    await page.waitForURL((url) => !url.searchParams.has("userId"));
    await page.waitForTimeout(settleMs);
    assert((await focusedRowIndex(page, usersGrid)) === 3, `Closing the pane left focus on row ${await focusedRowIndex(page, usersGrid)}.`);

    await page.keyboard.press("ArrowDown");
    await page.waitForTimeout(settleMs);
    assert((await focusedRowIndex(page, usersGrid)) === 4, `ArrowDown moved focus to ${await focusedRowIndex(page, usersGrid)}.`);
    assert(!new URL(page.url()).searchParams.has("userId"), "ArrowDown activated the row.");
    await page.keyboard.press("Enter");
    await page.waitForURL((url) => url.searchParams.has("userId") && url.searchParams.get("userId") !== tapped);
    await page.keyboard.press("Escape");
    await page.waitForURL((url) => !url.searchParams.has("userId"));
    return { longPress };
  })
);

await check("phone: the sentinel and load more append pages, Back and reload restore the loaded range", () =>
  withPhone(async ({ page, usersRequests }) => {
    await waitPhoneList(page, { loadedCount: 25, pageOffset: 0 });
    const loadMore = page.locator(`${usersGrid} [data-list-load-more]`);
    assert((await loadMore.textContent()).trim() === "Load more", "The load more button has no accessible name.");
    await page.locator(`${usersGrid} [data-list-sentinel]`).scrollIntoViewIfNeeded();
    await waitPhoneList(page, { loadedCount: invitedUsers + 1, pageOffset: 1 });
    await page.waitForURL((url) => url.searchParams.get("pageOffset") === "1");
    assert(pageOffsetRequests(usersRequests, 1) === 1, `The next page was requested ${pageOffsetRequests(usersRequests, 1)} times.`);
    assert((await loadMore.count()) === 0, "The load more button stays after the last page.");
    assert((await page.locator(`${usersGrid} [data-list-load-status]`).textContent()) === "All rows loaded", "The end of the list is not announced.");
    const appendedUrl = page.url();

    // Sorting starts over at the first page with a history entry; Back restores both pages from the cache
    await page.locator(`${usersGrid} [data-list-sort="Name"]`).click();
    await page.waitForURL(/sortOrder=Descending/);
    await waitPhoneList(page, { loadedCount: 25, pageOffset: 0 });
    const requestsBeforeBack = usersRequests.length;
    await page.goBack({ waitUntil: "commit" });
    await page.waitForURL(appendedUrl, { waitUntil: "commit" });
    await waitPhoneList(page, { loadedCount: invitedUsers + 1, pageOffset: 1 });
    assert(usersRequests.length === requestsBeforeBack, `Back made ${usersRequests.length - requestsBeforeBack} users requests.`);

    await page.reload({ waitUntil: "load" });
    await waitPhoneList(page, { loadedCount: invitedUsers + 1, pageOffset: 1 });

    // From the first page again, the load more button is reached and pressed with the keyboard
    await page.goto(usersUrl, { waitUntil: "load" });
    await waitPhoneList(page, { loadedCount: 25, pageOffset: 0 });
    const requestsBeforeLoadMore = pageOffsetRequests(usersRequests, 1);
    await loadMore.focus();
    await page.keyboard.press("Enter");
    await waitPhoneList(page, { loadedCount: invitedUsers + 1, pageOffset: 1 });
    assert(pageOffsetRequests(usersRequests, 1) - requestsBeforeLoadMore === 1, "Load more and the sentinel requested the next page twice.");
    return { usersRequests: usersRequests.length };
  })
);

await browser.close();

mkdirSync(resultsFolder, { recursive: true });
const resultFile = path.join(resultsFolder, `data-list-${options.browser}.json`);
writeFileSync(resultFile, JSON.stringify({ browser: options.browser, finishedAt: new Date().toISOString(), results }, null, 2));
const failed = results.filter((result) => !result.passed);
console.log(`${options.browser}: ${results.length - failed.length} of ${results.length} passed. Result file: ${resultFile}`);
process.exitCode = failed.length === 0 ? 0 : 1;
