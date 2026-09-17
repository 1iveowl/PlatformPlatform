// The tenant-facing Features section on the Blazor account settings page and the user-facing Feature preferences section on
// the Blazor preferences page, through the gateway against the running stack, in one browser and one culture
// (--culture en-US|da-DK). A new owner is signed up for the run.
//
// Both flags are kill-switch flags, which the reconciler creates globally inactive, and both sections hide a flag whose
// base row is inactive. The run therefore activates account-overview and compact-view through the back office first, the
// way the React specification does; that is a prerequisite, not a case.
//
// 1. The Features section renders for the owner with the localized heading, description and one switch per configurable
//    tenant flag, each named by the flag's localized name and described by its localized description.
// 2. The owner turns account-overview on: the PUT carries { enabled: true }, its x-user-feature-flags response header
//    contains the key, the switch reads checked and the success toast names the flag and the five-minute delay.
// 3. The owner turns it off again: the header no longer contains the key and the switch reads unchecked.
// 4. The Feature preferences section renders on the preferences page with its own heading and description.
// 5. The user turns compact-view on and 6. off again, with the same header, switch and toast assertions and the
//    preferences toast title.
// 7. A refused change leaves the switch as the server has it: the tenant override PUT is routed to 403 with the API's
//    message, which appears as an error toast in English in both cultures, and the switch stays unchecked.
// Every case asserts zero content security policy violations and no page errors, and the cases without an expected error
// response also no console errors.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started.
// Run: dotnet run --project developer-cli -- blazor-harness feature-flags --browser all --culture da-DK

import {
  basePort,
  baseUrl,
  launchBrowser,
  newContext,
  observeErrors,
  parseArguments,
  pathBase,
  policyViolationsOf,
  probeHostConfiguration,
  signUpThroughBlazor,
  writeResult
} from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium", culture: "en-US" });
const settingsUrl = `${baseUrl}${pathBase}/account/settings`;
const preferencesUrl = `${baseUrl}${pathBase}/user/preferences`;
const backOfficeUrl = `https://back-office.dev.localhost:${basePort + 1}`;
const tenantFlagKey = "account-overview";
const userFlagKey = "compact-view";
const tenantOverridePath = `/api/account/feature-flags/${tenantFlagKey}/tenant-override`;
const userOverridePath = `/api/account/feature-flags/${userFlagKey}/user-override`;
const interactiveTimeoutMs = 60_000;
const expectedCaseCount = 7;

const texts = {
  "en-US": {
    features: "Features",
    featuresDescription: "Toggle features available to your account.",
    featurePreferences: "Feature preferences",
    featurePreferencesDescription: "Customize which optional features are enabled for your account.",
    featureUpdated: "Feature updated successfully",
    preferenceUpdated: "Preference updated successfully",
    tenantFlagName: "Account overview page",
    userFlagName: "Compact view",
    userFlagDescription: "Reduce spacing between UI elements for a denser layout",
    delay: "It takes up to 5 minutes for changes to reach all users."
  },
  "da-DK": {
    features: "Funktioner",
    featuresDescription: "Skift funktioner tilgængelige for din konto.",
    featurePreferences: "Funktionspræferencer",
    featurePreferencesDescription: "Tilpas hvilke valgfri funktioner der er aktiveret for din konto.",
    featureUpdated: "Funktion opdateret succesfuldt",
    preferenceUpdated: "Præference opdateret succesfuldt",
    tenantFlagName: "Kontooversigtsside",
    userFlagName: "Kompakt visning",
    userFlagDescription: "Reducér afstanden mellem UI-elementer for et tættere layout",
    delay: "Det tager op til 5 minutter, før ændringer når alle brugere."
  }
}[options.culture];
if (!texts) throw new Error(`Unsupported culture ${options.culture}.`);
// The account API returns its messages in English in every UI culture
const ownerOnlyMessage = "Only owners are allowed to configure tenant feature flags.";

const browser = await launchBrowser(options.browser);
const hostConfiguration = await probeHostConfiguration(browser, options.browser);
const results = [];
const testId = (id) => `[data-testid="${id}"]`;
const switchTestId = (flagKey) => testId(`feature-flag-${flagKey}`);

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function check(name, action) {
  try {
    const detail = await action();
    results.push({ name, passed: true, detail });
    console.log(`PASS ${name}${detail ? `: ${detail}` : ""}`);
  } catch (error) {
    // The first line only: a request failure's call log carries the session cookies
    const message = String(error.message).split("\n")[0];
    results.push({ name, passed: false, detail: message });
    console.log(`FAIL ${name}: ${message}`);
  }
}

// Both flags are created globally inactive by the reconciler, and a section hides a flag whose base row is inactive, so the
// back office activates them first. The PUT is idempotent and leaves the same end state for a concurrent run.
async function activateFlagsThroughBackOffice() {
  const context = await newContext(browser, options.browser);
  try {
    const page = await context.newPage();
    // The development mock of the platform's identity provider: the callback sets the back-office session cookie directly
    await page.goto(`${backOfficeUrl}/.auth/login/aad/callback?identity=admin&post_login_redirect_uri=%2Ffeature-flags`, { waitUntil: "load" });
    await page.waitForFunction(() => (document.head.querySelector('meta[name="antiforgeryToken"]')?.getAttribute("content") ?? "").length > 0, undefined, { timeout: interactiveTimeoutMs });
    const antiforgeryToken = await page.evaluate(() => document.head.querySelector('meta[name="antiforgeryToken"]').getAttribute("content"));
    assert(antiforgeryToken.length > 0, "The back office served no antiforgery token, so the admin session did not start.");
    for (const flagKey of [tenantFlagKey, userFlagKey]) {
      // Sent from the document, so it goes through the browser's network stack and the development certificate the
      // context already trusts, and carries the back-office session cookie
      const status = await page.evaluate(
        async ({ flagKey, antiforgeryToken }) => {
          const response = await fetch(`/api/back-office/feature-flags/${flagKey}/activate`, {
            method: "PUT",
            credentials: "same-origin",
            headers: { "x-xsrf-token": antiforgeryToken }
          });
          return response.status;
        },
        { flagKey, antiforgeryToken }
      );
      assert(status >= 200 && status < 300, `Activating '${flagKey}' returned ${status}.`);
    }
  } finally {
    await context.close();
  }
}

// A signed-in page on one of the two surfaces, with every override request and response recorded
async function withPage(url, readyTestId, action, { strictConsole = true } = {}) {
  const context = await newContext(browser, options.browser, signedUp.storageState, options.culture);
  try {
    const page = await context.newPage();
    const observations = observeErrors(page);
    const overrideResponses = [];
    page.on("response", (response) => {
      const path = new URL(response.url()).pathname;
      if (path === tenantOverridePath || path === userOverridePath) overrideResponses.push({ path, status: response.status() });
    });
    const overrideRequests = [];
    page.on("request", (request) => {
      const path = new URL(request.url()).pathname;
      if (path === tenantOverridePath || path === userOverridePath) overrideRequests.push({ path, method: request.method(), body: request.postData() });
    });
    await page.goto(url, { waitUntil: "load" });
    await page.locator(testId(readyTestId)).waitFor({ timeout: interactiveTimeoutMs });
    const lang = await page.locator("html").getAttribute("lang");
    assert(lang === options.culture, `The page rendered in ${lang}, not ${options.culture}.`);
    let detail;
    try {
      detail = await action(page, { overrideRequests, overrideResponses });
    } catch (error) {
      const consoleErrors = observations.consoleErrors.map((text) => text.replace(/\s+/g, " ").slice(0, 200));
      throw new Error(
        `${String(error.message).split("\n")[0]} (overrides: ${JSON.stringify(overrideResponses)}; console: ${consoleErrors.join(" | ")}; page errors: ${observations.pageErrors.join(" | ")})`
      );
    }
    const violations = policyViolationsOf(context);
    assert(violations.length === 0, `${violations.length} content security policy violations: ${JSON.stringify(violations)}`);
    assert(observations.pageErrors.length === 0, `Page errors: ${observations.pageErrors.join(" | ")}`);
    if (strictConsole) assert(observations.consoleErrors.length === 0, `Console errors: ${observations.consoleErrors.join(" | ")}`);
    return detail;
  } finally {
    await context.close();
  }
}

// Clicks the switch, waits for the override response and for the section to have read the list again and shown its toast,
// and returns the override response with the feature flag header the gateway put on it
async function toggle(page, flagKey, overridePath, toastTestIdName) {
  const responded = page.waitForResponse((response) => new URL(response.url()).pathname === overridePath, { timeout: interactiveTimeoutMs });
  await page.locator(switchTestId(flagKey)).click();
  const raw = await responded;
  const response = { path: overridePath, status: raw.status(), featureFlags: raw.headers()["x-user-feature-flags"] ?? null };
  await page.locator(testId(toastTestIdName)).waitFor({ timeout: interactiveTimeoutMs });
  return response;
}

function switchState(page, flagKey) {
  return page.locator(switchTestId(flagKey)).getAttribute("aria-checked");
}

// The flag key as the response header reports it, which is the evaluated set of the refreshed access token
function headerContains(response, flagKey) {
  assert(response.featureFlags !== null, `The ${response.path} response carried no x-user-feature-flags header.`);
  return response.featureFlags.split(",").map((key) => key.trim()).includes(flagKey);
}

async function expectToast(page, testIdName, title, flagName) {
  const toast = page.locator(testId(testIdName));
  await toast.waitFor({ timeout: interactiveTimeoutMs });
  const shownTitle = await toast.locator(testId("toast-title")).textContent();
  const shownMessage = await toast.locator(testId("toast-message")).textContent();
  assert(shownTitle.trim() === title, `The toast title was "${shownTitle.trim()}", not "${title}".`);
  assert(shownMessage.trim() === `${flagName}. ${texts.delay}`, `The toast message was "${shownMessage.trim()}".`);
  await toast.locator(testId("toast-dismiss")).click();
  await toast.waitFor({ state: "hidden" });
}

console.log("Activating the two kill-switch flags through the back office...");
await activateFlagsThroughBackOffice();
console.log("Signing up the owner...");
const stamp = `${options.browser}-${options.culture}-${Date.now()}`.toLowerCase();
const signedUp = await signUpThroughBlazor(browser, options.browser, `feature-flags-${stamp}@example.com`, options.culture);
console.log("Owner signed up.");

await check("the Features section renders for the owner with the flag's localized name and description", () =>
  withPage(settingsUrl, "account-features", async (page) => {
    const section = page.locator(testId("account-features"));
    const heading = await section.locator("h2").textContent();
    const description = await section.locator("p").first().textContent();
    assert(heading.trim() === texts.features, `The heading was "${heading.trim()}", not "${texts.features}".`);
    assert(description.trim() === texts.featuresDescription, `The description was "${description.trim()}".`);
    const flagSwitch = page.locator(switchTestId(tenantFlagKey));
    assert((await flagSwitch.getAttribute("role")) === "switch", "The control is not a switch.");
    const name = await page.locator(`#feature-flag-${tenantFlagKey}-name`).textContent();
    assert(name.trim() === texts.tenantFlagName, `The switch is named "${name.trim()}", not "${texts.tenantFlagName}".`);
    assert((await flagSwitch.getAttribute("aria-labelledby")) === `feature-flag-${tenantFlagKey}-name`, "The switch is not named by the flag's name.");
    assert((await flagSwitch.getAttribute("aria-describedby")) === `feature-flag-${tenantFlagKey}-description`, "The switch is not described by the flag's description.");
    assert((await page.locator(`#feature-flag-${tenantFlagKey}-description`).textContent()).trim().length > 0, "The flag has no description.");
    assert((await page.locator(`${testId("account-features")} [style]`).count()) === 0, "The section wrote a style attribute.");
    return `heading, description and the ${tenantFlagKey} switch named "${texts.tenantFlagName}"`;
  })
);

await check("the owner turns the tenant flag on and the response header and the switch agree", () =>
  withPage(settingsUrl, "account-features", async (page, recorded) => {
    assert((await switchState(page, tenantFlagKey)) === "false", "The tenant flag was already on before the run.");
    const response = await toggle(page, tenantFlagKey, tenantOverridePath, "feature-updated-toast");
    assert(response.status === 204 || response.status === 200, `The override returned ${response.status}.`);
    assert(JSON.parse(recorded.overrideRequests.at(-1).body).enabled === true, "The override did not ask for enabled true.");
    assert(headerContains(response, tenantFlagKey), `The header "${response.featureFlags}" does not contain ${tenantFlagKey}.`);
    assert((await switchState(page, tenantFlagKey)) === "true", "The switch did not read checked after the change.");
    await expectToast(page, "feature-updated-toast", texts.featureUpdated, texts.tenantFlagName);
    return `header "${response.featureFlags}" and the switch both on`;
  })
);

await check("the owner turns the tenant flag off and the header no longer carries it", () =>
  withPage(settingsUrl, "account-features", async (page, recorded) => {
    assert((await switchState(page, tenantFlagKey)) === "true", "The flag was not on at the start of the case.");
    const response = await toggle(page, tenantFlagKey, tenantOverridePath, "feature-updated-toast");
    assert(JSON.parse(recorded.overrideRequests.at(-1).body).enabled === false, "The override did not ask for enabled false.");
    assert(!headerContains(response, tenantFlagKey), `The header "${response.featureFlags}" still contains ${tenantFlagKey}.`);
    assert((await switchState(page, tenantFlagKey)) === "false", "The switch did not read unchecked after the change.");
    await expectToast(page, "feature-updated-toast", texts.featureUpdated, texts.tenantFlagName);
    return `header "${response.featureFlags}" and the switch both off`;
  })
);

await check("the Feature preferences section renders on the preferences page", () =>
  withPage(preferencesUrl, "preferences-feature-flags", async (page) => {
    const section = page.locator(testId("preferences-feature-flags"));
    const heading = await section.locator("h2").textContent();
    const description = await section.locator("p").first().textContent();
    assert(heading.trim() === texts.featurePreferences, `The heading was "${heading.trim()}".`);
    assert(description.trim() === texts.featurePreferencesDescription, `The description was "${description.trim()}".`);
    const name = await page.locator(`#feature-flag-${userFlagKey}-name`).textContent();
    const flagDescription = await page.locator(`#feature-flag-${userFlagKey}-description`).textContent();
    assert(name.trim() === texts.userFlagName, `The switch is named "${name.trim()}".`);
    assert(flagDescription.trim() === texts.userFlagDescription, `The description was "${flagDescription.trim()}".`);
    assert((await page.locator(`${testId("preferences-feature-flags")} [style]`).count()) === 0, "The section wrote a style attribute.");
    return `heading, description and the ${userFlagKey} switch named "${texts.userFlagName}"`;
  })
);

await check("the user turns the user flag on and the response header and the switch agree", () =>
  withPage(preferencesUrl, "preferences-feature-flags", async (page, recorded) => {
    assert((await switchState(page, userFlagKey)) === "false", "The user flag was already on before the run.");
    const response = await toggle(page, userFlagKey, userOverridePath, "preference-updated-toast");
    assert(JSON.parse(recorded.overrideRequests.at(-1).body).enabled === true, "The override did not ask for enabled true.");
    assert(headerContains(response, userFlagKey), `The header "${response.featureFlags}" does not contain ${userFlagKey}.`);
    assert((await switchState(page, userFlagKey)) === "true", "The switch did not read checked after the change.");
    await expectToast(page, "preference-updated-toast", texts.preferenceUpdated, texts.userFlagName);
    return `header "${response.featureFlags}" and the switch both on`;
  })
);

await check("the user turns the user flag off and the header no longer carries it", () =>
  withPage(preferencesUrl, "preferences-feature-flags", async (page, recorded) => {
    assert((await switchState(page, userFlagKey)) === "true", "The flag was not on at the start of the case.");
    const response = await toggle(page, userFlagKey, userOverridePath, "preference-updated-toast");
    assert(JSON.parse(recorded.overrideRequests.at(-1).body).enabled === false, "The override did not ask for enabled false.");
    assert(!headerContains(response, userFlagKey), `The header "${response.featureFlags}" still contains ${userFlagKey}.`);
    assert((await switchState(page, userFlagKey)) === "false", "The switch did not read unchecked after the change.");
    await expectToast(page, "preference-updated-toast", texts.preferenceUpdated, texts.userFlagName);
    return `header "${response.featureFlags}" and the switch both off`;
  })
);

await check(
  "a refused change shows the API message as a toast and leaves the switch as the server has it",
  () =>
    withPage(
      settingsUrl,
      "account-features",
      async (page, recorded) => {
        await page.route(`**${tenantOverridePath}`, (route) =>
          route.fulfill({
            status: 403,
            contentType: "application/problem+json",
            body: JSON.stringify({ title: "Forbidden", status: 403, detail: ownerOnlyMessage })
          })
        );
        await page.locator(switchTestId(tenantFlagKey)).click();
        const toast = page.locator(testId("api-failure-toast"));
        await toast.waitFor({ timeout: interactiveTimeoutMs });
        const message = await toast.locator(testId("toast-message")).textContent();
        assert(message.trim() === ownerOnlyMessage, `The toast message was "${message.trim()}", not the API's message.`);
        assert((await switchState(page, tenantFlagKey)) === "false", "The refused change moved the switch.");
        assert((await page.locator(testId("feature-updated-toast")).count()) === 0, "A success toast was shown for a refused change.");
        assert(recorded.overrideRequests.length === 1, `The click sent ${recorded.overrideRequests.length} override requests.`);
        return `"${ownerOnlyMessage}" shown in ${options.culture} and the switch unchanged`;
      },
      { strictConsole: false }
    )
);

await browser.close();

const verdict = writeResult(`feature-flags-${options.browser}-${options.culture}.json`, { browser: options.browser, culture: options.culture, ...hostConfiguration, results }, expectedCaseCount);
console.log(`Result file: ${verdict.resultFile}`);
if (!verdict.passed) process.exit(1);
