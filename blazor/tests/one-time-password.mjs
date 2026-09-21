// The one-time password input of the static verification pages, in one browser and in both cultures, on the Development
// stack with the codes the account API mails (never a debug override).
//
// With the module: the server-rendered input is enhanced into six presentation slots over the same labelled input.
// 1. Signup completes by pasting the six characters: they fill the slots, upper case, and submit once without a click.
// 2. Login from the enhanced login page completes by typing lower case one key at a time; the active slot follows the arrow
//    keys and Backspace, and the code posts once.
// 3. Wrong codes show the wrong-code state with the API message beneath it, clear the input and focus it; a concurrent
//    auto-submit and click post once; the fourth attempt shows the locked state with the input and Verify disabled, and
//    enabling them again in the page is still refused by the API.
// 4. The first resend invalidates the first code (wrong-code state); the second resend shows the locked state with the
//    input disabled; a new load of the page completes login with the resent code.
// 5. A start beyond three open confirmations for the address shows the API message on the login and signup pages.
// 6. The countdown announces the expiry in its polite live region; a rejected code then shows the expired state. The query
//    string only drives the display (it is shortened here to reach expiry), so the API still accepts the valid code.
// Without scripts (javaScriptEnabled false): signup and the welcome setup complete through the plain input and Verify, and
// login completes with a resent code after a reload reveals the resend action.
//
// Every page with scripts asserts zero content security policy violations, no style attribute and no WebAssembly runtime
// request on the verification pages. A page without scripts cannot report violations, so those cases assert the flow only.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started in
// Development.
// Run: dotnet run --project developer-cli -- blazor-harness one-time-password --browser all [--culture en-US|da-DK|all]

import { readFileSync } from "node:fs";
import path from "node:path";
import {
  baseUrl,
  completeWelcomeThroughBlazor,
  launchBrowser,
  newContext,
  observeErrors,
  parseArguments,
  pathBase,
  policyViolationsOf,
  readOneTimePassword,
  repositoryRoot,
  runtimeRequestPattern,
  writeResult
} from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium", culture: "all" });
const cultures = options.culture === "all" ? ["en-US", "da-DK"] : [options.culture];
const casesPerCulture = 8;
const codeLength = 6;
const resendDelayMs = 31_000;
const maximumValidForSeconds = 300;
const secondsBeforeExpiry = 4;

// The account API's messages, shown in English as returned in every culture
const apiMessages = {
  wrongCode: "The code is wrong or no longer valid.",
  tooManyAttempts: "Too many attempts, please request a new code.",
  tooManyStarts: "Too many attempts to confirm this email address. Please try again later."
};

const browser = await launchBrowser(options.browser);
const results = [];

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

// The page texts come from the localization resources the host renders, so the script follows a changed translation
function readStrings(culture) {
  const suffix = culture === "en-US" ? "" : `.${culture}`;
  const file = path.join(repositoryRoot, `application/shared-kernel/SharedKernel.Localization/Resources/AuthenticationStrings${suffix}.resx`);
  const strings = {};
  for (const match of readFileSync(file, "utf8").matchAll(/<data name="([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>/g)) {
    strings[match[1]] = match[2].replaceAll("&lt;", "<").replaceAll("&gt;", ">").replaceAll("&quot;", '"').replaceAll("&amp;", "&");
  }
  return strings;
}

function uniqueEmail(label, culture) {
  return `otp-${label}-${culture.toLowerCase()}-${options.browser}-${Date.now()}@example.com`;
}

// A code that differs from the mailed one in its first letter, so it is always wrong
function wrongCodeFor(code) {
  return `${code[0] === "A" ? "B" : "A"}${code.slice(1)}`;
}

// Observes a page on a verification flow: the posts of the verification form, WebAssembly runtime requests made while a
// verification page is shown, and page errors
function observeVerification(page) {
  const observed = { verifyPosts: 0, runtimeRequests: [], errors: observeErrors(page) };
  page.on("request", (request) => {
    const url = new URL(request.url());
    if (request.method() === "POST" && url.pathname.endsWith("/verify")) observed.verifyPosts++;
    if (runtimeRequestPattern.test(request.url()) && page.url().includes("/verify")) observed.runtimeRequests.push(request.url());
  });
  return observed;
}

// A static form post answers with a new document; the time origin tells it apart from the page that posted
async function waitForNewDocument(page, previousOrigin) {
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    const origin = await page.evaluate(() => (document.readyState === "complete" ? performance.timeOrigin : null)).catch(() => null);
    if (origin !== null && origin !== previousOrigin) return;
    await page.waitForTimeout(100);
  }
  throw new Error("No new document within 30 seconds.");
}

async function timeOrigin(page) {
  return page.evaluate(() => performance.timeOrigin);
}

// The enhanced control's state as the browser shows it
async function controlState(page) {
  return page.evaluate(() => {
    const root = document.querySelector("[data-one-time-password]");
    const input = root.querySelector("input");
    const slots = [...root.querySelectorAll("[data-one-time-password-slot]")];
    return {
      enhanced: root.classList.contains("one-time-password-enhanced"),
      slotsHidden: root.querySelector("[data-one-time-password-slots]").hidden,
      state: root.dataset.verificationState ?? null,
      value: input.value,
      slotTexts: slots.map((slot) => slot.textContent),
      activeSlot: slots.findIndex((slot) => slot.classList.contains("one-time-password-slot-active")),
      focused: document.activeElement === input,
      inputDisabled: input.disabled,
      verifyDisabled: document.querySelector('[data-testid="submit"]').disabled,
      describedBy: input.getAttribute("aria-describedby"),
      autocomplete: input.getAttribute("autocomplete"),
      maxLength: input.maxLength,
      fontSizePx: Number.parseFloat(getComputedStyle(input).fontSize)
    };
  });
}

// The active slot follows the caret through selectionchange, which the browser dispatches after the key event
async function waitForActiveSlot(page, index, label) {
  try {
    await page.waitForFunction(
      (expected) => [...document.querySelectorAll("[data-one-time-password-slot]")].findIndex((slot) => slot.classList.contains("one-time-password-slot-active")) === expected,
      index,
      { timeout: 2_000 }
    );
  } catch {
    throw new Error(`${label}: expected ${index}, got ${(await controlState(page)).activeSlot}.`);
  }
}

// The verification page's accessible structure: the input is found by its label, the slots are hidden from assistive
// technology, and the ids the input is described by exist
async function assertAccessibleInput(page, label) {
  const input = page.getByLabel(label, { exact: true });
  assertEqual(await input.getAttribute("data-testid"), "code", "The input labelled with the verification code label");
  const state = await controlState(page);
  assertEqual(state.autocomplete, "one-time-code", "autocomplete");
  assertEqual(state.maxLength, codeLength, "maxlength");
  const describedByTargets = await page.evaluate(
    (ids) => ids.split(" ").every((id) => document.getElementById(id) !== null),
    state.describedBy
  );
  assert(describedByTargets, `Not every id in aria-describedby "${state.describedBy}" exists.`);
  // iOS Safari zooms the page when the field it focuses computes below 16px, which puts the slots off screen. The
  // enhanced input draws its value in the slots beside it and its own text is transparent, so this size is read by the
  // browser alone and nothing but the zoom depends on it.
  assert(state.fontSizePx >= 16, `The code input computes ${state.fontSizePx}px, below the 16px iOS zooms at.`);
  assertEqual(await page.locator("[data-one-time-password-slots]").getAttribute("aria-hidden"), "true", "The slots' aria-hidden");
  return state;
}

// The state label and the API message beneath it, and the input's association with them
async function assertOutcome(page, expectedState, expectedTitle, expectedMessage) {
  await page.locator(`[data-verification-state="${expectedState}"]`).waitFor({ timeout: 15_000 });
  assertEqual((await page.locator(testId("form-error-title")).textContent()).trim(), expectedTitle, "State label");
  const messages = (await page.locator(testId("form-error-message")).allTextContents()).map((text) => text.trim());
  assertEqual(messages, [expectedMessage], "API message beneath the state");
  const state = await controlState(page);
  assert(state.describedBy.split(" ").includes(await page.locator(testId("form-error")).getAttribute("id")), "The input is not described by the error alert.");
  return state;
}

async function assertCleanDocument(page, context, observed, culture) {
  const documentState = await page.evaluate(() => ({ styleAttributes: document.querySelectorAll("[style]").length, lang: document.documentElement.lang }));
  assertEqual(documentState.styleAttributes, 0, "Style attributes");
  assertEqual(documentState.lang, culture, "Document culture");
  const violations = policyViolationsOf(context);
  assert(violations.length === 0, `Policy violations: ${JSON.stringify(violations)}.`);
  assert(observed.runtimeRequests.length === 0, `WebAssembly runtime requests on a verification page: ${observed.runtimeRequests.join(", ")}.`);
  assert(observed.errors.pageErrors.length === 0, `Page errors: ${observed.errors.pageErrors.join(" | ")}.`);
}

// Types a code into the focused input as a paste does: all characters in one input event
async function paste(page, text) {
  await page.getByTestId("code").focus();
  await page.keyboard.insertText(text);
}

async function startLogin(page, email) {
  await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
  await page.locator(testId("email")).fill(email);
  const sentAfter = Date.now();
  await page.locator(testId("submit")).click();
  await page.waitForURL(/\/blazor\/login\/verify\?/);
  await page.locator("[data-one-time-password]").waitFor();
  return sentAfter;
}

async function waitForResend(page) {
  await page.locator(testId("resend-code")).waitFor({ state: "visible", timeout: resendDelayMs + 10_000 });
}

async function runCulture(culture) {
  const strings = readStrings(culture);
  const signedUpEmail = uniqueEmail("a", culture);
  const noScriptEmail = uniqueEmail("b", culture);
  const withContext = async (action, contextOptions = {}) => {
    const context = contextOptions.javaScriptEnabled === false
      ? await browser.newContext({ ignoreHTTPSErrors: options.browser !== "chromium", locale: culture, javaScriptEnabled: false })
      : await newContext(browser, options.browser, undefined, culture);
    try {
      const page = await context.newPage();
      return await action(page, context);
    } finally {
      await context.close();
    }
  };

  await check(`${culture}: signup completes by pasting the six characters, which fill the slots and submit once`, () =>
    withContext(async (page, context) => {
      const observed = observeVerification(page);
      await page.goto(`${baseUrl}${pathBase}/signup`, { waitUntil: "load" });
      await page.locator(testId("email")).fill(signedUpEmail);
      const sentAfter = Date.now();
      await page.locator(testId("submit")).click();
      await page.waitForURL(/\/blazor\/signup\/verify\?/);
      await page.locator(".one-time-password-enhanced").waitFor();
      const initial = await assertAccessibleInput(page, strings.SignupVerificationCode);
      assert(initial.enhanced && !initial.slotsHidden, "The input is not enhanced into slots.");
      assert(initial.focused, "The input is not focused on load.");
      await waitForActiveSlot(page, 0, "Active slot on load");
      await assertCleanDocument(page, context, observed, culture);

      const code = await readOneTimePassword(signedUpEmail, sentAfter);
      const origin = await timeOrigin(page);
      const welcome = page.waitForURL(/\/blazor\/welcome\?/);
      await paste(page, code.toLowerCase());
      await welcome;
      assert((await timeOrigin(page)) !== origin, "The completed signup did not load a new document.");
      assertEqual(observed.verifyPosts, 1, "Verification posts");
      assert(observed.runtimeRequests.length === 0, "A WebAssembly runtime request was made on the verification page.");
      await completeWelcomeThroughBlazor(page);
      assert(policyViolationsOf(context).length === 0, `Policy violations: ${JSON.stringify(policyViolationsOf(context))}.`);
      return { verifyPosts: observed.verifyPosts };
    })
  );

  await check(`${culture}: login from the login page completes by typing lower case, with the active slot following arrows and Backspace`, () =>
    withContext(async (page, context) => {
      const observed = observeVerification(page);
      const sentAfter = await startLogin(page, signedUpEmail);
      await page.locator(".one-time-password-enhanced").waitFor();
      await assertAccessibleInput(page, strings.LoginVerificationCode);
      const code = await readOneTimePassword(signedUpEmail, sentAfter);

      await page.keyboard.type(code.slice(0, 3).toLowerCase(), { delay: 30 });
      let state = await controlState(page);
      assertEqual(state.value, code.slice(0, 3), "Value after typing three lower case letters");
      assertEqual(state.slotTexts, [...code.slice(0, 3), "", "", ""], "Slots after three letters");
      await waitForActiveSlot(page, 3, "Active slot after three letters");
      await page.keyboard.press("ArrowLeft");
      await page.keyboard.press("ArrowLeft");
      await waitForActiveSlot(page, 1, "Active slot after two ArrowLeft");
      await page.keyboard.press("ArrowRight");
      await page.keyboard.press("ArrowRight");
      await waitForActiveSlot(page, 3, "Active slot after two ArrowRight");
      await page.keyboard.press("Backspace");
      state = await controlState(page);
      assertEqual(state.value, code.slice(0, 2), "Value after Backspace");
      await waitForActiveSlot(page, 2, "Active slot after Backspace");
      assertEqual(observed.verifyPosts, 0, "Posts before six characters");
      await assertCleanDocument(page, context, observed, culture);

      const origin = await timeOrigin(page);
      const home = page.waitForURL(`${baseUrl}${pathBase}/app`);
      await page.keyboard.type(code.slice(2).toLowerCase(), { delay: 30 });
      await home;
      assert((await timeOrigin(page)) !== origin, "The completed login did not load a new document.");
      assertEqual(observed.verifyPosts, 1, "Verification posts");
      assert(policyViolationsOf(context).length === 0, `Policy violations: ${JSON.stringify(policyViolationsOf(context))}.`);
      return { verifyPosts: observed.verifyPosts };
    })
  );

  await check(`${culture}: wrong codes show the wrong-code state, clear and focus the input, and the fourth attempt locks it`, () =>
    withContext(async (page, context) => {
      const observed = observeVerification(page);
      const sentAfter = await startLogin(page, signedUpEmail);
      const code = await readOneTimePassword(signedUpEmail, sentAfter);
      const wrongCode = wrongCodeFor(code);

      // An auto-submit and a click in the same task post once; a second post would also spend a second attempt
      let origin = await timeOrigin(page);
      await page.evaluate((value) => {
        const input = document.querySelector('[data-testid="code"]');
        input.value = value;
        input.dispatchEvent(new Event("input", { bubbles: true }));
        document.querySelector('[data-testid="submit"]').click();
      }, wrongCode);
      await waitForNewDocument(page, origin);
      let state = await assertOutcome(page, "wrong-code", strings.VerificationStateWrongCode, apiMessages.wrongCode);
      await page.waitForTimeout(500);
      assertEqual(observed.verifyPosts, 1, "Posts of an auto-submit and a click");
      assertEqual(state.value, "", "Value after a wrong code");
      assert(state.focused, "The input is not focused after a wrong code.");
      await waitForActiveSlot(page, 0, "Active slot after a wrong code");
      assert(!state.inputDisabled && !state.verifyDisabled, "The input is disabled after a wrong code.");
      await assertCleanDocument(page, context, observed, culture);

      for (let attempt = 2; attempt <= 3; attempt++) {
        origin = await timeOrigin(page);
        await paste(page, wrongCode);
        await waitForNewDocument(page, origin);
        await assertOutcome(page, "wrong-code", strings.VerificationStateWrongCode, apiMessages.wrongCode);
      }

      origin = await timeOrigin(page);
      await paste(page, wrongCode);
      await waitForNewDocument(page, origin);
      state = await assertOutcome(page, "locked", strings.VerificationStateLocked, apiMessages.tooManyAttempts);
      assert(state.inputDisabled && state.verifyDisabled, `The input or Verify is enabled after the fourth attempt: ${JSON.stringify(state)}.`);
      assertEqual(state.activeSlot, -1, "Active slot of a disabled input");
      assertEqual(observed.verifyPosts, 4, "Verification posts");
      await assertCleanDocument(page, context, observed, culture);

      // Enabling the controls in the page does not unlock the attempt: the API refuses even the mailed code
      origin = await timeOrigin(page);
      await page.evaluate(() => {
        document.querySelector('[data-testid="code"]').disabled = false;
        document.querySelector('[data-testid="submit"]').disabled = false;
      });
      await paste(page, code);
      await waitForNewDocument(page, origin);
      await assertOutcome(page, "locked", strings.VerificationStateLocked, apiMessages.tooManyAttempts);
      assert(page.url().includes("/login/verify"), `The locked attempt left the verification page for ${page.url()}.`);
      return { verifyPosts: observed.verifyPosts };
    })
  );

  await check(`${culture}: a resend invalidates the first code, the second resend locks the input, and a new load completes with the resent code`, () =>
    withContext(async (page, context) => {
      const observed = observeVerification(page);
      const sentAfter = await startLogin(page, signedUpEmail);
      const firstCode = await readOneTimePassword(signedUpEmail, sentAfter);

      await waitForResend(page);
      const resentAfter = Date.now();
      await page.locator(testId("resend-code")).click();
      await page.waitForURL(/resent=true/);
      await page.locator(testId("code-resent")).waitFor();
      assertEqual((await page.locator(testId("code-resent")).textContent()).trim(), strings.NewCodeSent, "Resent status");
      const resentCode = await readOneTimePassword(signedUpEmail, resentAfter);
      assert(resentCode !== firstCode, "The resent code equals the first code.");

      let origin = await timeOrigin(page);
      await paste(page, firstCode);
      await waitForNewDocument(page, origin);
      await assertOutcome(page, "wrong-code", strings.VerificationStateWrongCode, apiMessages.wrongCode);

      await waitForResend(page);
      origin = await timeOrigin(page);
      await page.locator(testId("resend-code")).click();
      await waitForNewDocument(page, origin);
      const state = await assertOutcome(page, "locked", strings.VerificationStateLocked, apiMessages.tooManyAttempts);
      assert(state.inputDisabled && state.verifyDisabled, `The input or Verify is enabled after the second resend: ${JSON.stringify(state)}.`);
      await assertCleanDocument(page, context, observed, culture);

      // The refused resend locks the page it answered; the resent code is still valid on a new load, as in the React edition
      await page.goto(page.url(), { waitUntil: "load" });
      await page.locator(".one-time-password-enhanced").waitFor();
      assert(!(await controlState(page)).inputDisabled, "The input is disabled on a new load.");
      const home = page.waitForURL(`${baseUrl}${pathBase}/app`);
      await paste(page, resentCode);
      await home;
      assert(policyViolationsOf(context).length === 0, `Policy violations: ${JSON.stringify(policyViolationsOf(context))}.`);
      return { verifyPosts: observed.verifyPosts };
    })
  );

  await check(`${culture}: a start beyond three open confirmations shows the API message on the login and signup pages`, () =>
    withContext(async (page, context) => {
      // The account API refuses a start when three confirmations for the address are still open (15 minutes for login, 60
      // for signup); earlier cases may have left some open, so at most four starts reach the refusal
      const startUntilRefused = async (flow, email) => {
        for (let start = 1; start <= 4; start++) {
          await page.goto(`${baseUrl}${pathBase}/${flow}`, { waitUntil: "load" });
          await page.locator(testId("email")).fill(email);
          await page.locator(testId("submit")).click();
          const outcome = await Promise.race([
            page.waitForURL(new RegExp(`/blazor/${flow}/verify\\?`), { timeout: 15_000 }).then(() => "verify", () => "none"),
            page.locator(testId("form-error-message"), { hasText: apiMessages.tooManyStarts }).waitFor({ timeout: 15_000 }).then(() => "refused", () => "none")
          ]);
          if (outcome === "none") throw new Error(`The ${flow} start neither reached verification nor was refused.`);
          if (outcome === "refused") {
            assert(new URL(page.url()).pathname === `${pathBase}/${flow}`, `The refused start navigated to ${page.url()}.`);
            assertEqual(await page.locator(testId("form-error-title")).count(), 0, `State labels on the ${flow} page`);
            return start;
          }
        }
        throw new Error(`Four ${flow} starts were not refused.`);
      };
      const loginStarts = await startUntilRefused("login", signedUpEmail);
      const signupStarts = await startUntilRefused("signup", uniqueEmail("c", culture));
      assertEqual(signupStarts, 4, "The signup start refused for a new address");
      assert(policyViolationsOf(context).length === 0, `Policy violations: ${JSON.stringify(policyViolationsOf(context))}.`);
      return { loginStarts, signupStarts };
    })
  );

  await check(`${culture}: without scripts, signup and welcome complete through the plain input and Verify`, () =>
    withContext(
      async (page) => {
        await page.goto(`${baseUrl}${pathBase}/signup`, { waitUntil: "load" });
        await page.locator(testId("email")).fill(noScriptEmail);
        const sentAfter = Date.now();
        await page.locator(testId("submit")).click();
        await page.waitForURL(/\/blazor\/signup\/verify\?/);
        const input = page.getByLabel(strings.SignupVerificationCode, { exact: true });
        assert(await input.isVisible(), "The plain input is not visible without scripts.");
        assertEqual(await page.locator("[data-one-time-password-slots]").isVisible(), false, "Slots visible without scripts");
        assertEqual(await page.locator(".one-time-password-enhanced").count(), 0, "Enhanced controls without scripts");
        await input.fill(await readOneTimePassword(noScriptEmail, sentAfter));
        await page.locator(testId("submit")).click();
        await completeWelcomeThroughBlazor(page);
      },
      { javaScriptEnabled: false }
    )
  );

  await check(`${culture}: the countdown announces the expiry, and a rejected code then shows the expired state while the API still decides`, () =>
    withContext(async (page, context) => {
      const observed = observeVerification(page);
      const sentAfter = await startLogin(page, noScriptEmail);
      const code = await readOneTimePassword(noScriptEmail, sentAfter);
      const shortened = new URL(page.url());
      shortened.searchParams.set("sent", String(Math.floor(Date.now() / 1000) - maximumValidForSeconds + secondsBeforeExpiry));
      await page.goto(shortened.toString(), { waitUntil: "load" });
      await page.locator(".one-time-password-enhanced").waitFor();

      const expired = page.locator(testId("code-expired"));
      assertEqual(await expired.getAttribute("aria-live"), "polite", "The expiry region's aria-live");
      assertEqual((await expired.textContent()).trim(), "", "The expiry region before expiry");
      await page.waitForFunction((text) => document.querySelector('[data-testid="code-expired"]').textContent.trim() === text, strings.VerificationCodeExpired, {
        timeout: (secondsBeforeExpiry + 5) * 1000
      });
      assertEqual(await page.locator(testId("code-valid-for")).isVisible(), false, "The remaining validity visible after expiry");
      await assertCleanDocument(page, context, observed, culture);

      let origin = await timeOrigin(page);
      await paste(page, wrongCodeFor(code));
      await waitForNewDocument(page, origin);
      await assertOutcome(page, "expired", strings.VerificationStateExpired, apiMessages.wrongCode);
      await assertCleanDocument(page, context, observed, culture);

      origin = await timeOrigin(page);
      const home = page.waitForURL(`${baseUrl}${pathBase}/app`);
      await paste(page, code);
      await home;
      assertEqual(observed.verifyPosts, 2, "Verification posts");
      assert(policyViolationsOf(context).length === 0, `Policy violations: ${JSON.stringify(policyViolationsOf(context))}.`);
    })
  );

  await check(`${culture}: without scripts, a reload reveals the resend action and login completes with the resent code`, () =>
    withContext(
      async (page) => {
        await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
        await page.locator(testId("email")).fill(noScriptEmail);
        await page.locator(testId("submit")).click();
        await page.waitForURL(/\/blazor\/login\/verify\?/);
        assertEqual(await page.locator(testId("resend-code")).isVisible(), false, "Resend visible before the delay");
        await page.waitForTimeout(resendDelayMs);
        await page.reload({ waitUntil: "load" });
        const resentAfter = Date.now();
        await page.locator(testId("resend-code")).click();
        await page.waitForURL(/resent=true/);
        const input = page.getByLabel(strings.LoginVerificationCode, { exact: true });
        await input.fill((await readOneTimePassword(noScriptEmail, resentAfter)).toLowerCase());
        await page.locator(testId("submit")).click();
        await page.waitForURL(`${baseUrl}${pathBase}/app`);
      },
      { javaScriptEnabled: false }
    )
  );
}

for (const culture of cultures) await runCulture(culture);

await browser.close();

const { resultFile, passed, failures } = writeResult(
  `one-time-password-${options.browser}.json`,
  { browser: options.browser, cultures, results },
  casesPerCulture * cultures.length
);
console.log(`${passed ? "PASS" : "FAIL"} one-time-password ${options.browser}: ${resultFile}${failures.length > 0 ? ` (${failures.join("; ")})` : ""}`);
if (!passed) process.exit(1);
