// Culture selection and culture stability of the Blazor edition, in one browser.
//
// 1. Anonymous public pages follow the browser language: da-DK and en-US exactly, "da" by base language, and an
//    unsupported language (fr-FR) falls back to en-US. <html lang>, the heading and the validation message of an empty
//    login submit are checked; the request's Accept-Language header is recorded to prove what the browser asked for.
// 2. Authenticated pages follow the user's locale claim, set at signup, even when the browser asks for the other language,
//    in both directions. The prerendered document already carries the claim culture; after the WebAssembly start the
//    heading, the number and date sample and the interactive-only texts use the same culture, and a mutation observer
//    installed before any script runs proves the translated texts never showed another value. API calls from the
//    runtime send X-Locale with that culture.
// 3. The users page on the shared DataList renders its paginator texts in the claim culture.
// Every page asserts zero content security policy violations and no page errors.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill (Development), or a trimmed publish served in
// Production through blazor-publish and blazor-serve (the pages used here exist in both).
// Run: dotnet run --project developer-cli -- blazor-harness localization --browser all

import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { baseUrl, isProductionPolicy, launchBrowser, newContext, observeErrors, parseArguments, pathBase, resultsFolder, signUpThroughBlazor } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
const interactiveTimeoutMs = 60_000;
const testId = (id) => `[data-testid="${id}"]`;

// Expected texts, as in the en-US and da-DK resources of shared-kernel/SharedKernel.Localization; the format sample is
// 1234567.891 as "N2" and 2026-09-15 as "d" in each culture
const cultures = {
  "en-US": {
    loginHeading: "Hi! Welcome back",
    emailRequired: "Email address required",
    workspaceHeading: "Your workspace",
    interactive: "Interactive: True",
    formatSample: "Number and date format: 1,234,567.89 9/15/2026",
    logOut: "Log out",
    pagePattern: /^Page 1 of \d+$/,
    nextPage: "Next page"
  },
  "da-DK": {
    loginHeading: "Hej! Velkommen tilbage",
    emailRequired: "E-mailadresse påkrævet",
    workspaceHeading: "Dit arbejdsområde",
    interactive: "Interaktiv: True",
    formatSample: "Tal- og datoformat: 1.234.567,89 15.09.2026",
    logOut: "Log ud",
    pagePattern: /^Side 1 af \d+$/,
    nextPage: "Næste side"
  }
};

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

function assertEqual(actual, expected, label) {
  assert(JSON.stringify(actual) === JSON.stringify(expected), `${label}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}.`);
}

// The host encodes non-ASCII characters in markup as numeric character references
function decodeCharacterReferences(html) {
  return html.replace(/&#x([0-9a-f]+);/gi, (_, hex) => String.fromCodePoint(parseInt(hex, 16)));
}

async function assertCleanPage(page, observations, expectedLocale) {
  const state = await page.evaluate(() => ({ violations: window.__policyViolations ?? [], lang: document.documentElement.lang }));
  assertEqual(state.violations, [], "Policy violations");
  assertEqual(state.lang, expectedLocale, "<html lang>");
  assertEqual(observations.pageErrors, [], "Page errors");
}

// Records every text value the translated elements take from the first parse on, before any framework script runs
function recordTextHistory() {
  const selectors = ["h1", '[data-testid="culture-format"]', '[data-testid="nav-app"]', '[data-testid="nav-details"]'];
  window.__textHistory = {};
  const record = () => {
    for (const selector of selectors) {
      const element = document.querySelector(selector);
      if (element === null) continue;
      const values = (window.__textHistory[selector] ??= []);
      const text = element.textContent.trim();
      if (values.at(-1) !== text) values.push(text);
    }
  };
  new MutationObserver(record).observe(document, { subtree: true, childList: true, characterData: true });
  document.addEventListener("DOMContentLoaded", record);
}

async function withPage(locale, storageState, action) {
  const context = await newContext(browser, options.browser, storageState, locale);
  await context.addInitScript(recordTextHistory);
  const page = await context.newPage();
  const observations = observeErrors(page);
  try {
    return await action(page, observations);
  } finally {
    await context.close();
  }
}

for (const [browserLocale, expectedLocale] of [
  ["da-DK", "da-DK"],
  ["en-US", "en-US"],
  ["da", "da-DK"],
  ["fr-FR", "en-US"]
]) {
  await check(`anonymous login page with browser language ${browserLocale} renders ${expectedLocale}`, () =>
    withPage(browserLocale, undefined, async (page, observations) => {
      const expected = cultures[expectedLocale];
      const response = await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
      const acceptLanguage = await response.request().headerValue("accept-language");
      assert(acceptLanguage?.toLowerCase().startsWith(browserLocale.toLowerCase()), `The browser sent Accept-Language ${acceptLanguage}.`);
      assertEqual((await page.locator("h1").textContent()).trim(), expected.loginHeading, "Heading");
      await page.locator(testId("submit")).click();
      const message = page.locator('[data-valmsg-for="Input.Email"]');
      await page.waitForFunction(() => document.querySelector('[data-valmsg-for="Input.Email"]')?.textContent.trim().length > 0);
      assertEqual((await message.textContent()).trim(), expected.emailRequired, "Validation message");
      await assertCleanPage(page, observations, expectedLocale);
      return { acceptLanguage, production: isProductionPolicy(response.headers()["content-security-policy"]) };
    })
  );
}

const users = {};
for (const claimLocale of ["da-DK", "en-US"]) {
  users[claimLocale] = await signUpThroughBlazor(browser, options.browser, `localization-${claimLocale.toLowerCase()}-${stamp}@example.com`, claimLocale);
}

for (const [claimLocale, browserLocale] of [
  ["da-DK", "en-US"],
  ["en-US", "da-DK"]
]) {
  const expected = cultures[claimLocale];

  await check(`authenticated workspace with claim ${claimLocale} and browser language ${browserLocale} prerenders and starts in ${claimLocale}`, () =>
    withPage(browserLocale, users[claimLocale].storageState, async (page, observations) => {
      const localeHeaders = [];
      page.on("request", (request) => {
        if (new URL(request.url()).pathname.startsWith("/api/account/")) localeHeaders.push(request.headers()["x-locale"] ?? null);
      });

      const response = await page.goto(`${baseUrl}${pathBase}/app`, { waitUntil: "commit" });
      const prerendered = decodeCharacterReferences(await response.text());
      assert(prerendered.includes(`<html lang="${claimLocale}"`), "The prerendered document does not carry the claim culture.");
      assert(prerendered.includes(`<h1>${expected.workspaceHeading}</h1>`), "The prerendered heading is not in the claim culture.");
      assert(prerendered.includes(expected.formatSample), "The prerendered format sample is not in the claim culture.");

      await page.locator(testId("render-mode"), { hasText: expected.interactive }).waitFor({ timeout: interactiveTimeoutMs });
      await page.locator(testId("logout")).waitFor();
      assertEqual((await page.locator("h1").textContent()).trim(), expected.workspaceHeading, "Interactive heading");
      assertEqual((await page.locator(testId("culture-format")).textContent()).trim(), expected.formatSample, "Interactive format sample");
      assertEqual((await page.locator(testId("logout")).textContent()).trim(), expected.logOut, "Interactive-only text");

      const history = await page.evaluate(() => window.__textHistory);
      assertEqual(history.h1, [expected.workspaceHeading], "Heading values over time");
      assertEqual(history['[data-testid="culture-format"]'], [expected.formatSample], "Format sample values over time");
      assert(localeHeaders.length > 0, "The runtime made no account API call.");
      assert(localeHeaders.every((header) => header === claimLocale), `X-Locale headers: ${JSON.stringify(localeHeaders)}.`);
      await assertCleanPage(page, observations, claimLocale);
      return { apiCalls: localeHeaders.length, history };
    })
  );

  await check(`users page paginator with claim ${claimLocale} and browser language ${browserLocale} renders in ${claimLocale}`, () =>
    withPage(browserLocale, users[claimLocale].storageState, async (page, observations) => {
      await page.goto(`${baseUrl}${pathBase}/app/users/quick`, { waitUntil: "load" });
      const pageText = page.locator("[data-list-page]").first();
      await pageText.waitFor({ timeout: interactiveTimeoutMs });
      // The signed-up user is the tenant's one user, so a loaded list counts at least one
      await page.locator("[data-list-total-count]", { hasText: /^\s*[1-9]/ }).first().waitFor({ timeout: interactiveTimeoutMs });
      const text = (await pageText.textContent()).trim();
      assert(expected.pagePattern.test(text), `Paginator text: ${text}.`);
      assertEqual((await page.locator("[data-list-next]").first().textContent()).trim(), expected.nextPage, "Next page button");
      await assertCleanPage(page, observations, claimLocale);
      return { paginator: text };
    })
  );
}

await browser.close();

mkdirSync(resultsFolder, { recursive: true });
writeFileSync(path.join(resultsFolder, `localization-${options.browser}.json`), JSON.stringify({ browser: options.browser, results }, null, 2));
if (results.some((result) => !result.passed)) process.exit(1);
