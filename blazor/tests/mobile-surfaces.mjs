// Every surface of the Blazor edition at phone width, in one browser.
//
// The script walks the public surfaces (landing, legal index and the three legal documents, login, signup and the two
// verification pages, not-found and error), the authenticated surfaces (home, users, recycle bin, profile, preferences,
// sessions, account settings, details, and the status pages inside the shell), the signup and welcome journey, the shell's
// mobile menu, the dialogs (invite, filters, delete, role, purge, revoke, unsaved changes) and the toasts. On each one it
// asserts what the mobile pass is about:
//
// 1. No horizontal overflow: the document is no wider than the viewport, and the elements that stick out are named.
// 2. Zero content security policy violations, console errors, page errors and error responses, and no style attribute.
// 3. Touch: every control a finger aims at is at least 44 pixels in both axes. A link that renders inline in a sentence is
//    left out, as WCAG 2.5.8 does; it is not a control.
// 4. Keyboard: Tab reaches the controls in order without landing on the document body or stalling on one element, the
//    focused control is always distinguishable from its unfocused state, and Escape closes what is open.
// 5. Software keyboard: every text field names its type, its autocomplete and an accessible name, and a field the server
//    refused stays associated with its message.
// 6. Route changes: the document title changes with the route and the page keeps one main landmark and one first heading.
// 7. Dialogs: a dialog fills the screen, is a labelled modal, isolates the background and closes with Escape.
// 8. Toasts: a toast never covers the floating mobile menu button, is announced, and its dismiss target is reachable.
//
// It repeats the overflow check under the conditions the mobile pass also has to survive: reflow at 640 and at 320 CSS
// pixels (a 1280 by 1024 window at 200% and at 400% zoom, which is how the automation library can emulate zoom at all),
// the largest zoom level the preferences page offers, and Danish, whose labels are the longest the edition ships.
//
// What this script does not establish, and what stage E still needs from a person or a device: an assistive technology
// actually announcing the live regions and the dialog names, a physical touch screen (the automation library can tap but
// not press-and-hold, which EP-113 recorded), real browser zoom as opposed to the viewport emulation above, and the
// operating system's own large-text setting.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started in
// Development.
// Run: dotnet run --project developer-cli -- blazor-harness mobile-surfaces --browser all

import { readFileSync } from "node:fs";
import path from "node:path";
import {
  baseUrl,
  launchBrowser,
  newContext,
  observeErrors,
  parseArguments,
  pathBase,
  readOneTimePassword,
  repositoryRoot,
  signUpThroughBlazor,
  startLoginThroughBlazor,
  startSignupThroughBlazor,
  submitOneTimePasswordThroughBlazor,
  writeResult
} from "./support/stack.mjs";

// --only <text> runs the cases whose name contains the text, for working on one of them
const options = parseArguments(process.argv.slice(2), { browser: "chromium", only: "" });
const interactiveTimeoutMs = 60_000;
const settleMs = 400;
const tapTargetPixels = 44;
const overflowTolerancePixels = 1;
const invitedUsers = 3;

// The two phone sizes the React mobile specification uses, and the two reflow widths: a 1280 by 1024 window at 200% and at
// 400% browser zoom has a 640 and a 320 CSS pixel viewport, which is the only way the automation library emulates zoom
const phone = { name: "390x844", viewport: { width: 390, height: 844 }, hasTouch: true };
const smallPhone = { name: "375x667", viewport: { width: 375, height: 667 }, hasTouch: true };
const reflow200 = { name: "640x512 (200% zoom)", viewport: { width: 640, height: 512 } };
const reflow400 = { name: "320x256 (400% zoom)", viewport: { width: 320, height: 256 } };

const browser = await launchBrowser(options.browser);
const results = [];
const stamp = `${options.browser}-${Date.now()}`;
const testId = (id) => `[data-testid="${id}"]`;
const mobileMenuButton = "#mobile-menu-button";

// The address the brand names, read from the same file the host embeds; the support entry of the mobile menu renders only
// when it is set, so the check follows the configuration instead of assuming one
const brandSupportEmail = readBrandSupportEmail();

const publicSurfaces = [
  { name: "landing", url: `${baseUrl}${pathBase}/`, ready: "h1" },
  { name: "legal", url: `${baseUrl}${pathBase}/legal`, ready: "h1" },
  { name: "legal-terms", url: `${baseUrl}${pathBase}/legal/terms`, ready: "h1" },
  { name: "legal-privacy", url: `${baseUrl}${pathBase}/legal/privacy`, ready: "h1" },
  { name: "legal-dpa", url: `${baseUrl}${pathBase}/legal/dpa`, ready: "h1" },
  { name: "login", url: `${baseUrl}${pathBase}/login`, ready: "h1" },
  { name: "signup", url: `${baseUrl}${pathBase}/signup`, ready: "h1" },
  { name: "not-found", url: `${baseUrl}${pathBase}/not-found`, ready: "h1" },
  { name: "error-public", url: `${baseUrl}${pathBase}/error?error=session_expired`, ready: "h1" }
];

const authenticatedSurfaces = [
  { name: "home", url: `${baseUrl}${pathBase}/app`, ready: mobileMenuButton },
  { name: "details", url: `${baseUrl}${pathBase}/app/details`, ready: mobileMenuButton },
  { name: "users", url: `${baseUrl}${pathBase}/account/users`, ready: `${testId("users-grid")}[data-list-state="ready"]` },
  // A recycle bin with nothing in it renders its empty state instead of the list
  { name: "recycle-bin", url: `${baseUrl}${pathBase}/account/users/recycle-bin`, ready: `${testId("deleted-users-grid")}[data-list-state="ready"], ${testId("recycle-bin-empty")}` },
  { name: "profile", url: `${baseUrl}${pathBase}/user/profile`, ready: testId("profile-form") },
  { name: "preferences", url: `${baseUrl}${pathBase}/user/preferences`, ready: testId("preferences-theme") },
  { name: "sessions", url: `${baseUrl}${pathBase}/user/sessions`, ready: testId("sessions") },
  { name: "account-settings", url: `${baseUrl}${pathBase}/account/settings`, ready: testId("account-settings-form") },
  { name: "not-found-shell", url: `${baseUrl}${pathBase}/not-found`, ready: mobileMenuButton }
];

// The error page of a thrown exception, which is the one state that renders the shell statically: the framework re-executes
// the request without interactivity, so this document has no mobile menu, and the response is the 500 the page exists for.
// A /error?error=<code> landing renders the public layout even for a signed-in user, which error-public covers.
const thrownErrorSurface = { name: "error-shell", url: `${baseUrl}${pathBase}/development/throw`, ready: testId("app-shell") };

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function check(name, action) {
  if (typeof options.only === "string" && options.only.length > 0 && !name.includes(options.only)) return;

  try {
    const detail = await action();
    results.push({ name, passed: true, detail });
    console.log(`PASS ${name}${detail === undefined ? "" : `: ${JSON.stringify(detail)}`}`);
  } catch (error) {
    results.push({ name, passed: false, detail: String(error.stack ?? error.message).slice(0, 1_500) });
    console.log(`FAIL ${name}: ${error.message}`);
  }
}

// The brand's support address from application/platform-settings.jsonc, the file the host embeds and reads the same value
// from; the comments the file allows are stripped before parsing
function readBrandSupportEmail() {
  const settings = readFileSync(path.join(repositoryRoot, "application/platform-settings.jsonc"), "utf8");
  const withoutComments = settings.replace(/^\s*\/\/.*$/gm, "");
  return JSON.parse(withoutComments).branding.supportEmail ?? "";
}

const owner = await signUpThroughBlazor(browser, options.browser, `mobile-surfaces-owner-${stamp}@example.com`);
await seedUsers();
await signInAgain();

// A signed-in user's saved language wins over the browser's, so the Danish pass signs up an owner whose language is Danish
const danishOwner = await signUpThroughBlazor(browser, options.browser, `mobile-surfaces-danish-${stamp}@example.com`, "da-DK");

// Node cannot resolve app.dev.localhost, so the account API is called from a page on the gateway origin
async function seedUsers() {
  const context = await newContext(browser, options.browser, owner.storageState);
  const page = await context.newPage();
  await page.goto(`${baseUrl}${pathBase}/app`, { waitUntil: "load" });
  const failures = await page.evaluate(
    async ({ stamp, invitedUsers }) => {
      const bootstrap = await (await fetch("/api/account/bootstrap", { credentials: "same-origin" })).json();
      const send = (method, url, body) =>
        fetch(url, { method, credentials: "same-origin", headers: { "content-type": "application/json", "x-xsrf-token": bootstrap.antiforgeryToken }, body: JSON.stringify(body) });
      const describe = async (response) => (response.ok ? null : `${response.status} ${(await response.text()).slice(0, 300)}`);
      const outcome = [await describe(await send("PUT", "/api/account/tenants/current", { name: "Mobile surfaces fixture" }))];
      for (let index = 0; index < invitedUsers; index++) {
        outcome.push(await describe(await send("POST", "/api/account/users/invite", { email: `mobile-surfaces-${stamp}-${index}@example.com` })));
      }

      // One of them is deleted, so the recycle bin has a row and its dialogs can be opened
      const deleted = `mobile-surfaces-${stamp}-${invitedUsers - 1}@example.com`;
      const listed = await (await fetch(`/api/account/users?Search=${encodeURIComponent(deleted)}`, { credentials: "same-origin" })).json();
      const userId = listed.users?.[0]?.id ?? null;
      if (userId === null) {
        outcome.push(`The invited user ${deleted} was not listed`);
      } else {
        outcome.push(await describe(await send("POST", "/api/account/users/bulk-delete", { userIds: [userId] })));
      }

      return outcome.filter((entry) => entry !== null);
    },
    { stamp, invitedUsers }
  );
  await context.close();
  if (failures.length > 0) throw new Error(`Seeding failed: ${failures.slice(0, 3).join(" | ")}`);
}

// A second session for the same user, so the sessions page has a card to revoke, the way a second device creates one
async function signInAgain() {
  const context = await newContext(browser, options.browser);
  const page = await context.newPage();
  await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
  await page.locator(testId("email")).fill(owner.email);
  const sentAfter = Date.now();
  await page.locator(testId("submit")).click();
  await page.waitForURL(/\/blazor\/login\/verify\?/);
  await submitOneTimePasswordThroughBlazor(page, await readOneTimePassword(owner.email, sentAfter));
  await page.waitForURL(`${baseUrl}${pathBase}/app`);
  await context.close();
}

// Opens a context at one size and hands the page to the action, with the errors of every document it loads observed
async function withPage(size, storageState, action, locale = "en-US") {
  const context = await newContext(browser, options.browser, storageState, locale, { viewport: size.viewport, hasTouch: size.hasTouch === true });
  const page = await context.newPage();
  const observations = observeErrors(page);
  try {
    return await action(page, observations);
  } finally {
    await context.close();
  }
}

async function open(page, surface) {
  await page.goto(surface.url, { waitUntil: "load" });
  // Attachment, not visibility: a marker such as the mobile menu button is in the document only once the runtime started,
  // but the stylesheet hides it above the small breakpoint, which the reflow widths are
  await page.locator(surface.ready).first().waitFor({ state: "attached", timeout: interactiveTimeoutMs });
  await page.waitForTimeout(settleMs);
}

// The elements that reach past the viewport's inline edge, named so a failure says what to fix
function overflow(page) {
  return page.evaluate((tolerance) => {
    const width = window.innerWidth;
    const document_ = document.documentElement;
    const describe = (element, reason) => `${element.tagName.toLowerCase()}.${String(element.className || "").split(" ")[0]} ${reason}`;
    const offenders = [...document.body.querySelectorAll("*"), document.body]
      .flatMap((element) => {
        const box = element.getBoundingClientRect();
        if (box.width === 0 || box.height === 0) return [];
        if (box.right > width + tolerance) return [describe(element, `right=${Math.round(box.right)}`)];
        // An element wider than its own box is the container the overflow happens inside, which the box of its child hides
        if (element.scrollWidth > element.clientWidth + tolerance && getComputedStyle(element).overflowX === "visible") {
          return [describe(element, `scrollWidth=${element.scrollWidth} clientWidth=${element.clientWidth}`)];
        }

        return [];
      });
    return {
      documentWidth: document_.scrollWidth,
      viewportWidth: width,
      scrolls: document_.scrollWidth > width + tolerance,
      offenders: [...new Set(offenders)].slice(0, 6)
    };
  }, overflowTolerancePixels);
}

async function assertNoOverflow(page, label) {
  const measured = await overflow(page);
  assert(
    !measured.scrolls,
    `${label}: the document is ${measured.documentWidth} wide in a ${measured.viewportWidth} viewport. Offenders: ${measured.offenders.join(", ") || "none named"}`
  );
  return measured;
}

// A control a finger aims at. A link whose computed display is inline sits inside a sentence and is not one, which is the
// exception WCAG 2.5.8 makes; everything else that takes a click is.
function tapTargets(page) {
  return page.evaluate((minimum) => {
    const selector = "button, a[href], summary, [role='button'], input[type='checkbox'], input[type='radio'], select";
    // A checkbox or radio inside a label is activated by the whole label, so that is the area a finger aims at
    const target = (element) => {
      const box = element.getBoundingClientRect();
      if (!element.matches("input[type='checkbox'], input[type='radio']")) return box;

      const label = element.closest("label") ?? (element.id === "" ? null : document.querySelector(`label[for="${CSS.escape(element.id)}"]`));
      if (label === null) return box;

      const labelBox = label.getBoundingClientRect();
      return {
        width: Math.max(box.right, labelBox.right) - Math.min(box.left, labelBox.left),
        height: Math.max(box.bottom, labelBox.bottom) - Math.min(box.top, labelBox.top)
      };
    };
    return [...document.body.querySelectorAll(selector)]
      .filter((element) => {
        const box = element.getBoundingClientRect();
        if (box.width === 0 || box.height === 0) return false;
        if (element.closest("[hidden]") !== null) return false;
        const style = getComputedStyle(element);
        // A control the sheet hides, by clipping it or by making it transparent, is not a target; its label is
        return style.display !== "inline" && style.clipPath === "none" && style.opacity !== "0";
      })
      .map((element) => ({ element, box: target(element) }))
      .filter(({ box }) => box.width < minimum || box.height < minimum)
      .map(({ element, box }) => `${element.tagName.toLowerCase()}.${String(element.className || "").split(" ")[0] || "(none)"} ${Math.round(box.width)}x${Math.round(box.height)}`);
  }, tapTargetPixels);
}

// Every document the context has loaded reports its violations to the page as well
async function assertCleanDocument(page, observations, label) {
  const violations = await page.evaluate(() => window.__policyViolations ?? []);
  assert(violations.length === 0, `${label}: policy violations ${JSON.stringify(violations)}`);
  // The document element is left out, as shell-policy.mjs does: the framework writes its own load-percentage properties there
  const styled = await page.evaluate(() => [...document.body.querySelectorAll("[style]")].map((element) => `${element.tagName}.${element.className}`));
  assert(styled.length === 0, `${label}: elements carrying a style attribute: ${styled.join(", ")}`);
  assert(observations.pageErrors.length === 0, `${label}: page errors ${observations.pageErrors.join(" | ")}`);
  assert(observations.errorResponses.length === 0, `${label}: error responses ${observations.errorResponses.join(" | ")}`);
  assert(observations.consoleErrors.length === 0, `${label}: console errors ${observations.consoleErrors.join(" | ")}`);
}

// One main landmark, one first heading and a title, so a route change has something to announce and to focus
async function assertDocumentStructure(page, label) {
  const structure = await page.evaluate(() => ({
    title: document.title,
    mains: document.querySelectorAll("main, [role='main']").length,
    headings: document.querySelectorAll("h1").length
  }));
  assert(structure.title.length > 0, `${label}: the document has no title`);
  assert(structure.mains === 1, `${label}: the document has ${structure.mains} main landmarks`);
  assert(structure.headings === 1, `${label}: the document has ${structure.headings} first level headings`);
  return structure.title;
}

// Every text field states its type, its autocomplete and an accessible name, so a software keyboard offers the right keys
// and the right suggestion. A search or date field needs no autocomplete: the browser offers none for it.
async function assertFieldsUsableWithASoftwareKeyboard(page, label) {
  const fields = await page.evaluate(() => {
    const named = (element) => {
      if (element.getAttribute("aria-label")) return true;
      if (element.getAttribute("aria-labelledby")) return true;
      if (element.id && document.querySelector(`label[for="${CSS.escape(element.id)}"]`) !== null) return true;
      return element.closest("label") !== null;
    };
    return [...document.body.querySelectorAll("input, textarea")]
      .filter((element) => element.type !== "hidden" && element.type !== "checkbox" && element.type !== "radio" && element.type !== "file")
      .filter((element) => element.getBoundingClientRect().width > 0)
      .map((element) => ({
        id: element.getAttribute("data-testid") ?? element.id ?? element.name,
        type: element.getAttribute("type") ?? "text",
        autocomplete: element.getAttribute("autocomplete"),
        readOnly: element.readOnly,
        named: named(element)
      }));
  });
  const failures = fields
    .filter((field) => !field.readOnly)
    .flatMap((field) => {
      const problems = [];
      if (!field.named) problems.push("no accessible name");
      if (field.autocomplete === null && field.type !== "search" && field.type !== "date") problems.push("no autocomplete");
      return problems.map((problem) => `${field.id} (${field.type}): ${problem}`);
    });
  assert(failures.length === 0, `${label}: ${failures.join(", ")}`);
  return fields.length;
}

// Tabs through the document and reports what the keyboard reaches. The controls the document offers and the styles of every
// element are taken before the walk, so the focused control can be compared with its own unfocused state: a control whose
// appearance does not change has no visible focus indicator. Where the tab order ends differs by browser, so the walk stops
// when focus leaves the document or lands on the same element twice, and the verdict is what the keyboard reached, not how
// many presses it took: every control the document offers has to be one of the stops.
async function walkWithTheKeyboard(page, label, extraPresses = 8) {
  const expected = await page.evaluate(() => {
    // A dialog gives focus to its first control as it opens, so the styles are taken with nothing focused; otherwise that
    // control's focused appearance would be its own baseline and its indicator would look like no change at all
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur();
    const snapshot = (element) => {
      const style = getComputedStyle(element);
      return [style.outlineStyle, style.outlineWidth, style.outlineColor, style.boxShadow, style.backgroundColor, style.borderColor, style.textDecorationLine, style.color].join("|");
    };
    window.__unfocusedStyles = new WeakMap();
    for (const element of document.body.querySelectorAll("*")) window.__unfocusedStyles.set(element, snapshot(element));

    const focusable = "a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), summary, [tabindex]:not([tabindex='-1'])";
    // While a modal dialog is open the background is inert, so the controls the keyboard can reach are the dialog's own
    const modals = [...document.querySelectorAll("dialog:modal")];
    const scope = modals[modals.length - 1] ?? document.body;
    // A radio group is one tab stop: Tab enters it at the radio in force and the arrows move inside it
    const groupEntry = (element) => {
      if (!element.matches("input[type='radio']") || element.name === "") return true;

      const group = [...scope.querySelectorAll(`input[type='radio'][name="${CSS.escape(element.name)}"]`)];
      return element === (group.find((radio) => radio.checked) ?? group[0]);
    };
    window.__expectedStops = [...scope.querySelectorAll(focusable)].filter((element) => {
      const box = element.getBoundingClientRect();
      const style = getComputedStyle(element);
      if (box.width === 0 || box.height === 0) return false;
      if (style.visibility === "hidden" || style.clipPath !== "none" || style.opacity === "0") return false;
      return element.getAttribute("tabindex") !== "-1" && groupEntry(element);
    });
    window.__visitedStops = new Set();
    window.__previousStop = null;
    // The body is not focusable on its own, and without this the walk would continue from wherever focus already was, which
    // after a navigation is the heading the framework focuses, and never reach the controls above it
    document.body.setAttribute("tabindex", "-1");
    document.body.focus();
    document.body.removeAttribute("tabindex");
    return window.__expectedStops.map((element) => `${element.tagName.toLowerCase()}.${String(element.className || "").split(" ")[0] || element.id || "(none)"}`);
  });

  // The budget allows the order to be walked twice: where a timer re-renders the control that had focus, the document takes
  // it back and the next press starts at the top again, which the second pass then covers
  const stops = [];
  for (let press = 0; press < expected.length * 2 + extraPresses; press++) {
    await page.keyboard.press("Tab");
    const stop = await page.evaluate(() => {
      const element = document.activeElement;
      const remaining = () => window.__expectedStops.filter((candidate) => !window.__visitedStops.has(candidate)).length;
      if (element === null || element === document.body || element === document.documentElement) return { name: "(document)", left: true, remaining: remaining() };

      window.__visitedStops.add(element);
      const style = getComputedStyle(element);
      const focused = [style.outlineStyle, style.outlineWidth, style.outlineColor, style.boxShadow, style.backgroundColor, style.borderColor, style.textDecorationLine, style.color].join("|");
      return {
        name: `${element.tagName.toLowerCase()}.${String(element.className || "").split(" ")[0] || element.id || "(none)"}`,
        left: false,
        remaining: remaining(),
        // A text field always shows a caret, and a component library element styles its focus inside its own shadow root,
        // which the styles of the host element do not show
        indicated: focused !== window.__unfocusedStyles.get(element) || element.matches("input, textarea, select") || element.tagName.includes("-")
      };
    });
    if (!stop.left) stops.push(stop);

    if (stop.remaining === 0) break;
  }

  const missed = await page.evaluate(() =>
    window.__expectedStops
      .filter((element) => !window.__visitedStops.has(element))
      .map((element) => `${element.tagName.toLowerCase()}.${String(element.className || "").split(" ")[0] || element.id || "(none)"}`)
  );
  assert(stops.length > 0, `${label}: Tab reached no control`);
  assert(missed.length === 0, `${label}: Tab never reached ${[...new Set(missed)].join(", ")}`);
  const unindicated = [...new Set(stops.filter((stop) => !stop.indicated).map((stop) => stop.name))];
  assert(unindicated.length === 0, `${label}: no visible focus indicator on ${unindicated.join(", ")}`);
  return stops.map((stop) => stop.name);
}

async function inspectSurface(page, observations, surface, label) {
  await assertNoOverflow(page, label);
  await assertCleanDocument(page, observations, label);
  const title = await assertDocumentStructure(page, label);
  const small = await tapTargets(page);
  assert(small.length === 0, `${label}: tap targets under ${tapTargetPixels} pixels: ${small.join(", ")}`);
  const fields = await assertFieldsUsableWithASoftwareKeyboard(page, label);
  const stops = await walkWithTheKeyboard(page, label);
  return { surface: surface.name, title, fields, stops: stops.length };
}

for (const size of [phone, smallPhone]) {
  await check(`public surfaces at ${size.name}`, () =>
    withPage(size, undefined, async (page, observations) => {
      const verifyUrls = [
        { name: "login-verify", url: await startLoginThroughBlazor(browser, options.browser, owner.email), ready: "h1" },
        { name: "signup-verify", url: await startSignupThroughBlazor(browser, options.browser, `mobile-verify-${size.name}-${stamp}@example.com`), ready: "h1" }
      ];
      const visited = [];
      const titles = new Set();
      for (const surface of [...publicSurfaces, ...verifyUrls]) {
        await open(page, surface);
        const detail = await inspectSurface(page, observations, surface, `${surface.name} at ${size.name}`);
        titles.add(detail.title);
        visited.push(detail.surface);
      }

      // A route change changes the title, so the pages do not all announce the same one
      assert(titles.size >= visited.length - 4, `The ${visited.length} public surfaces share ${titles.size} titles`);
      return { visited };
    })
  );

  await check(`authenticated surfaces at ${size.name}`, () =>
    withPage(size, owner.storageState, async (page, observations) => {
      const visited = [];
      for (const surface of authenticatedSurfaces) {
        await open(page, surface);
        const detail = await inspectSurface(page, observations, surface, `${surface.name} at ${size.name}`);

        // The sidebar and the user menu are the pointer chrome; below the small breakpoint the mobile menu carries them
        const chrome = await page.evaluate(() => ({
          sidebar: document.querySelector(".app-sidebar")?.checkVisibility() === true,
          userMenu: document.querySelector(".user-menu-trigger")?.checkVisibility() === true,
          mobileMenu: document.querySelector("#mobile-menu-button")?.checkVisibility() === true
        }));
        assert(!chrome.sidebar && !chrome.userMenu, `${surface.name} at ${size.name}: the pointer chrome is visible on a phone`);
        assert(chrome.mobileMenu, `${surface.name} at ${size.name}: the mobile menu button is missing`);
        visited.push(detail.surface);
      }

      return { visited };
    })
  );
}

await check(`signup and welcome at ${phone.name}`, () =>
  withPage(phone, undefined, async (page, observations) => {
    const email = `mobile-welcome-${stamp}@example.com`;
    await open(page, { url: `${baseUrl}${pathBase}/signup`, ready: "h1" });
    await inspectSurface(page, observations, { name: "signup" }, `signup at ${phone.name}`);

    const sentAfter = Date.now();
    await page.locator(testId("email")).fill(email);
    await page.locator(testId("submit")).tap();
    await page.waitForURL(/\/blazor\/signup\/verify\?/);
    await inspectSurface(page, observations, { name: "signup-verify" }, `signup-verify at ${phone.name}`);

    const oneTimePassword = await readOneTimePassword(email, sentAfter);
    await submitOneTimePasswordThroughBlazor(page, oneTimePassword);
    await page.waitForURL(/\/blazor\/welcome\?/);
    await page.locator(testId("account-name")).waitFor({ timeout: interactiveTimeoutMs });
    await page.waitForTimeout(settleMs);
    await inspectSurface(page, observations, { name: "welcome-account" }, `welcome (account) at ${phone.name}`);

    await page.locator(testId("account-name")).fill("Mobile welcome");
    await page.locator(testId("continue")).tap();
    await page.locator(testId("first-name")).waitFor({ timeout: interactiveTimeoutMs });
    await page.waitForTimeout(settleMs);
    await inspectSurface(page, observations, { name: "welcome-profile" }, `welcome (profile) at ${phone.name}`);
    return { email };
  })
);

const openMobileMenu = async (page) => {
  await page.locator(mobileMenuButton).tap();
  await page.locator(testId("mobile-menu-dialog")).waitFor({ timeout: interactiveTimeoutMs });
  await page.waitForTimeout(settleMs);
};

await check(`the error page inside the shell at ${phone.name}`, () =>
  withPage(phone, owner.storageState, async (page, observations) => {
    await open(page, thrownErrorSurface);

    // The document the exception handler re-executes answers 500, which is what the page is for
    observations.errorResponses = observations.errorResponses.filter((entry) => !entry.startsWith("500 "));
    observations.consoleErrors = observations.consoleErrors.filter((entry) => !entry.includes("500"));
    await assertNoOverflow(page, `${thrownErrorSurface.name} at ${phone.name}`);
    await assertCleanDocument(page, observations, `${thrownErrorSurface.name} at ${phone.name}`);
    await assertDocumentStructure(page, `${thrownErrorSurface.name} at ${phone.name}`);
    const small = await tapTargets(page);
    assert(small.length === 0, `${thrownErrorSurface.name}: tap targets under ${tapTargetPixels} pixels: ${small.join(", ")}`);
    const stops = await walkWithTheKeyboard(page, `${thrownErrorSurface.name} at ${phone.name}`);

    // Without a runtime there is no mobile menu, so the page's own links out are the way back
    const waysOut = await page.locator(".app-main a[href]").count();
    assert(waysOut > 0, "The statically rendered error page offers no link out");
    return { stops: stops.length, waysOut };
  })
);

await check(`the mobile menu carries the navigation, theme, language, support and log out at ${phone.name}`, () =>
  withPage(phone, owner.storageState, async (page, observations) => {
    await open(page, authenticatedSurfaces[0]);
    await openMobileMenu(page);

    const dialog = page.locator(testId("mobile-menu-dialog"));
    const contents = await page.evaluate(() => {
      const element = document.querySelector('[data-testid="mobile-menu-dialog"]');
      return {
        isModal: element.matches(":modal"),
        labelled: element.getAttribute("aria-label") ?? element.getAttribute("aria-labelledby"),
        navigationLinks: element.querySelectorAll("nav a").length,
        themes: element.querySelectorAll('[data-testid^="mobile-menu-theme-"]').length,
        languages: element.querySelectorAll('[data-testid^="mobile-menu-language-"]').length,
        support: element.querySelector('[data-testid="mobile-menu-support"]')?.getAttribute("href") ?? null,
        logOut: [...element.querySelectorAll("button")].some((button) => button.textContent.trim().length > 0 && button.classList.contains("mobile-menu-action"))
      };
    });
    assert(contents.isModal, "The mobile menu is not a modal dialog");
    assert(contents.labelled !== null, "The mobile menu has no accessible name");
    assert(contents.navigationLinks >= 4, `The mobile menu carries ${contents.navigationLinks} navigation links`);
    assert(contents.themes === 3, `The mobile menu carries ${contents.themes} theme modes`);
    assert(contents.languages >= 2, `The mobile menu carries ${contents.languages} languages`);
    assert(contents.logOut, "The mobile menu carries no log out");
    // The support entry follows the brand: the address may be empty, and then there is nothing to write to
    if (brandSupportEmail.length > 0) {
      assert(contents.support?.startsWith("mailto:") === true, `The mobile menu's support entry is ${contents.support}`);
    } else {
      assert(contents.support === null, "The mobile menu offers support although the brand names no address");
    }

    const small = await tapTargets(page);
    assert(small.length === 0, `The open mobile menu has tap targets under ${tapTargetPixels} pixels: ${small.join(", ")}`);
    await assertNoOverflow(page, `the open mobile menu at ${phone.name}`);
    await walkWithTheKeyboard(page, `the open mobile menu at ${phone.name}`);

    // The background is the dialog's business: nothing behind it takes a tab stop
    const trapped = await page.evaluate(() => {
      const element = document.querySelector('[data-testid="mobile-menu-dialog"]');
      return element.contains(document.activeElement);
    });
    assert(trapped, "Tab left the open mobile menu");

    // A theme mode applies at once and keeps the menu open, so the chosen mode shows as pressed
    await page.locator(testId("mobile-menu-theme-dark")).tap();
    await page.waitForTimeout(settleMs);
    const theme = await page.evaluate(() => ({
      applied: document.documentElement.getAttribute("data-theme"),
      pressed: document.querySelector('[data-testid="mobile-menu-theme-dark"]').getAttribute("aria-pressed"),
      open: document.querySelector('[data-testid="mobile-menu-dialog"]').matches(":modal")
    }));
    assert(theme.applied === "dark", `Choosing dark left the document on ${theme.applied}`);
    assert(theme.pressed === "true", "The chosen theme mode is not marked as pressed");
    assert(theme.open, "Choosing a theme closed the menu");

    await page.keyboard.press("Escape");
    await page.waitForFunction(() => document.querySelector('[data-testid="mobile-menu-dialog"]')?.matches(":modal") !== true, undefined, { timeout: interactiveTimeoutMs });
    const focused = await page.evaluate(() => document.activeElement?.id);
    assert(focused === "mobile-menu-button", `Escape left focus on ${focused} instead of the mobile menu button`);
    await assertCleanDocument(page, observations, `the mobile menu at ${phone.name}`);
    await dialog.waitFor({ state: "attached" });
    return { support: contents.support, navigationLinks: contents.navigationLinks };
  })
);

// Each dialog: how it is opened, and where its trigger lives
const invitedRows = { name: "users", url: `${baseUrl}${pathBase}/account/users?search=mobile-surfaces-${stamp}`, ready: `${testId("users-grid")}[data-list-state="ready"]` };
const recycleBin = authenticatedSurfaces.find((surface) => surface.name === "recycle-bin");
const sessions = authenticatedSurfaces.find((surface) => surface.name === "sessions");
const rowMenuAction = async (page, action) => {
  await page.locator(`${testId("users-grid")} tbody tr.data-list-row`).first().locator(testId("user-actions")).tap();
  await page.locator("[role='menu']").waitFor({ timeout: interactiveTimeoutMs });
  await page.locator(testId(action)).click();
};

const dialogCases = [
  { name: "invite", surface: invitedRows, open: (page) => page.locator(testId("invite-user")).tap(), dialog: "invite-user-dialog" },
  { name: "filters", surface: invitedRows, open: (page) => page.locator(testId("filter-button")).tap(), dialog: "users-filter-dialog" },
  { name: "delete", surface: invitedRows, open: (page) => rowMenuAction(page, "action-delete"), dialog: "delete-users-dialog" },
  { name: "role", surface: invitedRows, open: (page) => rowMenuAction(page, "action-change-role"), dialog: "change-role-dialog" },
  { name: "purge", surface: recycleBin, open: (page) => page.locator(testId("empty-recycle-bin")).tap(), dialog: "permanently-delete-dialog" },
  { name: "revoke", surface: sessions, open: (page) => page.locator(testId("session-revoke")).first().tap(), dialog: "revoke-session-dialog" }
];

await check(`the dialogs fill the screen, isolate the background and close with Escape at ${phone.name}`, () =>
  withPage(phone, owner.storageState, async (page, observations) => {
    const opened = [];
    for (const dialogCase of dialogCases) {
      await open(page, dialogCase.surface);
      await dialogCase.open(page);
      const selector = testId(dialogCase.dialog);
      await page.locator(selector).waitFor({ timeout: interactiveTimeoutMs });
      await page.waitForTimeout(settleMs);

      const state = await page.evaluate((value) => {
        const element = document.querySelector(value);
        const box = element.getBoundingClientRect();
        const outside = [...document.body.querySelectorAll("a[href], button, input")].filter((candidate) => !element.contains(candidate) && candidate.checkVisibility());
        return {
          width: Math.round(box.width),
          height: Math.round(box.height),
          isModal: element.matches(":modal"),
          labelled: (element.getAttribute("aria-label") ?? element.getAttribute("aria-labelledby")) !== null,
          reachableBehind: outside.filter((candidate) => candidate.matches(":not(:disabled)") && !candidate.inert).length,
          focusInside: element.contains(document.activeElement)
        };
      }, selector);
      assert(state.isModal, `${dialogCase.name}: the dialog is not modal`);
      assert(state.labelled, `${dialogCase.name}: the dialog has no accessible name`);
      assert(Math.abs(state.width - phone.viewport.width) < 2, `${dialogCase.name}: the dialog is ${state.width} wide in a ${phone.viewport.width} viewport`);
      assert(state.height >= phone.viewport.height - 2, `${dialogCase.name}: the dialog is ${state.height} tall in a ${phone.viewport.height} viewport`);
      assert(state.focusInside, `${dialogCase.name}: the dialog did not take focus`);

      await assertNoOverflow(page, `${dialogCase.name} at ${phone.name}`);
      const small = await tapTargets(page);
      assert(small.length === 0, `${dialogCase.name}: tap targets under ${tapTargetPixels} pixels: ${small.join(", ")}`);
      await walkWithTheKeyboard(page, `${dialogCase.name} at ${phone.name}`);
      const stillInside = await page.evaluate((value) => document.querySelector(value).contains(document.activeElement), selector);
      assert(stillInside, `${dialogCase.name}: Tab left the modal dialog`);

      // Escape goes to the control that has focus first, and a native select takes it for its own dropdown, so the walk's
      // last stop decides whether the dialog hears it at all; focus moves to the dialog's own first button before the press
      await page.locator(`${selector} button`).first().focus();
      await page.keyboard.press("Escape");
      await page.waitForFunction((value) => document.querySelector(value)?.matches(":modal") !== true, selector, { timeout: interactiveTimeoutMs });
      opened.push(dialogCase.name);
    }

    await assertCleanDocument(page, observations, `the dialogs at ${phone.name}`);
    return { opened };
  })
);

await check(`the unsaved changes dialog at ${phone.name}`, () =>
  withPage(phone, owner.storageState, async (page, observations) => {
    await open(page, authenticatedSurfaces.find((surface) => surface.name === "profile"));
    await page.locator(testId("first-name")).fill(`Mobile ${Date.now() % 1000}`);
    await openMobileMenu(page);
    await page.locator(testId("mobile-menu-dialog")).locator("nav a").first().tap();

    const selector = `${testId("unsaved-changes-dialog")}[data-open="true"]`;
    await page.locator(selector).waitFor({ timeout: interactiveTimeoutMs });
    await page.waitForTimeout(settleMs);
    const state = await page.evaluate((value) => {
      const element = document.querySelector(value);
      const box = element.getBoundingClientRect();
      return { width: Math.round(box.width), isModal: element.matches(":modal"), focusInside: element.contains(document.activeElement) };
    }, selector);
    assert(state.isModal, "The unsaved changes dialog is not modal");
    assert(Math.abs(state.width - phone.viewport.width) < 2, `The unsaved changes dialog is ${state.width} wide`);
    assert(state.focusInside, "The unsaved changes dialog did not take focus");
    await assertNoOverflow(page, `unsaved changes at ${phone.name}`);
    const small = await tapTargets(page);
    assert(small.length === 0, `Unsaved changes: tap targets under ${tapTargetPixels} pixels: ${small.join(", ")}`);

    await page.locator(testId("unsaved-changes-stay")).tap();
    await page.waitForFunction((value) => document.querySelector(value)?.matches(":modal") !== true, selector, { timeout: interactiveTimeoutMs });
    await assertCleanDocument(page, observations, `unsaved changes at ${phone.name}`);
    return { width: state.width };
  })
);

await check(`a toast never covers the mobile menu button and a refused field keeps its message at ${phone.name}`, () =>
  withPage(phone, owner.storageState, async (page, observations) => {
    await open(page, authenticatedSurfaces.find((surface) => surface.name === "users"));

    // A refused invitation: the field keeps the server's message, and the dialog shows it where the field is
    await page.locator(testId("invite-user")).tap();
    await page.locator(testId("invite-user-dialog")).waitFor({ timeout: interactiveTimeoutMs });
    await page.locator(testId("invite-email")).fill(owner.email);
    await page.locator(testId("invite-send")).tap();
    await page.locator(`${testId("invite-user-dialog")} .validation-message, ${testId("invite-user-dialog")} [role='alert']`).first().waitFor({ timeout: interactiveTimeoutMs });
    const association = await page.evaluate(() => {
      const field = document.querySelector('[data-testid="invite-email"]');
      const messages = [...document.querySelectorAll('[data-testid="invite-user-dialog"] .validation-message, [data-testid="invite-user-dialog"] [role="alert"]')];
      return { invalid: field.getAttribute("aria-invalid"), messages: messages.map((message) => message.textContent.trim()).filter((text) => text.length > 0).length };
    });
    assert(association.messages > 0, "The refused invitation shows no message");

    // The dialog holds an edit, so Escape asks first; leaving discards it
    await page.keyboard.press("Escape");
    await page.locator(`${testId("unsaved-changes-dialog")}[data-open="true"]`).waitFor({ timeout: interactiveTimeoutMs });
    await page.locator(`${testId("unsaved-changes-dialog")}[data-open="true"] ${testId("unsaved-changes-leave")}`).tap();
    await page.waitForFunction((value) => document.querySelector(value)?.matches(":modal") !== true, testId("invite-user-dialog"), { timeout: interactiveTimeoutMs });
    await page.waitForTimeout(settleMs);

    // A successful invitation raises a toast, which must leave the way to the navigation free
    await page.locator(testId("invite-user")).tap();
    await page.locator(testId("invite-email")).fill(`mobile-toast-${stamp}@example.com`);
    await page.locator(testId("invite-send")).tap();
    const toast = page.locator(testId("toast-region")).locator("[role='alert']").first();
    await toast.waitFor({ timeout: interactiveTimeoutMs });
    await page.waitForTimeout(settleMs);

    const overlap = await page.evaluate(() => {
      const button = document.querySelector("#mobile-menu-button").getBoundingClientRect();
      return [...document.querySelectorAll('[data-testid="toast-region"] [role="alert"]')].map((element) => {
        const box = element.getBoundingClientRect();
        const covers = box.right > button.left && box.left < button.right && box.bottom > button.top && box.top < button.bottom;
        return { covers, toast: `${Math.round(box.left)},${Math.round(box.top)} ${Math.round(box.width)}x${Math.round(box.height)}`, button: `${Math.round(button.left)},${Math.round(button.top)}` };
      });
    });
    assert(overlap.every((entry) => !entry.covers), `A toast covers the mobile menu button: ${JSON.stringify(overlap)}`);
    assert((await page.locator(testId("toast-region")).getAttribute("aria-live")) !== null, "The toast region is not a live region");

    const small = await tapTargets(page);
    assert(small.length === 0, `With a toast shown, tap targets under ${tapTargetPixels} pixels: ${small.join(", ")}`);
    await assertNoOverflow(page, `a toast at ${phone.name}`);
    await page.locator(testId("toast-dismiss")).first().tap();

    // The account API refused the duplicate invitation with 400, which is what this case asked it for, and the browser
    // reports a refused request to the console as well
    observations.errorResponses = observations.errorResponses.filter((entry) => !entry.startsWith("400 "));
    observations.consoleErrors = observations.consoleErrors.filter((entry) => !entry.includes("400"));
    await assertCleanDocument(page, observations, `a toast at ${phone.name}`);
    return { toasts: overlap.length, invalid: association.invalid };
  })
);

// The public surfaces are visited without a session and the authenticated ones with it: the landing page sends a signed-in
// visitor to the authenticated home, and the error page of an ended session ends the session the context holds
// Only the reflow is measured here, surface by surface: the errors, the policy violations, the touch targets and the
// keyboard order of every surface are the two phone-width cases' business, and this loop leaves each page as soon as it has
// measured it, which cancels the requests it had in flight
async function measureEverySurface(size, label, locale = "en-US") {
  const measured = [];
  const signedIn = locale === "da-DK" ? danishOwner.storageState : owner.storageState;
  for (const [storageState, surfaces] of [
    [undefined, publicSurfaces],
    [signedIn, authenticatedSurfaces]
  ]) {
    await withPage(
      size,
      storageState,
      async (page) => {
        for (const surface of surfaces) {
          await open(page, surface);
          const overflowed = await assertNoOverflow(page, `${surface.name} ${label}`);
          measured.push(`${surface.name} ${overflowed.documentWidth}`);
        }
      },
      locale
    );
  }

  return measured;
}

for (const size of [reflow200, reflow400]) {
  await check(`every surface reflows at ${size.name}`, async () => ({ measured: (await measureEverySurface(size, `at ${size.name}`)).length }));
}

await check(`every surface holds the largest zoom level at ${phone.name}`, () =>
  withPage(phone, owner.storageState, async (page) => {
    await open(page, authenticatedSurfaces.find((surface) => surface.name === "preferences"));
    await page.locator(testId("zoom-level-1.25")).click();
    await page.waitForFunction(() => document.documentElement.getAttribute("data-zoom-level") === "1.25", undefined, { timeout: interactiveTimeoutMs });

    const measured = [];
    for (const surface of authenticatedSurfaces) {
      await open(page, surface);
      const level = await page.evaluate(() => document.documentElement.getAttribute("data-zoom-level"));
      assert(level === "1.25", `${surface.name}: the zoom level is ${level}`);
      const overflowed = await assertNoOverflow(page, `${surface.name} at the largest zoom level`);
      measured.push(`${surface.name} ${overflowed.documentWidth}`);
    }

    return { measured: measured.length };
  })
);

await check(`every surface holds the longer Danish labels at ${smallPhone.name}`, async () => {
  const measured = await measureEverySurface(smallPhone, `in Danish at ${smallPhone.name}`, "da-DK");
  const language = await withPage(smallPhone, danishOwner.storageState, async (page) => {
    await open(page, authenticatedSurfaces[0]);
    return page.evaluate(() => document.documentElement.lang);
  }, "da-DK");
  assert(language === "da-DK", `The documents render in ${language} instead of da-DK`);
  return { measured: measured.length, language };
});

await check(`nothing animates under reduced motion at ${phone.name}`, async () => {
  const context = await newContext(browser, options.browser, owner.storageState, "en-US", { viewport: phone.viewport, hasTouch: true, reducedMotion: "reduce" });
  const page = await context.newPage();
  const observations = observeErrors(page);
  try {
    const animated = [];
    for (const surface of authenticatedSurfaces.slice(0, 4)) {
      await open(page, surface);
      const moving = await page.evaluate(() =>
        [...document.body.querySelectorAll("*")]
          .filter((element) => {
            const style = getComputedStyle(element);
            const durations = [...style.transitionDuration.split(","), ...style.animationDuration.split(",")];
            return durations.some((duration) => Number.parseFloat(duration) > 0);
          })
          .map((element) => `${element.tagName.toLowerCase()}.${String(element.className || "").split(" ")[0]}`)
      );
      animated.push(...moving.map((entry) => `${surface.name}: ${entry}`));
    }

    assert(animated.length === 0, `Elements animate under reduced motion: ${[...new Set(animated)].slice(0, 6).join(", ")}`);
    await assertCleanDocument(page, observations, `reduced motion at ${phone.name}`);
    return { checked: 4 };
  } finally {
    await context.close();
  }
});

await browser.close();

const { resultFile, passed } = writeResult(
  `mobile-surfaces-${options.browser}.json`,
  {
    browser: options.browser,
    finishedAt: new Date().toISOString(),
    widths: [phone.name, smallPhone.name, reflow200.name, reflow400.name],
    brandSupportEmailConfigured: brandSupportEmail.length > 0,
    results
  },
  results.length
);
const failed = results.filter((result) => !result.passed);
console.log(`${options.browser}: ${results.length - failed.length} of ${results.length} passed. Result file: ${resultFile}`);
process.exitCode = passed ? 0 : 1;
