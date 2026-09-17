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
// 4. The public language menu writes the preferred-locale cookie (Path=/, Secure, SameSite=Lax, one year, not HttpOnly)
//    and loads the page again in the chosen language, which then wins over the browser language across a reload and an
//    enhanced navigation. A malformed cookie, unknown stored theme and zoom values and a storage that throws fall back to
//    the defaults without breaking the page.
// 5. The preferences page applies theme and zoom at once (data attributes on <html>, no style attribute, the telemetry
//    PUTs), moves through the zoom levels with the Arrow keys, saves the language on the user and loads the page again in
//    it; after a reload, a logout (the login page follows the cookie) and a new login with a conflicting cookie, the
//    language comes from the server and theme and zoom from the device.
// Every page asserts zero content security policy violations and no page errors.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill (Development), or a trimmed publish served in
// Production through blazor-publish and blazor-serve (the pages used here exist in both).
// Run: dotnet run --project developer-cli -- blazor-harness localization --browser all

import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { baseUrl, isProductionPolicy, launchBrowser, newContext, observeErrors, parseArguments, pathBase, readOneTimePassword, resultsFolder, signUpThroughBlazor, submitOneTimePasswordThroughBlazor } from "./support/stack.mjs";

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
    preferencesHeading: "User preferences",
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
    preferencesHeading: "Brugerpræferencer",
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
  const selectors = ["h1", '[data-testid="culture-format"]', '.app-sidebar-navigation a[href$="/app"]', '[data-testid="nav-details"]'];
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
      await page.locator("#user-menu-trigger").click({ timeout: interactiveTimeoutMs });
      const logOut = page.locator('[role="menu"] [role="menuitem"]').last();
      await logOut.waitFor();
      assertEqual((await page.locator("h1").textContent()).trim(), expected.workspaceHeading, "Interactive heading");
      assertEqual((await page.locator(testId("culture-format")).textContent()).trim(), expected.formatSample, "Interactive format sample");
      assertEqual((await logOut.textContent()).trim(), expected.logOut, "Interactive-only text");

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
      await page.goto(`${baseUrl}${pathBase}/account/users`, { waitUntil: "load" });
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

async function preferredLocaleCookie(context) {
  return (await context.cookies(baseUrl)).find((cookie) => cookie.name === "preferred-locale");
}

function assertPreferredLocaleCookie(cookie, locale) {
  assert(cookie !== undefined, "The preferred-locale cookie was not written.");
  assertEqual(
    { value: cookie.value, path: cookie.path, secure: cookie.secure, sameSite: cookie.sameSite, httpOnly: cookie.httpOnly },
    { value: locale, path: "/", secure: true, sameSite: "Lax", httpOnly: false },
    "preferred-locale cookie"
  );
  const days = (cookie.expires * 1000 - Date.now()) / 86_400_000;
  assert(days > 364 && days <= 365.01, `The preferred-locale cookie expires in ${days} days.`);
}

// No element in <body> has a style attribute and the zoom level is never a style property. On <html> the framework's
// WebAssembly loader sets its own --blazor-load-percentage properties, as the shell-policy theme case also allows.
async function assertNoStyleAttribute(page) {
  const styled = await page.evaluate(() => ({
    body: [...document.body.querySelectorAll("[style]")].map((element) => element.outerHTML.slice(0, 120)),
    rootZoom: document.documentElement.style.getPropertyValue("--zoom-level"),
    rootFontSize: document.documentElement.style.fontSize
  }));
  assertEqual(styled, { body: [], rootZoom: "", rootFontSize: "" }, "Style attributes");
}

await check("public language menu writes the preferred-locale cookie and renders the chosen language over the browser language", () =>
  withPage("en-US", undefined, async (page, observations) => {
    await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
    assertEqual((await page.locator("h1").textContent()).trim(), cultures["en-US"].loginHeading, "Heading before");
    await page.locator(testId("language-menu-trigger")).click();
    const danish = page.locator('[data-language-choice="da-DK"]');
    await danish.waitFor();
    assertEqual(await page.locator('[data-language-choice="en-US"]').getAttribute("aria-checked"), "true", "Checked language before");
    await Promise.all([page.waitForEvent("load"), danish.click()]);
    await page.locator("h1", { hasText: cultures["da-DK"].loginHeading }).waitFor();
    assertPreferredLocaleCookie(await preferredLocaleCookie(page.context()), "da-DK");
    await assertCleanPage(page, observations, "da-DK");

    await page.reload({ waitUntil: "load" });
    await assertCleanPage(page, observations, "da-DK");
    await page.locator(testId("nav-signup")).click();
    await page.waitForURL(/\/blazor\/signup$/);
    await assertCleanPage(page, observations, "da-DK");

    await page.locator(testId("language-menu-trigger")).click();
    assertEqual(await danish.getAttribute("aria-checked"), "true", "Checked language after");
    await Promise.all([page.waitForEvent("load"), page.locator('[data-language-choice="en-US"]').click()]);
    await page.waitForFunction(() => document.documentElement.lang === "en-US");
    assertPreferredLocaleCookie(await preferredLocaleCookie(page.context()), "en-US");
    await assertCleanPage(page, observations, "en-US");
  })
);

await check("malformed preferred-locale cookie, unknown stored preferences and a throwing storage fall back to the defaults", () =>
  withPage("da-DK", undefined, async (page, observations) => {
    await page.context().addCookies([{ name: "preferred-locale", value: "xx-XX", url: baseUrl }]);
    await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
    await page.evaluate(() => {
      localStorage.setItem("theme", "sepia");
      localStorage.setItem("zoom-level", "3");
    });
    await page.reload({ waitUntil: "load" });
    const stored = await page.evaluate(() => ({ theme: document.documentElement.dataset.themeMode, zoom: document.documentElement.getAttribute("data-zoom-level") }));
    assertEqual(stored, { theme: "system", zoom: null }, "Unknown stored values");
    await assertCleanPage(page, observations, "da-DK");

    await page.context().addInitScript(() => {
      Object.defineProperty(window, "localStorage", { configurable: true, get: () => { throw new DOMException("Blocked", "SecurityError"); } });
    });
    await page.reload({ waitUntil: "load" });
    const blocked = await page.evaluate(() => ({ theme: document.documentElement.dataset.themeMode, zoom: document.documentElement.getAttribute("data-zoom-level") }));
    assertEqual(blocked, { theme: "system", zoom: null }, "Blocked storage");
    await page.locator(testId("nav-signup")).click();
    await page.waitForURL(/\/blazor\/signup$/);
    assertEqual((await page.locator("h1").count()) > 0, true, "Signup page rendered");
    await assertCleanPage(page, observations, "da-DK");
  })
);

await check("preferences page applies theme, zoom and language and they survive reload, logout and a new login", async () => {
  const email = `preferences-${stamp}@example.com`;
  const user = await signUpThroughBlazor(browser, options.browser, email, "en-US");
  return withPage("en-US", user.storageState, async (page, observations) => {
    const context = page.context();
    const root = () => page.evaluate(() => ({ theme: document.documentElement.dataset.theme, zoom: document.documentElement.getAttribute("data-zoom-level"), fontSize: getComputedStyle(document.documentElement).fontSize }));
    const put = (route) => page.waitForResponse((response) => response.request().method() === "PUT" && new URL(response.url()).pathname === `/api/account/users/me/${route}`);
    const choice = (id) => page.locator(`${testId(id)} input`);

    await page.goto(`${baseUrl}${pathBase}/user/preferences`, { waitUntil: "load" });
    await page.locator(`${testId("theme-system")} input:not([disabled])`).waitFor({ timeout: interactiveTimeoutMs });
    await page.locator(`${testId("language-en-US")} input:checked:not([disabled])`).waitFor({ timeout: interactiveTimeoutMs });
    assertEqual((await page.locator("h1").textContent()).trim(), cultures["en-US"].preferencesHeading, "Heading");
    const baseFontSize = parseFloat((await root()).fontSize);

    const themePut = put("change-theme");
    await page.locator(testId("theme-dark")).click();
    assert((await themePut).ok(), "change-theme failed.");
    await page.locator(testId("theme-updated-toast")).waitFor();
    assertEqual((await root()).theme, "dark", "Theme after click");

    const zoomPut = put("change-zoom-level");
    await page.locator(testId("zoom-level-1.25")).click();
    assert((await zoomPut).ok(), "change-zoom-level failed.");
    await page.locator(testId("zoom-level-updated-toast")).waitFor();
    let state = await root();
    assertEqual(state.zoom, "1.25", "Zoom after click");
    assertEqual(parseFloat(state.fontSize), baseFontSize * 1.25, "Root font size at 1.25");
    assertEqual(await page.evaluate(() => localStorage.getItem("zoom-level")), "1.25", "Stored zoom");

    await choice("zoom-level-1.25").focus();
    await page.keyboard.press("ArrowLeft");
    await page.waitForFunction(() => document.documentElement.getAttribute("data-zoom-level") === "1.125");
    await page.keyboard.press("ArrowRight");
    await page.waitForFunction(() => document.documentElement.getAttribute("data-zoom-level") === "1.25");
    assertEqual(await choice("zoom-level-1.25").isChecked(), true, "Zoom radio after the Arrow keys");
    await assertNoStyleAttribute(page);
    await assertCleanPage(page, observations, "en-US");

    const localePut = put("change-locale");
    const loaded = page.waitForEvent("load");
    await page.locator(testId("language-da-DK")).click();
    assert((await localePut).ok(), "change-locale failed.");
    await loaded;
    await page.locator("h1", { hasText: cultures["da-DK"].preferencesHeading }).waitFor({ timeout: interactiveTimeoutMs });
    // The heading is prerendered; an enabled group means the runtime runs, with the da-DK resources it loads on demand, so
    // the reload below cannot cancel a download (WebKit reports that as a page error)
    await page.locator(`${testId("language-da-DK")} input:checked:not([disabled])`).waitFor({ timeout: interactiveTimeoutMs });
    assertPreferredLocaleCookie(await preferredLocaleCookie(context), "da-DK");
    await assertCleanPage(page, observations, "da-DK");

    await page.reload({ waitUntil: "load" });
    await page.locator(`${testId("language-da-DK")} input:checked:not([disabled])`).waitFor({ timeout: interactiveTimeoutMs });
    await page.locator(`${testId("theme-dark")} input:checked`).waitFor({ timeout: interactiveTimeoutMs });
    await page.locator(`${testId("zoom-level-1.25")} input:checked`).waitFor({ timeout: interactiveTimeoutMs });
    state = await root();
    assertEqual({ theme: state.theme, zoom: state.zoom }, { theme: "dark", zoom: "1.25" }, "Device preferences after reload");
    await assertNoStyleAttribute(page);
    await assertCleanPage(page, observations, "da-DK");

    await page.locator("#user-menu-trigger").click();
    await Promise.all([page.waitForURL(/\/blazor\/login/, { timeout: interactiveTimeoutMs }), page.locator('[role="menu"] [role="menuitem"]').last().click()]);
    await page.waitForLoadState("load");
    await page.locator("h1", { hasText: cultures["da-DK"].loginHeading }).waitFor();
    await assertCleanPage(page, observations, "da-DK");

    // A conflicting cookie before the new login: the saved language must come from the server's locale claim
    await context.addCookies([{ name: "preferred-locale", value: "en-US", url: baseUrl, secure: true, sameSite: "Lax" }]);
    await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
    await page.locator(testId("email")).fill(email);
    const sentAfter = Date.now();
    await page.locator(testId("submit")).click();
    await page.waitForURL(/\/blazor\/login\/verify\?/);
    await submitOneTimePasswordThroughBlazor(page, await readOneTimePassword(email, sentAfter));
    await page.waitForURL(`${baseUrl}${pathBase}/app`, { timeout: interactiveTimeoutMs });
    // Leaving a document while its runtime still downloads makes Firefox report the aborted downloads as page errors
    await page.locator(testId("render-mode"), { hasText: cultures["da-DK"].interactive }).waitFor({ timeout: interactiveTimeoutMs });
    await page.goto(`${baseUrl}${pathBase}/user/preferences`, { waitUntil: "load" });
    await page.locator(`${testId("language-da-DK")} input:checked:not([disabled])`).waitFor({ timeout: interactiveTimeoutMs });
    await page.locator(`${testId("theme-dark")} input:checked`).waitFor({ timeout: interactiveTimeoutMs });
    await page.locator(`${testId("zoom-level-1.25")} input:checked`).waitFor({ timeout: interactiveTimeoutMs });
    state = await root();
    assertEqual({ theme: state.theme, zoom: state.zoom }, { theme: "dark", zoom: "1.25" }, "Device preferences after a new login");
    await assertNoStyleAttribute(page);
    await assertCleanPage(page, observations, "da-DK");
    return { baseFontSize };
  });
});

await browser.close();

mkdirSync(resultsFolder, { recursive: true });
writeFileSync(path.join(resultsFolder, `localization-${options.browser}.json`), JSON.stringify({ browser: options.browser, results }, null, 2));
if (results.some((result) => !result.passed)) process.exit(1);
