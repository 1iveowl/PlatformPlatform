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
// Every case asserts zero content security policy violations, no style attribute inside either list, and no page errors.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started in
// Development.
// Run: dotnet run --project developer-cli -- blazor-harness data-list --browser all

import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { baseUrl, isKnownHostConsoleError, isKnownHostErrorResponse, launchBrowser, newContext, observeErrors, parseArguments, pathBase, resultsFolder, signUpThroughBlazor } from "./support/stack.mjs";

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

async function assertCleanDocument(page, observations, expected) {
  await page.waitForTimeout(settleMs);
  const violations = await page.evaluate(() => window.__policyViolations);
  assert(violations.length === 0, `Policy violations: ${JSON.stringify(violations)}`);
  const styled = await page.locator(`${primary} [style], ${secondary} [style], ${primary}[style], ${secondary}[style]`).count();
  assert(styled === 0, `${styled} elements inside the lists carry a style attribute.`);
  assert(observations.pageErrors.length === 0, `Page errors: ${observations.pageErrors.join(" | ")}`);
  const errorResponses = observations.errorResponses.filter((response) => !isKnownHostErrorResponse(response) && !expected?.response.test(response));
  assert(errorResponses.length === 0, `Error responses: ${errorResponses.join(" | ")}`);
  const expectedCount = observations.errorResponses.filter((response) => expected?.response.test(response)).length;
  const consoleErrors = observations.consoleErrors.filter((message) => !isKnownHostConsoleError(message));
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

await browser.close();

mkdirSync(resultsFolder, { recursive: true });
const resultFile = path.join(resultsFolder, `data-list-${options.browser}.json`);
writeFileSync(resultFile, JSON.stringify({ browser: options.browser, finishedAt: new Date().toISOString(), results }, null, 2));
const failed = results.filter((result) => !result.passed);
console.log(`${options.browser}: ${results.length - failed.length} of ${results.length} passed. Result file: ${resultFile}`);
process.exitCode = failed.length === 0 ? 0 : 1;
