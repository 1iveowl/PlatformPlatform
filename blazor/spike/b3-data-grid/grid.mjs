// Spike code (Blazor edition, stage B3): Playwright harness for the users list on FluentDataGrid and QuickGrid. Not
// production code. Run with node (never the Playwright test runner), one browser at a time:
//   node blazor/spike/b3-data-grid/grid.mjs seed --users 10000
//   node blazor/spike/b3-data-grid/grid.mjs measure --browser chromium
//   node blazor/spike/b3-data-grid/grid.mjs interact --browser chromium
// seed: signs up an owner through the real account API, names the tenant, invites users, logs a sample of them in to give
// them names and an Active status, and promotes a sample to Admin. No database writes; everything goes through the API.
// measure: per grid and per mode (virtualized, paged), first rows, rendered row count, scroll to the end, filter and sort
// requests, CSP violations and page errors.
// interact: keyboard focus and selection, side pane with focus restoration, role change, delete and bulk delete, checked
// against the API afterwards.

import { mkdirSync, readFileSync, writeFileSync, existsSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";

const repositoryRoot = path.resolve(import.meta.dirname, "../../..");
const playwright = createRequire(path.join(repositoryRoot, "application/"))("playwright");
const basePort = readFileSync(path.join(repositoryRoot, ".workspace/port.txt"), "utf8").trim();
const origin = `https://app.dev.localhost:${basePort}`;
const blazor = `${origin}/blazor`;
const verificationCode = "UNLOCK";

const mode = process.argv[2];
const argument = (name, fallback) => (process.argv.includes(`--${name}`) ? process.argv[process.argv.indexOf(`--${name}`) + 1] : fallback);
const browserName = argument("browser", "chromium");
const resultsFolder = path.join(repositoryRoot, ".workspace/experiment-06-blazor/b3-results");
const seedFile = path.join(resultsFolder, "seed.json");
mkdirSync(resultsFolder, { recursive: true });

const grids = (argument("grids", "fluent,quick")).split(",");
const browser = await playwright[browserName].launch();
const result = { mode, browser: browserName, browserVersion: browser.version(), origin, startedAt: new Date().toISOString() };

try {
  if (mode === "seed") result.seed = await seed(Number(argument("users", "10000")), Number(argument("named", "60")), Number(argument("admins", "150")));
  else if (mode === "measure") result.measure = await measure();
  else if (mode === "interact") result.interact = await interact();
  else throw new Error(`Unknown mode '${mode}'.`);
} catch (error) {
  result.error = String(error.stack ?? error).slice(0, 3_000);
  process.exitCode = 1;
} finally {
  await browser.close();
}

result.finishedAt = new Date().toISOString();
const resultFile = path.join(resultsFolder, `${mode}-${browserName}${mode === "seed" ? "" : `-${grids.join("-")}`}${argument("csp-variant", null) ? `-${argument("csp-variant", null)}` : ""}.json`);
writeFileSync(resultFile, JSON.stringify(result, null, 2));
console.log(JSON.stringify(result, null, 2).slice(0, 20_000));
console.log(`\nResult file: ${resultFile}`);

async function newContext() {
  const context = await browser.newContext({ ignoreHTTPSErrors: true, locale: "en-US", viewport: { width: 1440, height: 900 } });
  await context.addInitScript(() => {
    window.__b3Violations = [];
    document.addEventListener("securitypolicyviolation", (event) =>
      window.__b3Violations.push({ directive: event.effectiveDirective, blockedURI: event.blockedURI, sample: event.sample })
    );
  });
  return context;
}

function observe(page) {
  const log = { console: [], pageErrors: [], api: [] };
  page.on("console", (message) => {
    if (message.type() === "error" || message.type() === "warning") log.console.push({ type: message.type(), text: message.text().slice(0, 300) });
  });
  page.on("pageerror", (error) => log.pageErrors.push(String(error.message).slice(0, 300)));
  page.on("requestfinished", async (request) => {
    if (!request.url().includes("/api/account/users")) return;
    const response = await request.response();
    const timing = request.timing();
    log.api.push({ method: request.method(), url: request.url().replace(origin, ""), status: response?.status() ?? null, ms: Math.round(timing.responseEnd) });
  });
  page.__log = log;
  return log;
}

// Node cannot resolve app.dev.localhost, so API calls run as fetch inside a page on the gateway origin
async function api(page, method, url, data) {
  return page.evaluate(
    async ({ method, url, data }) => {
      const bootstrap = await (await fetch("/blazor/api/bootstrap")).json();
      const response = await fetch(url, {
        method,
        credentials: "same-origin",
        body: data === undefined ? undefined : JSON.stringify(data),
        headers: { "content-type": "application/json", "x-xsrf-token": bootstrap.antiforgeryToken }
      });
      const text = await response.text();
      let body = null;
      try {
        body = text ? JSON.parse(text) : null;
      } catch {
        body = text.slice(0, 300);
      }
      return { status: response.status, body };
    },
    { method, url, data }
  );
}

async function loginThroughApi(page, email, signup = false) {
  const kind = signup ? "signup" : "login";
  const start = await api(page, "POST", `/api/account/authentication/email/${kind}/start`, { email });
  if (start.status !== 200) throw new Error(`${kind} start for ${email} returned ${start.status}: ${JSON.stringify(start.body)}`);
  const id = start.body.emailLoginId;
  const complete = await api(page, "POST", `/api/account/authentication/email/${kind}/${id}/complete`, { oneTimePassword: verificationCode });
  if (complete.status !== 200) throw new Error(`${kind} complete for ${email} returned ${complete.status}: ${JSON.stringify(complete.body)}`);
}

async function inParallel(items, concurrency, work) {
  let next = 0;
  const failures = [];
  await Promise.all(
    Array.from({ length: concurrency }, async () => {
      while (next < items.length) {
        const item = items[next++];
        try {
          await work(item);
        } catch (error) {
          failures.push(String(error.message ?? error).slice(0, 200));
        }
      }
    })
  );
  return failures;
}

async function seed(userCount, namedCount, adminCount) {
  const stamp = Date.now();
  const ownerEmail = `b3-owner-${stamp}@example.com`;
  const context = await newContext();
  const page = await context.newPage();
  await page.goto(`${blazor}/`);
  await loginThroughApi(page, ownerEmail, true);
  const nameTenant = await api(page, "PUT", "/api/account/tenants/current", { name: `B3 grid tenant ${stamp}` });
  const nameOwner = await api(page, "PUT", "/api/account/users/me", { firstName: "Olivia", lastName: "Owner", title: "Owner" });

  const firstNames = ["Anna", "Bo", "Carl", "Dina", "Emil", "Freja", "Gustav", "Hanne", "Ib", "Jonas", "Karen", "Lars", "Maja", "Niels", "Ole", "Pia"];
  const lastNames = ["Andersen", "Berg", "Christensen", "Dahl", "Eriksen", "Frost", "Holm", "Jensen", "Knudsen", "Larsen", "Madsen", "Nielsen"];
  const emails = Array.from({ length: userCount }, (_, index) => `b3-user-${stamp}-${String(index).padStart(5, "0")}@example.com`);

  const inviteStarted = Date.now();
  const inviteFailures = await inParallel(emails, 8, async (email) => {
    const response = await api(page, "POST", "/api/account/users/invite", { email });
    if (response.status !== 200) throw new Error(`invite ${email}: ${response.status} ${JSON.stringify(response.body).slice(0, 120)}`);
  });
  const inviteMs = Date.now() - inviteStarted;

  const listed = await api(page, "GET", `/api/account/users?PageSize=1000&OrderBy=Email`);
  const adminIds = (listed.body?.users ?? []).filter((user) => user.email.startsWith("b3-user-")).slice(0, adminCount).map((user) => user.id);
  const adminFailures = await inParallel(adminIds, 8, async (id) => {
    const response = await api(page, "PUT", `/api/account/users/${id}/change-user-role`, { userRole: "Admin" });
    if (response.status !== 200) throw new Error(`role ${id}: ${response.status}`);
  });

  // Invitees become Active and get names by logging in themselves, each in its own context
  const namedEmails = emails.filter((_, index) => index % Math.max(1, Math.floor(userCount / namedCount)) === 0).slice(0, namedCount);
  const namedFailures = await inParallel(namedEmails, 4, async (email) => {
    const inviteeContext = await newContext();
    try {
      const inviteePage = await inviteeContext.newPage();
      await inviteePage.goto(`${blazor}/`);
      await loginThroughApi(inviteePage, email);
      const index = Number(email.match(/-(\d{5})@/)[1]);
      const update = await api(inviteePage, "PUT", "/api/account/users/me", {
        firstName: firstNames[index % firstNames.length],
        lastName: lastNames[index % lastNames.length],
        title: index % 3 === 0 ? "Engineer" : "Consultant"
      });
      if (update.status !== 200) throw new Error(`name ${email}: ${update.status}`);
    } finally {
      await inviteeContext.close();
    }
  });

  const summary = await api(page, "GET", "/api/account/users/summary");
  const seedRecord = { stamp, ownerEmail, userCount, invitedAt: new Date().toISOString() };
  writeFileSync(seedFile, JSON.stringify(seedRecord, null, 2));
  await context.close();
  return {
    ...seedRecord,
    nameTenantStatus: nameTenant.status,
    nameOwnerStatus: nameOwner.status,
    inviteMs,
    inviteFailures: inviteFailures.slice(0, 10),
    inviteFailureCount: inviteFailures.length,
    adminPromoted: adminIds.length - adminFailures.length,
    adminFailures: adminFailures.slice(0, 5),
    named: namedEmails.length - namedFailures.length,
    namedFailures: namedFailures.slice(0, 5),
    summary: summary.body
  };
}

function loadSeed() {
  if (!existsSync(seedFile)) throw new Error("Run the seed mode first.");
  return JSON.parse(readFileSync(seedFile, "utf8"));
}

async function ownerPage() {
  const { ownerEmail } = loadSeed();
  const context = await newContext();
  const page = await context.newPage();
  observe(page);
  await page.goto(`${blazor}/`);
  await loginThroughApi(page, ownerEmail);
  return { context, page };
}

// --csp-variant style-attr-unsafe-inline loads every grid page under the policy plus style-src-attr 'unsafe-inline'
function gridUrl(grid, query = "") {
  const cspVariant = argument("csp-variant", null);
  const variant = cspVariant ? `csp-variant=${cspVariant}` : "";
  return `${blazor}/app/users/${grid}${query}${variant ? `${query ? "&" : "?"}${variant}` : ""}`;
}

async function waitGridReady(page) {
  await page.waitForFunction(() => document.querySelector('[data-testid="users-grid"]')?.dataset.state === "ready", null, { timeout: 60_000 });
}

async function gridState(page) {
  return page.evaluate(() => {
    const root = document.querySelector('[data-testid="users-grid"]');
    const rows = root ? [...root.querySelectorAll("[data-user-row]")] : [];
    const scroller = root?.querySelector('[data-testid="grid-scroll"]');
    return {
      state: root?.dataset.state ?? null,
      totalCount: Number(root?.dataset.totalCount ?? -1),
      renderedRows: rows.length,
      firstRowEmail: rows[0]?.dataset.email ?? null,
      lastRowEmail: rows.at(-1)?.dataset.email ?? null,
      elementsInGrid: root ? root.querySelectorAll("*").length : 0,
      scrollHeight: scroller?.scrollHeight ?? null,
      clientHeight: scroller?.clientHeight ?? null,
      inlineStyleAttributes: root ? root.querySelectorAll("[style]").length : 0,
      violations: window.__b3Violations.length
    };
  });
}

function violationSummary(violations) {
  const byDirective = {};
  for (const violation of violations) byDirective[violation.directive] = (byDirective[violation.directive] ?? 0) + 1;
  return { total: violations.length, byDirective, samples: [...new Set(violations.map((violation) => `${violation.directive} ${violation.blockedURI} ${violation.sample ?? ""}`))].slice(0, 6) };
}

async function scrollToEnd(page) {
  // Scrolls the grid's scroll container in steps like a user flinging the wheel, until the last row is rendered
  return page.evaluate(async () => {
    const root = document.querySelector('[data-testid="users-grid"]');
    const scroller = root.querySelector('[data-testid="grid-scroll"]');
    const total = Number(root.dataset.totalCount);
    const started = performance.now();
    const frameGaps = [];
    let last = performance.now();
    let steps = 0;
    let running = true;
    const sampleFrames = () => {
      const now = performance.now();
      frameGaps.push(now - last);
      last = now;
      if (running) requestAnimationFrame(sampleFrames);
    };
    requestAnimationFrame(sampleFrames);
    const lastRendered = () => [...root.querySelectorAll("[data-user-row]")].some((row) => Number(row.dataset.index) === total - 1);
    while (!lastRendered() && performance.now() - started < 120_000) {
      scroller.scrollTop = scroller.scrollTop + scroller.clientHeight * 3;
      steps++;
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
    running = false;
    const sorted = [...frameGaps].sort((a, b) => a - b);
    return {
      reachedEnd: lastRendered(),
      ms: Math.round(performance.now() - started),
      steps,
      frames: frameGaps.length,
      frameGapP50: Math.round(sorted[Math.floor(sorted.length * 0.5)] ?? 0),
      frameGapP95: Math.round(sorted[Math.floor(sorted.length * 0.95)] ?? 0),
      frameGapMax: Math.round(sorted.at(-1) ?? 0),
      renderedRowsAtEnd: root.querySelectorAll("[data-user-row]").length
    };
  });
}

async function measure() {
  const { context, page } = await ownerPage();
  const measurements = [];
  try {
    for (const grid of grids) {
      for (const listMode of ["virtual", "paged"]) {
        const log = page.__log;
        log.console.length = 0;
        log.pageErrors.length = 0;
        log.api.length = 0;
        const entry = { grid, listMode };
        measurements.push(entry);
        try {
        const started = Date.now();
        await page.goto(gridUrl(grid, `?mode=${listMode}`));
        await waitGridReady(page);
        const firstRowsMs = Date.now() - started;
        const initial = await gridState(page);
        Object.assign(entry, { firstRowsMs, initial });

        if (listMode === "virtual") {
          entry.scroll = await scrollToEnd(page);
          entry.afterScroll = await gridState(page);
          entry.apiCallsDuringScroll = log.api.filter((call) => call.method === "GET").length;
        } else {
          // FluentPaginator renders buttons; QuickGrid's Paginator on .NET 11 renders links carrying ?page= in the URL
          const pageButton = page.getByRole("button", { name: /next/i }).or(page.getByRole("link", { name: /next page/i })).first();
          const pageStarted = Date.now();
          await pageButton.click();
          await page.waitForFunction(() => document.querySelector('[data-testid="users-grid"]')?.dataset.pageIndex === "1", null, { timeout: 30_000 });
          await waitGridReady(page);
          entry.nextPageMs = Date.now() - pageStarted;
          entry.afterNextPage = await gridState(page);
        }

        // Server-side search, enum filters and sort, each observed as a request with the right query
        const searchStarted = Date.now();
        await page.locator('[data-testid="search"] input, input[data-testid="search"]').first().fill("b3-user");
        await page.waitForFunction(() => document.querySelector('[data-testid="users-grid"]')?.dataset.query?.includes("Search=b3-user"), null, { timeout: 30_000 });
        await waitGridReady(page);
        entry.search = { ms: Date.now() - searchStarted, state: await gridState(page) };

        await page.goto(gridUrl(grid, `?mode=${listMode}&userRole=Admin&orderBy=Email&sortOrder=Descending`));
        await waitGridReady(page);
        entry.urlFilter = { state: await gridState(page), lastQuery: log.api.filter((call) => call.method === "GET").at(-1)?.url ?? null };

        const sortStarted = Date.now();
        await page.locator('[data-testid="users-grid"] th').filter({ hasText: "Created" }).locator("button, fluent-button, a.col-title").first().click();
        await page.waitForFunction(() => document.querySelector('[data-testid="users-grid"]')?.dataset.query?.includes("OrderBy=CreatedAt"), null, { timeout: 30_000 });
        await waitGridReady(page);
        entry.sortByHeader = { ms: Date.now() - sortStarted, url: page.url().replace(origin, ""), lastQuery: log.api.filter((call) => call.method === "GET").at(-1)?.url ?? null };

        } catch (error) {
          entry.error = String(error.message ?? error).slice(0, 500);
          entry.errorAtUrl = page.url().replace(origin, "");
          entry.stateAtError = await gridState(page).catch(() => null);
        }
        entry.violations = violationSummary(await page.evaluate(() => window.__b3Violations));
        entry.pageErrors = [...log.pageErrors];
        entry.console = log.console.slice(0, 8);
        entry.apiStatusCodes = [...new Set(log.api.map((call) => call.status))];
        entry.apiGetMedianMs = median(log.api.filter((call) => call.method === "GET").map((call) => call.ms));
        await page.evaluate(() => (window.__b3Violations.length = 0));
      }
    }
  } finally {
    await context.close();
  }
  return measurements;
}

function median(values) {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.floor(sorted.length / 2)];
}

async function focused(page) {
  return page.evaluate(() => {
    const element = document.activeElement;
    const row = element?.closest("tr, [role='row']")?.querySelector("[data-user-row]");
    return {
      tag: element?.tagName?.toLowerCase() ?? null,
      testId: element?.dataset?.testid ?? null,
      role: element?.getAttribute("role") ?? null,
      rowEmail: row?.dataset.email ?? null,
      rowIndex: row ? Number(row.dataset.index) : null
    };
  });
}

function rowOf(page, email) {
  return page.locator('[data-testid="users-grid"] tbody tr').filter({ has: page.locator(`[data-user-row][data-email="${email}"]`) });
}

async function interact() {
  const { context, page } = await ownerPage();
  const { stamp } = loadSeed();
  const outcome = [];
  try {
    for (const grid of grids) {
      const steps = [];
      const record = (step, data) => steps.push({ step, ...data });
      outcome.push({ grid, steps });
      try {
      page.__log.pageErrors.length = 0;
      await page.evaluate(() => (window.__b3Violations.length = 0)).catch(() => {});

      const pane = page.locator('[data-testid="profile-pane"]');
      const selectedCount = () => page.evaluate(() => document.querySelector('[data-testid="users-grid"]').dataset.selectedCount);
      for (const listMode of ["virtual", "paged"]) {
        // Narrow the list to this run's invitees so mutations hit known rows
        await page.goto(gridUrl(grid, `?mode=${listMode}&search=b3-user-${stamp}&orderBy=Email`));
        await waitGridReady(page);
        record(`${listMode}: loaded`, await gridState(page));

        // Keyboard: tab from the search box into the grid, then move between rows
        await page.locator('[data-testid="search"] input, input[data-testid="search"]').first().focus();
        const tabs = [];
        for (let index = 0; index < 10; index++) {
          await page.keyboard.press("Tab");
          const now = await focused(page);
          await page.waitForTimeout(150);
          tabs.push(now.tag);
          if (now.rowEmail) break;
        }
        record(`${listMode}: tab into grid`, { tabStops: tabs, focus: await focused(page) });
        await page.keyboard.press("ArrowDown");
        await page.waitForTimeout(400);
        record(`${listMode}: ArrowDown`, { focus: await focused(page) });
        await page.keyboard.press("ArrowDown");
        await page.waitForTimeout(400);
        record(`${listMode}: ArrowDown again`, { focus: await focused(page) });
        await page.keyboard.press("Space");
        await page.waitForTimeout(400);
        record(`${listMode}: Space`, { focus: await focused(page), selected: await selectedCount(), paneVisible: await pane.isVisible() });

        // Enter opens the side pane; Escape closes it and focus returns to the row
        const beforeEnter = await focused(page);
        await page.keyboard.press("Enter");
        const paneOpened = await pane.waitFor({ state: "visible", timeout: 5_000 }).then(() => true).catch(() => false);
        record(`${listMode}: Enter`, { before: beforeEnter, paneOpened, paneEmail: paneOpened ? await pane.getAttribute("data-email") : null, selected: await selectedCount(), url: page.url().replace(origin, ""), focus: await focused(page) });
        await page.keyboard.press("Escape");
        await pane.waitFor({ state: "hidden", timeout: 5_000 }).catch(() => {});
        await page.waitForTimeout(400);
        record(`${listMode}: Escape closes pane, focus restored`, { paneHidden: !(await pane.isVisible().catch(() => false)), focus: await focused(page), expectedRow: beforeEnter.rowEmail, url: page.url().replace(origin, "") });
      }

      // Deep link to a profile: a row inside the first fetched page (index 30) and one beyond it (index 400)
      for (const deepIndex of ["00030", "00400"]) {
        const deepTarget = `b3-user-${stamp}-${deepIndex}@example.com`;
        const deepUser = await api(page, "GET", `/api/account/users?Search=${encodeURIComponent(deepTarget)}`);
        const deepId = deepUser.body?.users?.[0]?.id;
        await page.goto(gridUrl(grid, `?mode=virtual&search=b3-user-${stamp}&orderBy=Email&userId=${deepId}`));
        await waitGridReady(page);
        await pane.waitFor({ state: "visible", timeout: 15_000 }).catch(() => {});
        await page.waitForTimeout(1_500);
        record(`deep link to userId, row index ${Number(deepIndex)}`, {
          paneEmail: await pane.getAttribute("data-email").catch(() => null),
          rowRendered: await page.locator(`[data-user-row][data-email="${deepTarget}"]`).count(),
          rowSelected: await page.locator(`[data-user-row][data-email="${deepTarget}"][data-selected="true"]`).count(),
          notInViewNotice: await page.locator('[data-testid="not-in-view"]').count()
        });
        await page.keyboard.press("Escape");
      }

      // Role change through the row action menu and dialog, checked against the API
      // Filter dialog: role and date range through the dialog, active count badge, then clear
      await page.goto(gridUrl(grid, `?mode=virtual&search=b3-user-${stamp}`));
      await waitGridReady(page);
      await page.locator('[data-testid="filters"]').click();
      const filterDialog = page.locator('[data-testid="filter-dialog"]');
      const filterDialogOpened = await filterDialog.waitFor({ state: "visible", timeout: 10_000 }).then(() => true).catch(() => false);
      await filterDialog.locator('[data-testid="filter-role"]').click();
      await filterDialog.locator('fluent-option[value="Admin"]').click();
      await filterDialog.locator('[data-testid="filter-start"]').fill("2020-01-01");
      await filterDialog.locator('[data-testid="filter-end"]').fill("2099-12-31");
      await filterDialog.locator('[data-testid="filter-apply"]').click();
      await page.waitForFunction(() => document.querySelector('[data-testid="users-grid"]')?.dataset.query?.includes("UserRole=Admin"), null, { timeout: 15_000 }).catch(() => {});
      await waitGridReady(page);
      const filtered = await gridState(page);
      const adminCheck = await api(page, "GET", `/api/account/users?Search=b3-user-${stamp}&UserRole=Admin&StartDate=2020-01-01&EndDate=2099-12-31&PageSize=1`);
      record("filter dialog", {
        filterDialogOpened,
        query: await page.evaluate(() => document.querySelector('[data-testid="users-grid"]').dataset.query),
        url: page.url().replace(origin, ""),
        badge: await page.locator('[data-testid="filter-count"]').textContent().catch(() => null),
        gridTotal: filtered.totalCount,
        apiTotal: adminCheck.body?.totalCount,
        rowsAllAdmin: await page.evaluate(() => [...document.querySelectorAll("[data-user-row]")].every((row) => row.dataset.role === "Admin"))
      });
      await page.locator('[data-testid="clear-filters"]').click();
      await page.waitForFunction(() => !document.querySelector('[data-testid="users-grid"]')?.dataset.query?.includes("UserRole"), null, { timeout: 15_000 }).catch(() => {});
      await waitGridReady(page);
      record("clear filters", { query: await page.evaluate(() => document.querySelector('[data-testid="users-grid"]').dataset.query), gridTotal: (await gridState(page)).totalCount });

      // Targets are picked from users that still exist and are Members, so repeated runs never reuse a deleted or changed user
      const poolPrefix = `b3-user-${stamp}-0${grid === "fluent" ? "5" : "6"}`;
      const pool = (await api(page, "GET", `/api/account/users?Search=${poolPrefix}&UserRole=Member&OrderBy=Email&PageSize=50`)).body.users.map((user) => user.email);
      const roleTarget = pool[0];
      await page.goto(gridUrl(grid, `?mode=virtual&search=${encodeURIComponent(roleTarget)}`));
      await waitGridReady(page);
      const roleMarker = page.locator(`[data-user-row][data-email="${roleTarget}"]`);
      await rowOf(page, roleTarget).locator('[data-testid="row-actions"]').click();
      await page.locator('[data-testid="action-change-role"]').click();
      const roleDialog = page.locator('[data-testid="role-dialog"]');
      await roleDialog.waitFor({ state: "visible", timeout: 10_000 });
      const roleFocus = await focused(page);
      await roleDialog.locator('fluent-radio[value="Admin"]').click();
      await page.locator('[data-testid="role-confirm"]').click();
      await roleDialog.waitFor({ state: "hidden", timeout: 10_000 }).catch(() => {});
      await page.waitForFunction((email) => document.querySelector(`[data-user-row][data-email="${email}"]`)?.dataset.role === "Admin", roleTarget, { timeout: 10_000 }).catch(() => {});
      const roleApi = await api(page, "GET", `/api/account/users?Search=${encodeURIComponent(roleTarget)}`);
      record("change role", { focusInDialog: roleFocus, rowRole: await roleMarker.getAttribute("data-role").catch(() => null), apiRole: roleApi.body?.users?.[0]?.role, focusAfterClose: await focused(page) });

      // Single delete through the row menu
      const deleteTarget = pool[1];
      await page.goto(gridUrl(grid, `?mode=virtual&search=${encodeURIComponent(deleteTarget)}`));
      await waitGridReady(page);
      await rowOf(page, deleteTarget).locator('[data-testid="row-actions"]').click();
      await page.locator('[data-testid="action-delete"]').click();
      await page.locator('[data-testid="delete-dialog"]').waitFor({ state: "visible", timeout: 10_000 });
      await page.locator('[data-testid="delete-confirm"]').click();
      await page.waitForFunction(() => document.querySelector('[data-testid="users-grid"]')?.dataset.totalCount === "0", null, { timeout: 15_000 }).catch(() => {});
      const deleteApi = await api(page, "GET", `/api/account/users?Search=${encodeURIComponent(deleteTarget)}`);
      record("delete one", { gridTotal: (await gridState(page)).totalCount, apiTotal: deleteApi.body?.totalCount });

      // Bulk delete: select three rows with the select column, then delete from the toolbar
      const bulkPrefix = `b3-user-${stamp}-0${grid === "fluent" ? "7" : "8"}`;
      await page.goto(gridUrl(grid, `?mode=virtual&search=${encodeURIComponent(bulkPrefix)}&orderBy=Email`));
      await waitGridReady(page);
      const before = await gridState(page);
      const bulkRows = page.locator('[data-testid="users-grid"] tbody tr').filter({ has: page.locator("[data-user-row]") });
      // FluentDataGrid's SelectColumn toggles on a click anywhere in its cell; the QuickGrid column is a plain checkbox
      const selectCells = grid === "quick" ? bulkRows.locator('[data-testid="row-select"]') : bulkRows.locator("td:first-child");
      for (let index = 0; index < 3; index++) await selectCells.nth(index).click();
      const bulkSelected = await selectedCount();
      await page.locator('[data-testid="bulk-delete"]').click();
      await page.locator('[data-testid="delete-dialog"]').waitFor({ state: "visible", timeout: 10_000 });
      await page.locator('[data-testid="delete-confirm"]').click();
      await page.waitForFunction((expected) => Number(document.querySelector('[data-testid="users-grid"]')?.dataset.totalCount) === expected, before.totalCount - 3, { timeout: 15_000 }).catch(() => {});
      const bulkApi = await api(page, "GET", `/api/account/users?Search=${encodeURIComponent(bulkPrefix)}`);
      record("bulk delete three", { before: before.totalCount, selectedCount: bulkSelected, gridAfter: (await gridState(page)).totalCount, apiAfter: bulkApi.body?.totalCount });

      } catch (error) {
        record("failed", { error: String(error.message ?? error).slice(0, 400), url: page.url().replace(origin, ""), focus: await focused(page).catch(() => null) });
      }
      record("errors", { violations: violationSummary(await page.evaluate(() => window.__b3Violations)), pageErrors: [...page.__log.pageErrors] });
    }
  } finally {
    await context.close();
  }
  return outcome;
}
