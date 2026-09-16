// The authenticated shell in one browser, against the running stack.
//
// 1. Desktop: the shell renders its landmarks with no style attribute and no policy violation; Ctrl+B collapses the
//    sidebar and the collapsed state survives a reload; a malformed stored value falls back to expanded.
// 2. Repeated enhanced navigation leaves one hotkey listener: after several page changes one Ctrl+B toggles once.
// 3. Mobile at 390 px: the sidebar is replaced by the Open navigation menu button; the modal dialog opens with the keyboard,
//    keeps focus inside, closes with Escape and with Close menu, and focus returns to the button.
// 4. The install prompt stays hidden outside the iOS heuristics.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started.
// Run: dotnet run --project developer-cli -- blazor-harness shell-layout --browser all

import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { baseUrl, launchBrowser, newContext, parseArguments, pathBase, policyViolationsOf, resultsFolder, signUpThroughBlazor } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
const interactiveTimeoutMs = 60_000;
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

const signedUp = await signUpThroughBlazor(browser, options.browser, `shell-layout-${stamp}@example.com`);

async function withPage(viewport, action) {
  const context = await newContext(browser, options.browser, signedUp.storageState);
  try {
    const page = await context.newPage();
    await page.setViewportSize(viewport);
    const detail = await action(page);
    const violations = policyViolationsOf(context);
    assert(violations.length === 0, `Policy violations: ${JSON.stringify(violations)}`);
    return detail;
  } finally {
    await context.close();
  }
}

async function openInteractive(page, route, marker) {
  await page.goto(`${baseUrl}${pathBase}/${route}`, { waitUntil: "load" });
  await page.locator(marker).waitFor({ state: "visible", timeout: interactiveTimeoutMs });
  await page.locator('[data-testid="app-shell"][data-tenants-state="loaded"]').waitFor({ timeout: interactiveTimeoutMs });
}

const sidebarState = (page) => page.locator('[data-testid="app-shell"]').getAttribute("data-sidebar");
// The document element is left out: the framework's loader sets its load percentage custom properties there through the
// CSSOM, which the policy does not govern and which predates the shell
async function styleAttributeCount(page) {
  const styled = await page.evaluate(() => [...document.querySelectorAll("body [style], body[style]")].map((element) => `${element.tagName.toLowerCase()}#${element.id}.${element.className}[style=${element.getAttribute("style")}]`));
  if (styled.length > 0) console.log(`Styled elements: ${styled.join(" | ")}`);
  return styled.length;
}

await check("desktop shell has its landmarks, no style attribute, and the collapsed state survives a reload", () =>
  withPage({ width: 1280, height: 800 }, async (page) => {
    await openInteractive(page, "app", "#user-menu-trigger");
    assert((await page.locator("main#main-content").count()) === 1, "No main content landmark.");
    assert((await page.locator(".app-sidebar-navigation[aria-label] a[aria-current='page']").count()) === 1, "No current navigation item.");
    assert((await sidebarState(page)) === "expanded", "The sidebar did not start expanded.");
    assert((await styleAttributeCount(page)) === 0, "A style attribute was written.");

    await page.keyboard.press("Control+b");
    await page.waitForFunction(() => document.querySelector('[data-testid="app-shell"]')?.dataset.sidebar === "collapsed");
    await page.reload({ waitUntil: "load" });
    await page.locator("#user-menu-trigger").waitFor({ timeout: interactiveTimeoutMs });
    await page.waitForFunction(() => document.querySelector('[data-testid="app-shell"]')?.dataset.sidebar === "collapsed", undefined, { timeout: interactiveTimeoutMs });
    assert((await styleAttributeCount(page)) === 0, "A style attribute was written after collapsing.");

    await page.evaluate(() => localStorage.setItem("side-menu-collapsed", "{not json"));
    await page.reload({ waitUntil: "load" });
    await page.locator("#user-menu-trigger").waitFor({ timeout: interactiveTimeoutMs });
    await page.waitForTimeout(500);
    assert((await sidebarState(page)) === "expanded", "A malformed stored value did not fall back to expanded.");
    return { stored: await page.evaluate(() => localStorage.getItem("side-menu-collapsed")) };
  })
);

await check("repeated enhanced navigation leaves one hotkey listener", () =>
  withPage({ width: 1280, height: 800 }, async (page) => {
    await openInteractive(page, "app", "#user-menu-trigger");
    for (let round = 0; round < 3; round++) {
      await page.locator('.app-sidebar-navigation a[href$="/account/users"]').click();
      await page.waitForURL(/\/account\/users$/);
      await page.locator("#user-menu-trigger").waitFor({ timeout: interactiveTimeoutMs });
      await page.locator('.app-sidebar-navigation a[href$="/app"]').click();
      await page.waitForURL(/\/app$/);
      await page.locator("#user-menu-trigger").waitFor({ timeout: interactiveTimeoutMs });
    }
    await page.waitForTimeout(500);
    const before = await sidebarState(page);
    await page.keyboard.press("Control+b");
    await page.waitForTimeout(500);
    const after = await sidebarState(page);
    assert(before !== after, `One Ctrl+B did not toggle once (before ${before}, after ${after}).`);
    assert((await page.locator('[data-testid="app-shell"]').count()) === 1 && (await page.locator("#user-menu-trigger").count()) === 1, "The shell or its menu is duplicated.");
    return { before, after };
  })
);

await check("mobile menu at 390 px opens and closes with the keyboard and returns focus to its button", () =>
  withPage({ width: 390, height: 844 }, async (page) => {
    await openInteractive(page, "app", "#mobile-menu-button");
    assert(!(await page.locator(".app-sidebar").isVisible()), "The sidebar is visible at 390 px.");
    const dialog = page.locator("dialog[aria-modal='true'][aria-label]");
    const focusedId = () => page.evaluate(() => document.activeElement?.id ?? "");
    const focusInDialog = () => page.evaluate(() => document.activeElement?.closest("dialog")?.open === true);

    await page.locator("#mobile-menu-button").focus();
    await page.keyboard.press("Enter");
    await dialog.locator(".mobile-menu").waitFor();
    for (let tab = 0; tab < 20; tab++) {
      await page.keyboard.press("Tab");
      assert(await focusInDialog(), `Focus left the dialog after ${tab + 1} Tab presses.`);
    }
    await page.keyboard.press("Escape");
    await page.waitForFunction(() => document.querySelector("dialog .mobile-menu") === null);
    await page.waitForFunction(() => document.activeElement?.id === "mobile-menu-button");

    await page.keyboard.press("Enter");
    await dialog.locator(".mobile-menu-close").focus();
    await page.keyboard.press("Enter");
    await page.waitForFunction(() => document.querySelector("dialog .mobile-menu") === null);
    await page.waitForFunction(() => document.activeElement?.id === "mobile-menu-button");
    assert((await styleAttributeCount(page)) === 0, "A style attribute was written.");
    return { focus: await focusedId(), label: await dialog.getAttribute("aria-label") };
  })
);

await check("install prompt stays hidden outside the iOS heuristics", () =>
  withPage({ width: 1280, height: 800 }, async (page) => {
    await openInteractive(page, "app", "#user-menu-trigger");
    await page.waitForTimeout(500);
    assert((await page.locator(".install-prompt").count()) === 0, "The install prompt rendered outside iOS.");
  })
);

await browser.close();

mkdirSync(resultsFolder, { recursive: true });
writeFileSync(path.join(resultsFolder, `shell-layout-${options.browser}.json`), JSON.stringify({ browser: options.browser, results }, null, 2));
if (results.some((result) => !result.passed)) process.exit(1);
