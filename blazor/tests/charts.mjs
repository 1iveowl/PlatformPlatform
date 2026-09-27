// The back-office dashboard's chart cards under the host's content security policy, against the running stack. The case
// signs up an account through Blazor so the local stack has a signup and a login today, signs in to the back office as
// the local admin, and waits for the five chart cards (MRR trend, plan distribution, revenue, account growth and user
// logins) to load and draw. It then switches the period to 7 days, waits for the trend cards to redraw for it, opens every
// data table, and records:
//
// 1. every securitypolicyviolation event, with its directive and blocked source
// 2. every element in the document body that carries a style attribute, in light and shadow trees, grouped by element
//    and by the properties it sets, with a sample
// 3. every style or script element added to the document after load, and whether it carries a nonce
//
// The edition allows no style attribute, from markup or from script, and no violation (.claude/rules/blazor/
// component-library.md). The case passes only when all five charts drew, the period toggle changed the trend cards, and
// all three lists are empty.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started and the
// local subscription setting on.
// Run: dotnet run --project developer-cli -- blazor-harness charts --browser all

import { basePort, launchBrowser, newContext, observeErrors, parseArguments, pathBase, policyViolationsOf, probeHostConfiguration, signUpThroughBlazor, writeResult } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
// The account API's back-office listener, which forwards the path base to the Blazor host (PortAllocation: base port + 1)
const backOfficeUrl = `https://back-office.dev.localhost:${basePort + 1}`;
const dashboardUrl = `${backOfficeUrl}${pathBase}/back-office`;
const trendCards = ["mrr-trend", "revenue-trend", "account-growth", "user-logins"];
const chartCards = [...trendCards, "plan-distribution"];
const loadTimeoutMs = 60_000;

const browser = await launchBrowser(options.browser);
const hostConfiguration = await probeHostConfiguration(browser, options.browser);
const results = [];

async function readStyleAttributes(page) {
  return page.evaluate(() => {
    const found = [];
    const visit = (root) => {
      for (const element of root.querySelectorAll("*")) {
        if (element.hasAttribute("style")) {
          found.push({ element: element.tagName.toLowerCase(), className: String(element.getAttribute("class") ?? "").slice(0, 60), style: element.getAttribute("style").slice(0, 160) });
        }
        if (element.shadowRoot) visit(element.shadowRoot);
      }
    };
    visit(document.body);
    return found;
  });
}

function summarise(styleAttributes) {
  const groups = new Map();
  for (const entry of styleAttributes) {
    const properties = entry.style.split(";").map((declaration) => declaration.split(":")[0].trim()).filter(Boolean).sort().join(",");
    const key = `${entry.element}.${entry.className.split(" ")[0]} [${properties}]`;
    const group = groups.get(key) ?? { key, count: 0, sample: entry.style };
    group.count++;
    groups.set(key, group);
  }
  return [...groups.values()].sort((first, second) => second.count - first.count);
}

async function waitForCards(page) {
  for (const card of chartCards) {
    await page.locator(`[data-testid="${card}"][data-state="loaded"]`).waitFor({ timeout: loadTimeoutMs });
  }
}

// What each card drew: a trend card its current series (bars, or a line with path data) and one table row a day, the plan
// card its track and one table row a plan
async function readCharts(page) {
  return page.evaluate((cards) => cards.map((card) => {
    const chart = document.querySelector(`[data-testid="${card}-chart"]`);
    const kind = chart?.getAttribute("data-chart") ?? null;
    const drawn = kind === "bar"
      ? chart.querySelectorAll("rect.dashboard-chart-bar-current").length
      : kind === "area"
        ? [...chart.querySelectorAll("path.dashboard-chart-line-current")].filter((path) => (path.getAttribute("d") ?? "").length > 0).length
        : chart?.querySelectorAll("path.dashboard-chart-slice").length ?? 0;
    return { card, kind, drawn, rows: chart?.querySelectorAll(`[data-testid="${card}-chart-row"]`).length ?? 0, subtitle: document.querySelector(`[data-testid="${card}-subtitle"]`)?.textContent ?? null };
  }), chartCards);
}

try {
  await signUpThroughBlazor(browser, options.browser, `charts-${options.browser}-${Date.now()}@example.com`);

  const context = await newContext(browser, options.browser);
  const signIn = await context.newPage();
  await signIn.goto(`${backOfficeUrl}/.auth/login/aad/callback?identity=admin&post_login_redirect_uri=${encodeURIComponent(`${pathBase}/back-office`)}`, { waitUntil: "load" });
  await signIn.close();

  const page = await context.newPage();
  const observations = observeErrors(page);
  await page.goto(dashboardUrl, { waitUntil: "load" });
  await page.evaluate(() => document.querySelectorAll("style, script, link[rel=stylesheet]").forEach((element) => (element.__initial = true)));
  await waitForCards(page);
  const charts30Days = await readCharts(page);

  await page.getByTestId("dashboard-period-7").click();
  await page.getByTestId("dashboard-period-7").and(page.locator('[aria-pressed="true"]')).waitFor();
  await waitForCards(page);
  await page.waitForFunction((cards) => cards.every((card) => document.querySelectorAll(`[data-testid="${card}-chart-row"]`).length <= 8), trendCards, { timeout: loadTimeoutMs });
  const charts7Days = await readCharts(page);

  for (const details of await page.locator("details.dashboard-chart-data").all()) await details.locator("summary").click();
  const openTables = await page.locator("details.dashboard-chart-data[open]").count();

  const styleAttributes = await readStyleAttributes(page);
  const addedElements = await page.evaluate(() =>
    [...document.querySelectorAll("style, script, link[rel=stylesheet]")].filter((element) => !element.__initial).map((element) => ({
      element: element.tagName.toLowerCase(), source: element.getAttribute("src") ?? element.getAttribute("href") ?? `inline, ${element.textContent.length} characters`, nonce: element.nonce !== ""
    })));
  const violations = policyViolationsOf(context);

  const failures = [];
  for (const chart of charts30Days) if (chart.drawn === 0 || chart.rows === 0) failures.push(`${chart.card}: nothing drawn`);
  for (const card of trendCards) {
    const before = charts30Days.find((chart) => chart.card === card);
    const after = charts7Days.find((chart) => chart.card === card);
    if (after.rows === 0 || after.rows >= before.rows) failures.push(`${card}: the 7-day period did not redraw the card (${before.rows} rows, then ${after.rows})`);
  }
  if (openTables !== trendCards.length) failures.push(`${openTables} of ${trendCards.length} data tables opened`);
  if (observations.pageErrors.length > 0) failures.push(`${observations.pageErrors.length} page errors`);

  const passed = failures.length === 0 && violations.length === 0 && styleAttributes.length === 0 && addedElements.length === 0;
  results.push({
    name: "dashboard charts under the content security policy",
    passed,
    failures,
    violationCount: violations.length,
    violations: [...new Map(violations.map((violation) => [`${violation.effectiveDirective} ${violation.blockedURI}`, violation])).values()],
    styleAttributeCount: styleAttributes.length,
    styleAttributes: summarise(styleAttributes),
    addedElements,
    charts30Days,
    charts7Days,
    openTables,
    consoleErrors: observations.consoleErrors,
    pageErrors: observations.pageErrors
  });
  console.log(`${passed ? "PASS" : "FAIL"} charts: ${violations.length} violations, ${styleAttributes.length} style attributes, ${addedElements.length} added style or script elements${failures.length > 0 ? `; ${failures.join("; ")}` : ""}`);
  await context.close();
} catch (error) {
  results.push({ name: "dashboard charts under the content security policy", passed: false, detail: error.message });
  console.log(`FAIL charts: ${error.message}`);
}

await browser.close();
const { resultFile, passed } = writeResult(`charts-${options.browser}.json`, { browser: options.browser, hostConfiguration, results }, 1);
console.log(`Result: ${resultFile}`);
process.exit(passed ? 0 : 1);
