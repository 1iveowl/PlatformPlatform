// The authenticated surfaces of the Blazor edition as a user sees them, shared by the local gated check
// (authenticated-surfaces.mjs) and the staging acceptance command (staging-acceptance.mjs). Each surface is opened in a
// fresh context of the signed-in identity at desktop and at phone width, and must:
//
// - render its content: chart cards with their charts, list rows, a detail header or the page's own form;
// - scroll with the mouse wheel when the document is taller than the window. Playwright scrolls an element into view
//   before it acts on it, so a click-driven test cannot notice a document the user cannot scroll (EP-217 finding F-3);
// - report no content security policy violation, console error, page error or HTTP error response.
//
// A surface marked mustScroll must be taller than the window at that width, so the scroll assertion can never pass by
// finding nothing to scroll. They are the surfaces taller than the window by their own content, whatever the data: on an
// empty database on 2026-09-30, the dashboard was 1,999 px at desktop and 3,884 px at phone width, the feature flag list
// 1,432 and 1,766 px and the app home 842 and 1,125 px, in windows of 720 and 844 px.

import { newContext, observeErrors, pathBase, policyViolationsOf, requestsInFlight, strictFailures } from "./stack.mjs";

export const surfaceViewports = [
  { name: "desktop", width: 1280, height: 720 },
  { name: "phone", width: 390, height: 844 }
];

const readyTimeoutMs = 60_000;

// The chart cards the dashboard always shows, and those it shows only with the subscription setting on
const chartsAlwaysShown = ["account-growth-chart", "user-logins-chart"];
const chartsWithSubscription = ["mrr-trend-chart", "revenue-trend-chart", "plan-distribution-chart"];

async function waitForBackOfficeShell(page) {
  await page.locator('[data-testid="back-office-shell"][data-identity-state="loaded"]').waitFor({ timeout: readyTimeoutMs });
}

async function waitForAppShell(page) {
  await page.locator('[data-testid="app-shell"][data-tenants-state="loaded"]').waitFor({ timeout: readyTimeoutMs });
}

// Waits for a DataList to finish loading and returns its state and loaded row count
async function readList(page, testId) {
  const list = page.locator(`.data-list[data-testid="${testId}"]`);
  await list.waitFor({ timeout: readyTimeoutMs });
  await page.waitForFunction(
    (selector) => ["ready", "empty", "error"].includes(document.querySelector(selector)?.getAttribute("data-list-state")),
    `.data-list[data-testid="${testId}"]`,
    { timeout: readyTimeoutMs }
  );
  return { state: await list.getAttribute("data-list-state"), rows: Number(await list.getAttribute("data-list-loaded-count")) };
}

async function expectListRows(page, testId) {
  const list = await readList(page, testId);
  if (list.state !== "ready" || list.rows < 1) throw new Error(`The list ${testId} is ${list.state} with ${list.rows} rows.`);
  return { [testId]: list };
}

async function expectVisible(page, testId) {
  await page.locator(`[data-testid="${testId}"]`).first().waitFor({ state: "visible", timeout: readyTimeoutMs });
  return { [testId]: "visible" };
}

// Every card has finished loading, and every chart card shown renders its chart with a data row per point
async function expectDashboard(page) {
  await expectVisible(page, "back-office-dashboard");
  await page.waitForFunction(() => document.querySelectorAll("section.dashboard-card").length > 0 && document.querySelectorAll('section.dashboard-card[data-state="loading"]').length === 0, null, {
    timeout: readyTimeoutMs
  });
  const charts = await page.evaluate(() =>
    [...document.querySelectorAll("[data-chart]")].map((chart) => ({
      testId: chart.getAttribute("data-testid"),
      kind: chart.getAttribute("data-chart"),
      hasSvg: chart.querySelector("svg") !== null,
      rows: document.querySelectorAll(`[data-testid="${chart.getAttribute("data-testid")}-row"]`).length
    }))
  );
  const cards = await page.evaluate(() => [...document.querySelectorAll("section.dashboard-card")].map((card) => `${card.getAttribute("data-testid")}:${card.getAttribute("data-state")}`));
  const rendered = new Map(charts.map((chart) => [chart.testId, chart]));
  const missing = chartsAlwaysShown.filter((testId) => !rendered.get(testId)?.hasSvg || rendered.get(testId).rows < 1);
  const broken = charts.filter((chart) => chartsWithSubscription.includes(chart.testId) && (!chart.hasSvg || chart.rows < 1));
  if (missing.length > 0 || broken.length > 0) throw new Error(`Chart cards without a rendered chart: ${[...missing, ...broken.map((chart) => chart.testId)].join(", ")}. Cards: ${cards.join(", ")}.`);
  return { charts: charts.map((chart) => `${chart.testId} (${chart.kind}, ${chart.rows} points)`), cards };
}

// The back-office surfaces. ids names the account, user and flag whose detail pages are opened.
export function backOfficeSurfaces(ids) {
  return [
    { name: "back-office dashboard", route: "back-office", mustScroll: ["desktop", "phone"], ready: async (page) => (await waitForBackOfficeShell(page), expectDashboard(page)) },
    { name: "back-office accounts", route: "back-office/accounts", ready: async (page) => (await waitForBackOfficeShell(page), expectListRows(page, "accounts-grid")) },
    {
      name: "back-office account detail",
      route: `back-office/accounts/${ids.tenantId}`,
      ready: async (page) => (
        await waitForBackOfficeShell(page),
        { ...(await expectVisible(page, "account-detail-header")), ...(await expectVisible(page, "account-detail-tiles")), ...(await expectVisible(page, "account-owners")) }
      )
    },
    { name: "back-office users", route: "back-office/users", ready: async (page) => (await waitForBackOfficeShell(page), expectListRows(page, "users-grid")) },
    {
      name: "back-office user detail",
      route: `back-office/users/${encodeURIComponent(ids.userId)}`,
      ready: async (page) => (await waitForBackOfficeShell(page), { ...(await expectVisible(page, "user-detail-header")), ...(await expectVisible(page, "user-tabs")) })
    },
    {
      name: "back-office feature flags",
      route: "back-office/feature-flags",
      mustScroll: ["desktop", "phone"],
      ready: async (page) => (await waitForBackOfficeShell(page), { ...(await expectVisible(page, "feature-flag-row")), rows: await page.locator('[data-testid="feature-flag-row"]').count() })
    },
    {
      name: "back-office feature flag detail",
      route: `back-office/feature-flags/${encodeURIComponent(ids.flagKey)}`,
      ready: async (page) => (await waitForBackOfficeShell(page), expectVisible(page, "feature-flag-header"))
    }
  ];
}

// The app edition's authenticated pages, for a signed-in account owner
export function appSurfaces() {
  return [
    { name: "app home", route: "app", mustScroll: ["desktop", "phone"], ready: async (page) => (await waitForAppShell(page), expectVisible(page, "app-shell")) },
    { name: "app details", route: "app/details", ready: async (page) => (await waitForAppShell(page), expectVisible(page, "app-shell")) },
    { name: "app users", route: "account/users", ready: async (page) => (await waitForAppShell(page), expectListRows(page, "users-grid")) },
    { name: "app recycle bin", route: "account/users/recycle-bin", ready: async (page) => (await waitForAppShell(page), { "deleted-users-grid": await readList(page, "deleted-users-grid") }) },
    { name: "app account settings", route: "account/settings", ready: async (page) => (await waitForAppShell(page), expectVisible(page, "account-settings-form")) },
    { name: "app profile", route: "user/profile", ready: async (page) => (await waitForAppShell(page), expectVisible(page, "profile-form")) },
    { name: "app preferences", route: "user/preferences", ready: async (page) => (await waitForAppShell(page), expectVisible(page, "preferences-theme")) },
    { name: "app sessions", route: "user/sessions", ready: async (page) => (await waitForAppShell(page), expectVisible(page, "session-card")) }
  ];
}

// Wheels over the middle of the window and returns how far the document scrolled; fails when it does not move
export async function wheelScroll(page, viewport) {
  const [scrollHeight, innerHeight] = await page.evaluate(() => [document.scrollingElement.scrollHeight, window.innerHeight]);
  if (scrollHeight <= innerHeight) throw new Error(`The page is not taller than the window at ${viewport.width} px (${scrollHeight} of ${innerHeight}).`);
  await page.mouse.move(viewport.width / 2, viewport.height / 2);
  await page.mouse.wheel(0, 300);
  await page.waitForFunction(() => window.scrollY > 0, null, { timeout: 5_000 }).catch((error) => {
    throw new Error(`The wheel did not scroll the document at ${viewport.width} px (${scrollHeight} of ${innerHeight}): ${error.message}`);
  });
  // WebKit animates a wheel scroll, so the position is read once it stops changing
  let previous;
  let scrollY = await page.evaluate(() => window.scrollY);
  do {
    previous = scrollY;
    await page.waitForTimeout(200);
    scrollY = await page.evaluate(() => window.scrollY);
  } while (scrollY !== previous);
  return scrollY;
}

// Lets the context's requests finish, then closes its pages, so a cookie snapshot taken afterwards is not missing the
// Set-Cookie of a request still in flight. The load state is not enough: a page that has been idle once reports network
// idle at once, while the WebAssembly runtime can still start a fetch that rotates the refresh token (observed on staging
// on 2026-09-30: the rotation one second after the snapshot, then "Replay attack detected" on the next run). Waits for one
// quiet second with nothing in flight, at most 20 s; a deployed run reconciles the session afterwards as well
// (reconcileAppSession in deployed.mjs), because a request started after the quiet second is still possible.
export async function settleAndClosePages(context) {
  const deadline = Date.now() + 20_000;
  let quietSince = requestsInFlight(context) === 0 ? Date.now() : undefined;
  while (Date.now() < deadline && (quietSince === undefined || Date.now() - quietSince < 1_000)) {
    await new Promise((resolve) => setTimeout(resolve, 100));
    if (requestsInFlight(context) > 0) quietSince = undefined;
    else quietSince ??= Date.now();
  }
  for (const page of context.pages()) await page.close();
}

// Opens one surface at one width in a fresh context of the identity and returns the case result. The context's cookies are
// handed on before it closes, because the account API rotates the refresh token and revokes a session whose previous token
// comes back, so every next context has to start from the cookies the last one ended with.
async function checkSurface(target, surface, viewport) {
  const { browser, browserName, origin, contextOptions } = target;
  const name = `${surface.name} at ${viewport.name} width`;
  const context = await newContext(browser, browserName, target.storageState, "en-US", { viewport: { width: viewport.width, height: viewport.height }, ...contextOptions });
  try {
    const page = await context.newPage();
    const observations = observeErrors(page);
    const url = `${origin}${pathBase}/${surface.route}`;
    const response = await page.goto(url, { waitUntil: "load" });
    if (response?.status() !== 200) throw new Error(`${url} answered ${response?.status()}.`);
    if (new URL(page.url()).pathname !== new URL(url).pathname) throw new Error(`${url} ended on ${page.url()}.`);
    const content = await surface.ready(page);

    const [scrollHeight, innerHeight] = await page.evaluate(() => [document.scrollingElement.scrollHeight, window.innerHeight]);
    const taller = scrollHeight > innerHeight;
    if (!taller && surface.mustScroll?.includes(viewport.name)) throw new Error(`The page is not taller than the window at ${viewport.width} px (${scrollHeight} of ${innerHeight}), so its scrolling was not tested.`);
    const scrollY = taller ? await wheelScroll(page, viewport) : null;

    const failures = strictFailures(name, observations, policyViolationsOf(context));
    if (failures.length > 0) throw new Error(`${failures.join("; ")}: ${JSON.stringify({ ...observations, violations: policyViolationsOf(context) })}`);
    return { name, passed: true, detail: { url, content, scrollHeight, innerHeight, scrolledBy: scrollY } };
  } catch (error) {
    return { name, passed: false, detail: error.message };
  } finally {
    await settleAndClosePages(context);
    await target.beforeSnapshot?.(context);
    target.storageState = await context.storageState();
    await target.onStorageState?.(target.storageState);
    await context.close();
  }
}

// Runs every surface at every width and returns the case results in order. target: the browser, its name, the origin the
// routes are opened on, the identity's storage state, further context options (a deployed host verifies certificates) and
// an optional beforeSnapshot hook that runs on each context before its cookies are taken, and an optional onStorageState
// callback that receives the cookies after every context, to keep a stored session current.
// target.storageState holds the last context's cookies afterwards.
export async function checkSurfaces(target, surfaces, viewports = surfaceViewports) {
  const results = [];
  for (const surface of surfaces) {
    for (const viewport of viewports) {
      const result = await checkSurface(target, surface, viewport);
      console.log(`${result.passed ? "PASS" : "FAIL"} ${result.name}${result.passed ? "" : `: ${result.detail}`}`);
      results.push(result);
    }
  }
  return results;
}

// Reads the ids of the first account, user and flag through the back-office API from a signed-in back-office document, so
// the detail pages open on real records on any host
export async function readBackOfficeIds(page) {
  return page.evaluate(async () => {
    const read = async (url) => {
      const response = await fetch(url, { credentials: "same-origin", headers: { Accept: "application/json" } });
      if (!response.ok) throw new Error(`${url} answered ${response.status}`);
      return response.json();
    };
    const tenants = await read("/api/back-office/tenants?OrderBy=CreatedAt&SortOrder=Descending&PageSize=1");
    const users = await read("/api/back-office/users?OrderBy=CreatedAt&SortOrder=Descending&PageSize=1");
    const flags = await read("/api/back-office/feature-flags?IncludeDeleted=false");
    return { tenantId: tenants.tenants[0]?.id, userId: users.users[0]?.id, flagKey: flags.flags[0]?.key };
  });
}
