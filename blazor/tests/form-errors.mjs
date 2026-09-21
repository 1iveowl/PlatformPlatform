// Form error mapping and error presentation of the Blazor edition, on the Development-only forms fixture, in one browser,
// with Danish as the UI culture so the English API messages are shown unchanged in the other culture.
//
// 1. Static server-rendered form (/blazor/development/form-errors/static, no render mode): the framework's form script
//    shows DataAnnotations messages without posting and without WebAssembly; canned API failures are placed at their fields, unmatched keys and non-validation failures
//    in the accessible form-level alert (detail, then title), a 401 shows nothing, an antiforgery rejection shows a
//    reload link that loads the page as a new document; messages render as text; no WebAssembly runtime request is made.
// 2. Interactive WebAssembly form (/blazor/development/form-errors/interactive): the same mapper places field messages,
//    matches keys case-insensitively, puts unmatched keys in the validation summary, and the next submit clears server
//    messages while DataAnnotations messages remain.
// 3. In-house toast region: detail then title, a transport failure message, field errors without a form, dismissal, no
//    toast for a 401 or a cancellation, and an antiforgery toast whose Reload page action loads a new document.
// Every page asserts zero content security policy violations and no style attribute in the toast region.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started in
// Development.
// Run: dotnet run --project developer-cli -- blazor-harness form-errors --browser all

import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { baseUrl, completeWelcomeThroughBlazor, launchBrowser, newContext, observeErrors, parseArguments, pathBase, readOneTimePassword, resultsFolder, runtimeRequestPattern, submitOneTimePasswordThroughBlazor } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
const interactiveUrl = `${baseUrl}${pathBase}/development/form-errors/interactive`;
const staticUrl = `${baseUrl}${pathBase}/development/form-errors/static`;
const interactiveTimeoutMs = 60_000;
const uiCulture = "da-DK";

// The fixture's canned API messages, as defined in Blazor.Client/Development/FormErrorScenarios.cs, stay English; the
// page's own texts (transport failure, antiforgery recovery, toast title and reload action) are the da-DK resources in
// shared-kernel/SharedKernel.Localization
const messages = {
  nameTooShort: "Name must be at least 3 characters.",
  nameReserved: "Name is reserved.",
  emailTaken: "Email <b>is</b> already in use.",
  tenantLocked: "The tenant is locked.",
  conflictDetail: "The user was changed by someone else.",
  conflictTitle: "Conflict",
  transportFailure: "Serveren kunne ikke nås. Tjek din forbindelse, og prøv igen.",
  antiforgery: "Siden er udløbet. Genindlæs siden, og prøv igen.",
  errorToastTitle: "Noget gik galt",
  reloadPage: "Genindlæs siden",
  nameRequired: "The Name field is required."
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

const testId = (id) => `[data-testid="${id}"]`;

// The browser locale sets Accept-Language, which the host reads for an anonymous visitor and the signup stores as the
// user's locale
function danishContext(storageState) {
  return newContext(browser, options.browser, storageState, uiCulture);
}

// A static form POST answers with a new document; the time origin tells it apart from the page that posted
async function waitForNewDocument(page, previousOrigin) {
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    const origin = await page.evaluate(() => (document.readyState === "complete" ? performance.timeOrigin : null)).catch(() => null);
    if (origin !== null && origin !== previousOrigin) return;
    await page.waitForTimeout(100);
  }
  throw new Error("No new document within 30 seconds.");
}

// Signs up through the Blazor pages with Danish as the requested language, so the session's locale claim is Danish
async function signUpInDanish(email) {
  const context = await danishContext();
  const page = await context.newPage();
  await page.goto(`${baseUrl}${pathBase}/signup`, { waitUntil: "load" });
  await page.locator(testId("email")).fill(email);
  const sentAfter = Date.now();
  await page.locator(testId("submit")).click();
  await page.waitForURL(/\/blazor\/signup\/verify\?/);
  await submitOneTimePasswordThroughBlazor(page, await readOneTimePassword(email, sentAfter));
  await completeWelcomeThroughBlazor(page);
  const storageState = await context.storageState();
  await context.close();
  return storageState;
}

async function texts(page, id) {
  return (await page.locator(testId(id)).allTextContents()).map((text) => text.trim()).filter((text) => text !== "");
}

async function assertCleanPage(page, observations) {
  const state = await page.evaluate(() => ({
    violations: window.__policyViolations ?? [],
    styleAttributes: document.querySelectorAll('[data-testid="toast-region"] [style], [data-testid="toast-region"][style]').length,
    lang: document.documentElement.lang
  }));
  assert(state.violations.length === 0, `Policy violations: ${JSON.stringify(state.violations)}.`);
  assert(state.styleAttributes === 0, `${state.styleAttributes} style attributes in the toast region.`);
  assert(state.lang === uiCulture, `The page culture is ${state.lang}, not ${uiCulture}.`);
  assert(observations.pageErrors.length === 0, `Page errors: ${observations.pageErrors.join(" | ")}.`);
}

async function postStatic(page, scenario, name = "Ada Lovelace", email = "ada@example.com") {
  await page.locator(testId("name")).fill(name);
  await page.locator(testId("email")).fill(email);
  await page.locator(testId("scenario")).selectOption(scenario);
  const origin = await page.evaluate(() => performance.timeOrigin);
  await page.locator(testId("submit")).click();
  await waitForNewDocument(page, origin);
}

await check("static form renders API messages from the POST response and validates without WebAssembly", async () => {
  const context = await danishContext();
  const page = await context.newPage();
  const observations = observeErrors(page);
  const runtimeRequests = [];
  page.on("request", (request) => {
    if (runtimeRequestPattern.test(request.url())) runtimeRequests.push(request.url());
  });
  try {
    await page.goto(staticUrl, { waitUntil: "load" });

    // The framework's static form script validates in the browser without WebAssembly and posts nothing while invalid;
    // the server's own DataAnnotations validation of a POST is covered by the host tests
    const originBeforeInvalidSubmit = await page.evaluate(() => performance.timeOrigin);
    await page.locator(testId("scenario")).selectOption("field-messages");
    await page.locator(testId("submit")).click();
    await page.locator(testId("name-messages"), { hasText: messages.nameRequired }).waitFor();
    assert((await page.evaluate(() => performance.timeOrigin)) === originBeforeInvalidSubmit, "A client-invalid form was posted.");
    assertEqual(await texts(page, "applied-scenario"), [], "A client-invalid submit applied a scenario");

    await postStatic(page, "field-messages");
    assertEqual(await texts(page, "name-messages"), [messages.nameTooShort, messages.nameReserved], "Name messages");
    assertEqual(await texts(page, "email-messages"), [messages.emailTaken], "Email messages");
    assert((await page.locator(`${testId("email-messages")} b`).count()) === 0, "An API message was rendered as markup.");
    assertEqual(await texts(page, "form-error-message"), [], "Form messages for field errors");

    await postStatic(page, "case-insensitive-key");
    assertEqual(await texts(page, "email-messages"), [messages.emailTaken], "Email messages for an upper-case key");

    await postStatic(page, "unmatched-key");
    assertEqual(await texts(page, "form-error-message"), [messages.tenantLocked], "Form messages for an unmatched key");
    assertEqual(await texts(page, "name-messages"), [messages.nameReserved], "Name messages next to an unmatched key");
    assert((await page.locator(testId("form-error")).getAttribute("role")) === "alert", "The form-level region is not an alert.");

    await postStatic(page, "detail");
    assertEqual(await texts(page, "form-error-message"), [messages.conflictDetail], "Detail message");
    assertEqual(await texts(page, "name-messages"), [], "Server messages left from the previous submit");

    await postStatic(page, "title-fallback");
    assertEqual(await texts(page, "form-error-message"), [messages.conflictTitle], "Title fallback message");

    await postStatic(page, "transport-failure");
    assertEqual(await texts(page, "form-error-message"), [messages.transportFailure], "Transport failure message");

    await postStatic(page, "unauthorized");
    assertEqual(await texts(page, "form-error-message"), [], "Messages for a 401");
    assertEqual(await texts(page, "applied-scenario"), ["unauthorized: Suppressed"], "Applied 401");

    await postStatic(page, "antiforgery");
    assertEqual(await texts(page, "form-error-message"), [messages.antiforgery], "Antiforgery message");
    const reload = page.locator(testId("form-error-reload"));
    assert((await reload.textContent()) === messages.reloadPage, "No Reload page link.");
    const origin = await page.evaluate(() => performance.timeOrigin);
    await reload.click();
    await waitForNewDocument(page, origin);
    assert(page.url() === staticUrl, `The reload link went to ${page.url()}.`);
    assert((await page.evaluate(() => performance.timeOrigin)) !== origin, "The reload link did not load a new document.");
    assertEqual(await texts(page, "form-error-message"), [], "Messages after the reload");
    await postStatic(page, "detail");
    assertEqual(await texts(page, "form-error-message"), [messages.conflictDetail], "A POST after the reload");

    await assertCleanPage(page, observations);
    assertEqual(runtimeRequests, [], "WebAssembly runtime requests on the static form");
    assert(observations.errorResponses.length === 0, `Error responses: ${observations.errorResponses.join(" | ")}.`);
  } finally {
    await context.close();
  }
});

const storageState = await signUpInDanish(`form-errors-${stamp}@example.com`);

async function withInteractivePage(action) {
  const context = await danishContext(storageState);
  const page = await context.newPage();
  const observations = observeErrors(page);
  try {
    await page.goto(interactiveUrl, { waitUntil: "load" });
    await page.locator(testId("fixture-interactive"), { hasText: "Interactive: True" }).waitFor({ timeout: interactiveTimeoutMs });
    const detail = await action(page);
    await assertCleanPage(page, observations);
    return detail;
  } finally {
    await context.close();
  }
}

async function submitInteractive(page, scenario, name = "Ada Lovelace", email = "ada@example.com") {
  await page.locator(testId("name")).fill(name);
  await page.locator(testId("email")).fill(email);
  await page.locator(testId("scenario")).selectOption(scenario);
  await submitFromKeyboard(page);
}

// Focusing the button commits the inputs' change events first; the re-render that follows can move the button, which a
// pointer click aimed at its old position would miss
async function submitFromKeyboard(page) {
  await page.locator(testId("submit")).focus();
  await page.keyboard.press("Enter");
}

await check("interactive form maps field, case-insensitive and unmatched keys and clears server messages on the next submit", () =>
  withInteractivePage(async (page) => {
    await submitInteractive(page, "field-messages", "", "");
    await page.locator(testId("name-messages"), { hasText: messages.nameRequired }).waitFor();
    assertEqual(await texts(page, "submit-count"), ["0"], "Submit count after a client-invalid submit");

    await submitInteractive(page, "field-messages");
    await page.locator(testId("submit-count"), { hasText: "1" }).waitFor();
    assertEqual(await texts(page, "name-messages"), [messages.nameTooShort, messages.nameReserved], "Name messages");
    assertEqual(await texts(page, "email-messages"), [messages.emailTaken], "Email messages");
    assert((await page.locator(`${testId("email-messages")} b`).count()) === 0, "An API message was rendered as markup.");

    // The next submit is client-invalid: the server messages go, the DataAnnotations message stays
    await page.locator(testId("name")).fill("");
    await submitFromKeyboard(page);
    await page.locator(testId("name-messages"), { hasText: messages.nameRequired }).waitFor();
    assertEqual(await texts(page, "name-messages"), [messages.nameRequired], "Name messages after the next submit");
    assertEqual(await texts(page, "email-messages"), [], "Email messages after the next submit");

    await submitInteractive(page, "case-insensitive-key");
    await page.locator(testId("submit-count"), { hasText: "2" }).waitFor();
    assertEqual(await texts(page, "email-messages"), [messages.emailTaken], "Email messages for an upper-case key");

    await submitInteractive(page, "unmatched-key");
    await page.locator(testId("submit-count"), { hasText: "3" }).waitFor();
    assert((await page.locator(testId("validation-summary")).textContent()).includes(messages.tenantLocked), "The unmatched key is not in the validation summary.");
    assertEqual(await texts(page, "name-messages"), [messages.nameReserved], "Name messages next to an unmatched key");
  })
);

await check("interactive failures show in the in-house toast region, with no toast for a 401 or a cancellation", () =>
  withInteractivePage(async (page) => {
    const toasts = page.locator(testId("api-failure-toast"));
    const present = async (scenario, expected) => {
      await page.locator(testId(`present-${scenario}`)).click();
      await page.locator(testId("presented-failure"), { hasText: expected }).waitFor();
    };

    await present("detail", "detail: Message");
    assertEqual(await toasts.last().locator(testId("toast-title")).textContent(), messages.errorToastTitle, "Toast title");
    assertEqual(await toasts.last().locator(testId("toast-message")).textContent(), messages.conflictDetail, "Detail toast");
    assert((await toasts.last().getAttribute("role")) === "alert", "The toast is not an alert.");
    assert((await page.locator(testId("toast-region")).getAttribute("aria-live")) === "assertive", "The toast region is not a live region.");

    await present("title-fallback", "title-fallback: Message");
    assertEqual(await toasts.last().locator(testId("toast-message")).textContent(), messages.conflictTitle, "Title fallback toast");

    await present("transport-failure", "transport-failure: Message");
    assertEqual(await toasts.last().locator(testId("toast-message")).textContent(), messages.transportFailure, "Transport failure toast");

    await present("field-messages", "field-messages: FieldValidation");
    const fieldToast = await toasts.last().locator(testId("toast-message")).textContent();
    assert(fieldToast.includes(messages.emailTaken) && fieldToast.includes(messages.nameReserved), `Field errors without a form: ${fieldToast}.`);
    assert((await page.locator(`${testId("toast-region")} b`).count()) === 0, "An API message was rendered as markup.");

    const before = await toasts.count();
    await toasts.last().locator(testId("toast-dismiss")).click();
    await page.waitForFunction((count) => document.querySelectorAll('[data-testid="api-failure-toast"]').length === count, before - 1);

    const countBeforeSuppressed = await page.locator(`${testId("toast-region")} [role="alert"]`).count();
    const url = page.url();
    await present("unauthorized", "unauthorized: Suppressed");
    await present("cancellation", "cancellation: Suppressed");
    await page.waitForTimeout(500);
    assert((await page.locator(`${testId("toast-region")} [role="alert"]`).count()) === countBeforeSuppressed, "A 401 or a cancellation showed a toast.");
    assert(page.url() === url, `Presenting a 401 navigated to ${page.url()}.`);
  })
);

await check("interactive antiforgery rejection shows a toast whose Reload page action loads a new document", () =>
  withInteractivePage(async (page) => {
    await page.locator(testId("present-antiforgery")).click();
    const toast = page.locator(testId("antiforgery-recovery-toast"));
    await toast.waitFor();
    assertEqual(await toast.locator(testId("toast-title")).textContent(), messages.antiforgery, "Antiforgery toast");
    const action = toast.locator(testId("toast-action"));
    assertEqual(await action.textContent(), messages.reloadPage, "Recovery action");
    const origin = await page.evaluate(() => performance.timeOrigin);
    await action.focus();
    await page.keyboard.press("Enter");
    await waitForNewDocument(page, origin);
    assert(page.url() === interactiveUrl, `The recovery action went to ${page.url()}.`);
    assert((await page.evaluate(() => performance.timeOrigin)) !== origin, "The recovery action did not load a new document.");
    await page.locator(testId("fixture-interactive"), { hasText: "Interactive: True" }).waitFor({ timeout: interactiveTimeoutMs });
  })
);

// The accessible state of a field the account API refused, which a screen reader needs and the mapper alone does not
// give: the control says it is invalid and points at the element holding its messages and at the form's alert
async function assertFieldState(page, fieldId, invalid, alertId) {
  const state = await page.locator(testId(fieldId)).evaluate((element) => ({
    invalid: element.getAttribute("aria-invalid"),
    describedBy: element.getAttribute("aria-describedby")
  }));
  assertEqual(state.invalid, invalid ? "true" : null, `${fieldId} aria-invalid`);
  assertEqual(state.describedBy, `${fieldId}-validation ${alertId}`, `${fieldId} aria-describedby`);
  const described = await page.evaluate((ids) => ids.split(" ").every((id) => document.getElementById(id) !== null), state.describedBy);
  assert(described, `${fieldId} is described by an element that does not exist.`);
}

await check("static form marks a field the API refused invalid and describes it with its messages and the alert", async () => {
  const context = await danishContext();
  const page = await context.newPage();
  const observations = observeErrors(page);
  try {
    await page.goto(staticUrl, { waitUntil: "load" });
    await assertFieldState(page, "name", false, "static-form-error");
    await postStatic(page, "field-messages");
    await assertFieldState(page, "name", true, "static-form-error");
    await assertFieldState(page, "email", true, "static-form-error");
    await assertFieldState(page, "scenario", false, "static-form-error");
    assertEqual(await texts(page, "name-messages"), [messages.nameTooShort, messages.nameReserved], "Name messages");
    await assertCleanPage(page, observations);
    return { alert: "static-form-error" };
  } finally {
    await context.close();
  }
});

await check("interactive form marks a field the API refused invalid and clears the mark on the next submit", () =>
  withInteractivePage(async (page) => {
    await assertFieldState(page, "name", false, "interactive-form-error");
    await submitInteractive(page, "field-messages");
    await page.locator(`${testId("name-messages")}`).first().waitFor();
    await assertFieldState(page, "name", true, "interactive-form-error");
    await assertFieldState(page, "email", true, "interactive-form-error");

    // The next submit refuses the email alone, so the mark the previous one left on the name is cleared with its message
    await submitInteractive(page, "case-insensitive-key");
    await page.waitForFunction(() => document.querySelector('[data-testid="name"]')?.getAttribute("aria-invalid") === null, undefined, { timeout: interactiveTimeoutMs });
    await assertFieldState(page, "name", false, "interactive-form-error");
    await assertFieldState(page, "email", true, "interactive-form-error");
    return { alert: "interactive-form-error" };
  })
);

await browser.close();

mkdirSync(resultsFolder, { recursive: true });
writeFileSync(path.join(resultsFolder, `form-errors-${options.browser}.json`), JSON.stringify({ browser: options.browser, results }, null, 2));
if (results.some((result) => !result.passed)) process.exit(1);
