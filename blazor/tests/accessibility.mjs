// The accessibility scan of every Blazor surface in one browser, against the running stack. It is the automated half of
// the bar in .claude/rules/blazor/accessibility.md; the cells that half cannot reach are listed there as manual and are
// recorded by the device pass.
//
// Every public and every authenticated surface is scanned in both cultures with the axe rule set for WCAG 2.1 level A and
// AA, at desktop width and again at phone width for the surfaces whose layout changes there. A violation of serious or
// critical impact fails the case; a moderate or minor one is reported in the result and does not. Each page is also held
// to the invariants the rest of the harness holds every page to: no content security policy violation and no style
// attribute.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started.
// Run: dotnet run --project developer-cli -- blazor-harness accessibility --browser all

import {
  baseUrl,
  launchBrowser,
  newContext,
  parseArguments,
  pathBase,
  policyViolationsOf,
  readOneTimePassword,
  requireFromApplication,
  signUpThroughBlazor,
  startLoginThroughBlazor,
  startSignupThroughBlazor,
  submitOneTimePasswordThroughBlazor,
  writeResult
} from "./support/stack.mjs";

const AxeBuilder = requireFromApplication("@axe-core/playwright").default;
const axeVersion = requireFromApplication("axe-core/package.json").version;

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
const interactiveTimeoutMs = 60_000;
const cultures = ["en-US", "da-DK"];
const desktop = { width: 1280, height: 800 };
const phone = { width: 390, height: 844 };
// The rule set the bar names: WCAG 2.1 level A and AA. Best-practice rules are left out, because a rule outside the
// standard is not a release condition and would make the bar drift with the scanner's own opinions.
const ruleTags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"];
const failingImpacts = ["serious", "critical"];

const browser = await launchBrowser(options.browser);
const stamp = `${options.browser}-${Date.now()}`;
const results = [];
const failures = [];

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

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

function summarize(violations) {
  return violations.map((violation) => ({
    id: violation.id,
    impact: violation.impact,
    help: violation.help,
    nodes: violation.nodes.slice(0, 3).map((node) => node.target.join(" "))
  }));
}

// The document element is left out, as in the other harness scripts: the framework's loader writes its load percentage
// custom properties there through the CSSOM, which predates the shell and which the policy does not govern
async function styledElements(page) {
  return page.evaluate(() =>
    [...document.querySelectorAll("body [style], body[style]")].map((element) => `${element.tagName.toLowerCase()}#${element.id}.${String(element.className || "")}[style=${element.getAttribute("style")}]`)
  );
}

// The scanner injects an inline style element of its own on every run, which this policy blocks and reports. That entry
// is the scanner's, not the page's, and its appearance is one more proof that the policy holds, so each scan judges only
// the violations recorded since the previous one finished.
const policyBaseline = new WeakMap();

function assertNoNewPolicyViolations(context, label) {
  const fresh = policyViolationsOf(context).slice(policyBaseline.get(context) ?? 0);
  assert(fresh.length === 0, `${label}: policy violations ${JSON.stringify(fresh)}`);
}

// One scan of the page as it stands, with the invariants every harness page is held to
async function scan(page, context, label) {
  assertNoNewPolicyViolations(context, label);
  const styled = await styledElements(page);
  assert(styled.length === 0, `${label}: a style attribute was written on ${JSON.stringify(styled)}`);
  const { violations } = await new AxeBuilder({ page }).withTags(ruleTags).analyze();
  policyBaseline.set(context, policyViolationsOf(context).length);
  const blocking = violations.filter((violation) => failingImpacts.includes(violation.impact));
  const other = violations.filter((violation) => !failingImpacts.includes(violation.impact));
  assert(blocking.length === 0, `${label}: ${blocking.length} serious or critical violations ${JSON.stringify(summarize(blocking))}`);
  return { url: page.url(), otherViolations: summarize(other) };
}

async function scanAtBothWidths(page, context, label) {
  await page.setViewportSize(desktop);
  const desktopResult = await scan(page, context, `${label} at ${desktop.width} px`);
  await page.setViewportSize(phone);
  const phoneResult = await scan(page, context, `${label} at ${phone.width} px`);
  return { url: desktopResult.url, desktop: desktopResult.otherViolations, phone: phoneResult.otherViolations };
}

async function withPage(culture, storageState, action) {
  const context = await newContext(browser, options.browser, storageState, culture);
  try {
    const page = await context.newPage();
    await page.setViewportSize(desktop);
    return await action(page, context);
  } finally {
    await context.close();
  }
}

// The public surfaces, in the order a visitor meets them. The two verification pages are reached through a real start, so
// the page renders the state it has in the flow rather than its expired form.
async function publicPages(culture) {
  const account = await signUpThroughBlazor(browser, options.browser, `accessibility-public-${culture}-${stamp}@example.com`, culture);
  const loginVerifyUrl = await startLoginThroughBlazor(browser, options.browser, account.email);
  const signupVerifyUrl = await startSignupThroughBlazor(browser, options.browser, `accessibility-signup-${culture}-${stamp}@example.com`);
  return {
    storageState: account.storageState,
    pages: [
      { name: "landing", url: `${baseUrl}${pathBase}/`, marker: '[data-testid="public-nav"]' },
      { name: "login", url: `${baseUrl}${pathBase}/login`, marker: '[data-testid="email"]' },
      { name: "signup", url: `${baseUrl}${pathBase}/signup`, marker: '[data-testid="email"]' },
      { name: "login-verify", url: loginVerifyUrl, marker: '[data-testid="code"]' },
      { name: "signup-verify", url: signupVerifyUrl, marker: '[data-testid="code"]' },
      { name: "legal", url: `${baseUrl}${pathBase}/legal`, marker: "h1" },
      { name: "terms", url: `${baseUrl}${pathBase}/legal/terms`, marker: "h1" },
      { name: "privacy", url: `${baseUrl}${pathBase}/legal/privacy`, marker: "h1" },
      { name: "dpa", url: `${baseUrl}${pathBase}/legal/dpa`, marker: "h1" }
    ]
  };
}

const authenticatedPages = [
  { name: "app-home", path: "app", marker: "#user-menu-trigger" },
  { name: "app-details", path: "app/details", marker: "#user-menu-trigger" },
  { name: "users", path: "account/users", marker: '[data-testid="users-grid"]' },
  { name: "recycle-bin", path: "account/users/recycle-bin", marker: "#user-menu-trigger" },
  { name: "profile", path: "user/profile", marker: '[data-testid="profile-form"]' },
  { name: "account-settings", path: "account/settings", marker: '[data-testid="account-settings-form"]' },
  { name: "preferences", path: "user/preferences", marker: "#user-menu-trigger" },
  { name: "sessions", path: "user/sessions", marker: "#user-menu-trigger" }
];

for (const culture of cultures) {
  const surface = await publicPages(culture);

  for (const pageDefinition of surface.pages) {
    await check(`${pageDefinition.name} in ${culture} has no serious or critical violation`, () =>
      withPage(culture, undefined, async (page, context) => {
        const response = await page.goto(pageDefinition.url, { waitUntil: "load" });
        assert(response.status() === 200, `${pageDefinition.name} answered ${response.status()}.`);
        await page.locator(pageDefinition.marker).first().waitFor({ state: "visible" });
        return scanAtBothWidths(page, context, `${pageDefinition.name} in ${culture}`);
      })
    );
  }

  await check(`welcome in ${culture} has no serious or critical violation on either step`, () =>
    withPage(culture, undefined, async (page, context) => {
      const email = `accessibility-welcome-${culture}-${stamp}@example.com`;
      await page.goto(`${baseUrl}${pathBase}/signup`, { waitUntil: "load" });
      await page.locator('[data-testid="email"]').fill(email);
      const sentAfter = Date.now();
      await page.locator('[data-testid="submit"]').click();
      await page.waitForURL(/\/blazor\/signup\/verify\?/);
      await submitOneTimePasswordThroughBlazor(page, await readOneTimePassword(email, sentAfter));
      await page.waitForURL(/\/blazor\/welcome\?/);
      await page.locator('[data-testid="account-name"]').waitFor();
      const accountStep = await scanAtBothWidths(page, context, `welcome account step in ${culture}`);

      await page.setViewportSize(desktop);
      await page.locator('[data-testid="account-name"]').fill("Harness account");
      await page.locator('[data-testid="continue"]').click();
      await page.locator('[data-testid="first-name"]').waitFor();
      const profileStep = await scanAtBothWidths(page, context, `welcome profile step in ${culture}`);
      return { accountStep, profileStep };
    })
  );

  for (const pageDefinition of authenticatedPages) {
    await check(`${pageDefinition.name} in ${culture} has no serious or critical violation`, () =>
      withPage(culture, surface.storageState, async (page, context) => {
        await page.goto(`${baseUrl}${pathBase}/${pageDefinition.path}`, { waitUntil: "load" });
        await page.locator(pageDefinition.marker).first().waitFor({ state: "visible", timeout: interactiveTimeoutMs });
        await page.locator('[data-testid="app-shell"][data-tenants-state="loaded"]').waitFor({ timeout: interactiveTimeoutMs });
        return scanAtBothWidths(page, context, `${pageDefinition.name} in ${culture}`);
      })
    );
  }
}

const browserVersion = browser.version();
await browser.close();

const expectedCaseCount = cultures.length * (9 + 1 + authenticatedPages.length);
const verdict = writeResult(`accessibility-${options.browser}.json`, {
  browser: options.browser,
  browserVersion,
  axeVersion,
  ruleTags,
  failingImpacts,
  viewports: { desktop, phone },
  cultures,
  results,
  failures
}, expectedCaseCount);
console.log(`${verdict.passed ? "PASS" : "FAIL"} accessibility on ${options.browser}: ${results.filter((entry) => entry.passed).length} of ${results.length} cases, result ${verdict.resultFile}`);
if (!verdict.passed) process.exit(1);
