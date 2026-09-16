// Antiforgery round trip through the gateway against the running stack.
//
// 1. Host to account API: the login start form, submitted in the browser, passes the host's antiforgery check and the
//    account API's own check on the token the host issued, and reaches the verification page.
// 2. Account API to host: a token and cookie issued by the React shell (served by the account API) are accepted by the
//    host's form post and again by the account API, and the host keeps the cookie it did not issue.
// 3. Cross browser: a form token issued to another browser, posted with this browser's cookie, is rejected with 400.
// 4. Missing token: a form post without its token is rejected with 400.
//
// The result records the host configuration (environment and build) the run was made against.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started.
// Run: dotnet run --project developer-cli -- blazor-harness antiforgery --browser chromium

import { baseUrl, launchBrowser, newContext, parseArguments, pathBase, probeHostConfiguration, writeResult } from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
const antiforgeryCookieName = "__Host-xsrf-token";
const formTokenSelector = 'input[name="__RequestVerificationToken"]';
const loginStartUrl = `${baseUrl}${pathBase}/login`;

const browser = await launchBrowser(options.browser);
const results = [];
const expectedCaseCount = 4;
const hostConfiguration = await probeHostConfiguration(browser, options.browser);

async function check(name, action) {
  try {
    const detail = await action();
    results.push({ name, passed: true, detail });
    console.log(`PASS ${name}${detail ? `: ${detail}` : ""}`);
  } catch (error) {
    results.push({ name, passed: false, detail: error.message });
    console.log(`FAIL ${name}: ${error.message}`);
  }
}

async function antiforgeryCookie(context) {
  const cookies = await context.cookies(baseUrl);
  return cookies.find((cookie) => cookie.name === antiforgeryCookieName)?.value;
}

async function submitLoginStart(page, email, formToken) {
  if (formToken !== undefined) {
    await page.locator(formTokenSelector).evaluate((input, value) => (input.value = value), formToken);
  }
  await page.locator('[data-testid="email"]').fill(email);
  const postResponse = page.waitForResponse((response) => response.request().method() === "POST" && response.url().startsWith(loginStartUrl));
  await page.locator('[data-testid="submit"]').click();
  return postResponse;
}

function uniqueEmail(label) {
  return `antiforgery-${label}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@example.com`;
}

await check("host-issued token accepted by host and account API", async () => {
  const context = await newContext(browser, options.browser);
  const page = await context.newPage();
  await page.goto(loginStartUrl, { waitUntil: "load" });
  const response = await submitLoginStart(page, uniqueEmail("host"));
  await page.waitForURL(/\/blazor\/login\/verify\?id=/, { timeout: 15_000 });
  await context.close();
  return `POST ${response.status()}, reached ${new URL(page.url()).pathname}`;
});

await check("React shell token accepted by host and account API", async () => {
  const context = await newContext(browser, options.browser);
  const page = await context.newPage();
  await page.goto(`${baseUrl}/login`, { waitUntil: "load" });
  const reactToken = await page.locator('meta[name="antiforgeryToken"]').getAttribute("content");
  const reactCookie = await antiforgeryCookie(context);
  if (!reactToken || !reactCookie) throw new Error("The React shell issued no antiforgery token or cookie.");

  await page.goto(loginStartUrl, { waitUntil: "load" });
  if ((await antiforgeryCookie(context)) !== reactCookie) throw new Error("The host replaced the cookie the React shell issued.");
  const response = await submitLoginStart(page, uniqueEmail("react"), reactToken);
  await page.waitForURL(/\/blazor\/login\/verify\?id=/, { timeout: 15_000 });
  await context.close();
  return `POST ${response.status()}, cookie kept, reached ${new URL(page.url()).pathname}`;
});

await check("token from another browser rejected with 400", async () => {
  const otherContext = await newContext(browser, options.browser);
  const otherPage = await otherContext.newPage();
  await otherPage.goto(loginStartUrl, { waitUntil: "load" });
  const otherToken = await otherPage.locator(formTokenSelector).getAttribute("value");
  await otherContext.close();

  const context = await newContext(browser, options.browser);
  const page = await context.newPage();
  await page.goto(loginStartUrl, { waitUntil: "load" });
  const ownToken = await page.locator(formTokenSelector).getAttribute("value");
  if (!otherToken || otherToken === ownToken) throw new Error("The two browsers did not receive distinct form tokens.");
  const response = await submitLoginStart(page, uniqueEmail("cross"), otherToken);
  const status = response.status();
  const url = page.url();
  await context.close();
  if (status !== 400) throw new Error(`Expected 400, got ${status}.`);
  if (url.includes("/login/verify")) throw new Error("The rejected post still reached the verification page.");
  return `POST ${status}`;
});

await check("form post without its token rejected with 400", async () => {
  const context = await newContext(browser, options.browser);
  const page = await context.newPage();
  await page.goto(loginStartUrl, { waitUntil: "load" });
  await page.locator(formTokenSelector).evaluate((input) => input.remove());
  const response = await submitLoginStart(page, uniqueEmail("missing"));
  const status = response.status();
  const url = page.url();
  await context.close();
  if (status !== 400) throw new Error(`Expected 400, got ${status}.`);
  if (url.includes("/login/verify")) throw new Error("The rejected post still reached the verification page.");
  return `POST ${status}`;
});

await browser.close();

const verdict = writeResult(`antiforgery-${options.browser}.json`, { browser: options.browser, culture: "en-US", ...hostConfiguration, results }, expectedCaseCount);
console.log(`Result file: ${verdict.resultFile}`);
if (!verdict.passed) process.exit(1);
