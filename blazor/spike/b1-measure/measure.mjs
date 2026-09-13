// Spike code (Blazor edition, stage B1): Playwright harness for the boot spike. Not production code.
// Run with node (never the Playwright test runner), one browser at a time:
//   node blazor/spike/b1-measure/measure.mjs policy  --browser chromium
//   node blazor/spike/b1-measure/measure.mjs timing  --browser firefox --samples 7 [--throttle]
//   node blazor/spike/b1-measure/measure.mjs payload --browser chromium --publish-dir <published host folder>
//   --locale da-DK sets the browser locale for the Blazor page (default en-US); --label <name> is appended to the result file name
// Every run signs up a fresh user through the React signup and welcome flow of the running stack, writes a JSON
// result file and prints a summary table.

import { mkdirSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";

const repositoryRoot = path.resolve(import.meta.dirname, "../../..");
const playwright = createRequire(path.join(repositoryRoot, "application/"))("playwright");
const basePort = readFileSync(path.join(repositoryRoot, ".workspace/port.txt"), "utf8").trim();
const baseUrl = `https://app.dev.localhost:${basePort}`;
const verificationCode = "UNLOCK";
const interactiveTimeoutMs = 60_000;

// Chromium only, through CDP. Stated in the result file; not a DevTools preset.
const throttleProfile = { name: "4G-like", latencyMs: 60, downloadKbps: 9_000, uploadKbps: 1_500 };

const options = parseArguments(process.argv.slice(2));
const browserType = playwright[options.browser];
const resultsFolder = options.out ?? path.join(repositoryRoot, ".workspace/experiment-06-blazor/b1-results");
mkdirSync(resultsFolder, { recursive: true });

// Chromium keeps nothing in its HTTP cache for a response with a certificate error, so warm loads refetch every asset
// under ignoreHTTPSErrors alone. --chromium-home points HOME at a folder whose .pki/nssdb trusts the gateway certificate.
const browser = await browserType.launch(
  options.chromiumHome && options.browser === "chromium" ? { env: { ...process.env, HOME: options.chromiumHome } } : {}
);
const result = {
  mode: options.mode,
  label: options.label ?? null,
  browser: options.browser,
  browserVersion: browser.version(),
  locale: options.locale,
  playwrightVersion: createRequire(path.join(repositoryRoot, "application/"))("playwright/package.json").version,
  baseUrl,
  chromiumTrustedCertificateHome: options.chromiumHome ?? null,
  startedAt: new Date().toISOString()
};

try {
  const account = await signUpThroughReact();
  result.account = { email: account.email };

  if (options.mode === "policy") result.policy = await runPolicyCases(account);
  else if (options.mode === "timing") result.timing = await runTiming(account);
  else if (options.mode === "payload") result.payload = await runPayload(account);
  else throw new Error(`Unknown mode '${options.mode}'.`);
} finally {
  await browser.close();
}

result.finishedAt = new Date().toISOString();
const resultFile = path.join(
  resultsFolder,
  `${options.mode}-${options.browser}${options.label ? `-${options.label}` : ""}${options.throttle ? "-throttled" : ""}.json`
);
writeFileSync(resultFile, JSON.stringify(result, null, 2));
printSummary(result);
console.log(`\nResult file: ${resultFile}`);

function parseArguments(argumentList) {
  const parsed = { mode: argumentList[0], browser: "chromium", samples: 5, throttle: false, locale: "en-US" };
  for (let index = 1; index < argumentList.length; index++) {
    const name = argumentList[index].replace(/^--/, "");
    if (name === "throttle") parsed.throttle = true;
    else parsed[name.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase())] = argumentList[++index];
  }
  parsed.samples = Number(parsed.samples);
  return parsed;
}

// The signup flow always runs in en-US because it finds React controls by their English names; --locale applies to the
// contexts that load the Blazor page
async function newContext(storageState, locale = options.locale) {
  // An explicit locale: headless Chromium in the container otherwise reports "en-US@posix", which the .NET runtime
  // rejects as a culture name (Argument_CultureNotSupported) and aborts the WebAssembly start
  const context = await browser.newContext({ ignoreHTTPSErrors: true, locale, storageState });
  await context.addInitScript(() => {
    // Counts DOM overflowchange events so the overflow check can tell "not dispatched" from "not delivered to .NET"
    window.__b1DomOverflowChangeCount = 0;
    document.addEventListener("overflowchange", () => window.__b1DomOverflowChangeCount++, true);
    window.__b1Violations = [];
    document.addEventListener("securitypolicyviolation", (event) => {
      window.__b1Violations.push({
        effectiveDirective: event.effectiveDirective,
        violatedDirective: event.violatedDirective,
        blockedURI: event.blockedURI,
        sample: event.sample,
        sourceFile: event.sourceFile,
        lineNumber: event.lineNumber,
        disposition: event.disposition
      });
    });
  });
  return context;
}

async function typeOneTimeCode(page) {
  const input = page.locator('input[autocomplete="one-time-code"]');
  await input.waitFor();
  await page.waitForFunction(() => document.activeElement?.getAttribute("autocomplete") === "one-time-code");
  await page.evaluate((code) => {
    const element = document.activeElement;
    Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value").set.call(element, code);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  }, verificationCode);
}

// Mirrors completeSignupFlow in application/shared-webapp/tests/e2e/utils/test-data.ts
async function signUpThroughReact() {
  const email = `b1-${options.browser}-${Date.now()}@example.com`;
  const context = await newContext(undefined, "en-US");
  const page = await context.newPage();
  // Right after an AppHost restart the React bundle can lag behind the gateway, so the signup page gets a few chances
  for (let attempt = 1; ; attempt++) {
    await page.goto(`${baseUrl}/signup`);
    const ready = await page.getByRole("textbox", { name: "Email" }).waitFor({ timeout: 30_000 }).then(() => true, () => false);
    if (ready) break;
    if (attempt === 6) throw new Error("The React signup page did not render.");
  }
  await page.getByRole("textbox", { name: "Email" }).fill(email);
  await page.getByRole("button", { name: "Sign up with email" }).click();
  await page.waitForURL(`${baseUrl}/signup/verify`);
  await typeOneTimeCode(page);
  await page.waitForURL(/\/welcome/);
  await page.getByRole("textbox", { name: "Account name" }).fill("B1 Spike");
  await page.getByRole("button", { name: "Continue" }).click();
  await page.getByRole("heading", { name: "Let's set up your profile" }).waitFor();
  await page.getByRole("textbox", { name: "First name" }).fill("Boot");
  await page.getByRole("textbox", { name: "Last name" }).fill("Spike");
  await page.getByRole("button", { name: "Continue" }).click();
  await page.waitForURL(`${baseUrl}/dashboard`);
  const storageState = await context.storageState();
  await context.close();
  return { email, storageState };
}

function observe(page) {
  const observations = { consoleMessages: [], pageErrors: [], failedRequests: [], errorResponses: [], scriptRequests: [] };
  page.on("console", (message) => {
    if (message.type() === "error" || message.type() === "warning") {
      observations.consoleMessages.push({ type: message.type(), text: message.text().slice(0, 600) });
    }
  });
  page.on("pageerror", (error) => observations.pageErrors.push(String(error.message).slice(0, 600)));
  page.on("requestfailed", (request) =>
    observations.failedRequests.push({ url: request.url(), failure: request.failure()?.errorText })
  );
  page.on("response", (response) => {
    if (response.status() >= 400) observations.errorResponses.push({ url: response.url(), status: response.status() });
    if (response.request().resourceType() === "script") {
      observations.scriptRequests.push({ url: response.url(), status: response.status() });
    }
  });
  return observations;
}

async function loadPage(context, url, { clickCounter = true } = {}) {
  const page = await context.newPage();
  const observations = observe(page);
  const response = await page.goto(url, { waitUntil: "load" });
  let interactive = true;
  await page
    .waitForFunction(() => typeof window.__b1InteractiveAt === "number", null, { timeout: interactiveTimeoutMs })
    .catch(() => (interactive = false));

  let counter = null;
  if (interactive && clickCounter) {
    const value = page.locator('[data-testid="counter-value"]');
    const before = await value.textContent();
    await page.locator('[data-testid="counter-button"]').click();
    const incremented = await page
      .waitForFunction((previous) => document.querySelector('[data-testid="counter-value"]')?.textContent !== previous, before, {
        timeout: 5_000
      })
      .then(() => true)
      .catch(() => false);
    counter = { before, after: await value.textContent(), incremented };
  }

  // Lets late violation reports and console messages arrive before reading them
  await page.waitForTimeout(1_000);
  const state = await page.evaluate(() => ({
    finalUrl: location.href,
    documentBaseUri: document.baseURI,
    violations: window.__b1Violations,
    markers: { ...document.documentElement.dataset },
    interactiveAt: window.__b1InteractiveAt ?? null,
    titleElementCount: document.querySelectorAll("title").length,
    chartSvgRendered: document.querySelector('[data-testid="chart"] svg') !== null,
    signedInText: document.querySelector('[data-testid="signed-in-email"]')?.textContent ?? null,
    documentLang: document.documentElement.lang,
    navigatorLanguage: navigator.language,
    domOverflowChangeCount: window.__b1DomOverflowChangeCount,
    overflowRaisedCount: document.querySelector('[data-testid="overflow-raised-count"]')?.textContent ?? null,
    overflowItemCount: document.querySelector('[data-testid="overflow-item-count"]')?.textContent ?? null
  }));
  const pageResult = {
    url,
    status: response?.status() ?? null,
    contentSecurityPolicy: response?.headers()["content-security-policy"] ?? null,
    interactive,
    counter,
    ...state,
    ...observations
  };
  await page.close();
  return pageResult;
}

async function runPolicyCases(account) {
  const cases = {};
  const context = await newContext(account.storageState);
  const caseUrls = {
    proposed: `${baseUrl}/blazor/`,
    proposedApexChartsNonce: `${baseUrl}/blazor/?b1-apex-nonce=1`,
    proposedWithHooks: `${baseUrl}/blazor/?b1-hooks=1`,
    noWasmEval: `${baseUrl}/blazor/?csp-variant=no-wasm-eval`,
    noStrictDynamic: `${baseUrl}/blazor/?csp-variant=no-strict-dynamic`,
    pathBaseWithoutSlash: `${baseUrl}/blazor`,
    deeperRoute: `${baseUrl}/blazor/b1/deeper/route`,
    overflowEvent: `${baseUrl}/blazor/b1/overflow?b1-apex-nonce=1`
  };
  for (const [name, url] of Object.entries(caseUrls)) {
    cases[name] = await loadPage(context, url, { clickCounter: name !== "overflowEvent" });
  }
  await context.close();

  cases.loginReturnPath = await runLoginReturnPath(account);
  return cases;
}

// Anonymous request to /blazor/, redirected to the React login; records where the login lands afterwards
async function runLoginReturnPath(account) {
  const context = await newContext(undefined, "en-US");
  const page = await context.newPage();
  await page.goto(`${baseUrl}/blazor/`);
  await page.getByRole("textbox", { name: "Email" }).waitFor();
  const loginUrl = page.url();
  await page.getByRole("textbox", { name: "Email" }).fill(account.email);
  await page.getByRole("button", { name: "Log in with email" }).click();
  await page.waitForURL(/\/login\/verify/);
  const verifyUrl = page.url();
  await typeOneTimeCode(page);
  await page.waitForURL((url) => !url.pathname.startsWith("/login"), { timeout: 30_000 }).catch(() => {});
  await page.waitForLoadState("load");
  const landedUrl = page.url();
  const interactive = await page
    .waitForFunction(() => typeof window.__b1InteractiveAt === "number", null, { timeout: 30_000 })
    .then(() => true)
    .catch(() => false);
  await context.close();
  return { loginUrl, verifyUrl, landedUrl, landedOnBlazor: new URL(landedUrl).pathname.startsWith("/blazor/"), interactive };
}

async function applyThrottling(context, page) {
  if (!options.throttle) return null;
  if (options.browser !== "chromium") throw new Error("Throttling uses CDP and is Chromium only.");
  const session = await context.newCDPSession(page);
  await session.send("Network.enable");
  await session.send("Network.emulateNetworkConditions", {
    offline: false,
    latency: throttleProfile.latencyMs,
    downloadThroughput: (throttleProfile.downloadKbps * 1000) / 8,
    uploadThroughput: (throttleProfile.uploadKbps * 1000) / 8
  });
  return throttleProfile;
}

async function readTimings(page) {
  await page.waitForFunction(() => typeof window.__b1InteractiveAt === "number", null, {
    timeout: options.throttle ? interactiveTimeoutMs * 3 : interactiveTimeoutMs
  });
  await page.waitForFunction(() => performance.getEntriesByType("navigation")[0]?.loadEventEnd > 0);
  return page.evaluate(() => {
    const navigation = performance.getEntriesByType("navigation")[0];
    const resources = performance.getEntriesByType("resource");
    return {
      timeToInteractiveMs: window.__b1InteractiveAt,
      domContentLoadedMs: navigation.domContentLoadedEventEnd,
      loadMs: navigation.loadEventEnd,
      resourceCount: resources.length,
      resourceTransferBytes: resources.reduce((sum, entry) => sum + (entry.transferSize ?? 0), 0) + (navigation.transferSize ?? 0)
    };
  });
}

async function runTiming(account) {
  // One discarded load warms the server (JIT, static asset endpoint caches) so samples measure the client
  const warmUpContext = await newContext(account.storageState);
  await loadPage(warmUpContext, `${baseUrl}/blazor/`, { clickCounter: false });
  await warmUpContext.close();

  const cold = [];
  const warm = [];
  let profile = null;
  for (let sample = 0; sample < options.samples; sample++) {
    const context = await newContext(account.storageState);
    const page = await context.newPage();
    profile = await applyThrottling(context, page);
    await page.goto(`${baseUrl}/blazor/`, { waitUntil: "load" });
    cold.push(await readTimings(page));
    await page.reload({ waitUntil: "load" });
    warm.push(await readTimings(page));
    await context.close();
  }
  return { throttleProfile: profile, samples: options.samples, cold, warm, coldSummary: summarize(cold), warmSummary: summarize(warm) };
}

function summarize(samples) {
  const statistics = (values) => {
    const sorted = [...values].sort((left, right) => left - right);
    const middle = Math.floor(sorted.length / 2);
    const median = sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    return { median: Math.round(median), min: Math.round(sorted[0]), max: Math.round(sorted.at(-1)) };
  };
  return {
    timeToInteractiveMs: statistics(samples.map((sample) => sample.timeToInteractiveMs)),
    domContentLoadedMs: statistics(samples.map((sample) => sample.domContentLoadedMs)),
    loadMs: statistics(samples.map((sample) => sample.loadMs)),
    resourceTransferBytes: statistics(samples.map((sample) => sample.resourceTransferBytes))
  };
}

function fileSize(filePath) {
  try {
    return statSync(filePath).size;
  } catch {
    return null;
  }
}

// Maps each route of the published endpoint manifest to the files MapStaticAssets serves for it: the identity file and
// the brotli variant, so on-disk sizes cover exactly the files behind the fetched URLs (fingerprinted routes included)
function readEndpointFiles(publishDir) {
  const manifestPath = path.join(publishDir, "Blazor.Host.staticwebassets.endpoints.json");
  const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
  const files = new Map();
  for (const endpoint of manifest.Endpoints) {
    const entry = files.get(endpoint.Route) ?? { identity: null, brotli: null };
    const encoding = endpoint.Selectors.find((selector) => selector.Name === "Content-Encoding")?.Value;
    if (!encoding) entry.identity = endpoint.AssetFile;
    if (encoding === "br") entry.brotli = endpoint.AssetFile;
    files.set(endpoint.Route, entry);
  }
  return files;
}

async function runPayload(account) {
  const endpointFiles = options.publishDir ? readEndpointFiles(options.publishDir) : null;
  const context = await newContext(account.storageState);
  const page = await context.newPage();
  const requests = [];
  page.on("requestfinished", (request) => requests.push(request));
  const response = await page.goto(`${baseUrl}/blazor/`, { waitUntil: "load" });
  const html = await response.text();
  await page.waitForFunction(() => typeof window.__b1InteractiveAt === "number", null, { timeout: interactiveTimeoutMs });
  await page.waitForLoadState("networkidle");
  const violations = await page.evaluate(() => window.__b1Violations);
  const languages = await page.evaluate(() => ({ documentLang: document.documentElement.lang, navigatorLanguage: navigator.language }));

  const rows = [];
  for (const request of requests) {
    const requestResponse = await request.response();
    const sizes = await request.sizes();
    const body = await requestResponse.body().catch(() => null);
    const url = new URL(request.url());
    const row = {
      url: request.url(),
      resourceType: request.resourceType(),
      status: requestResponse.status(),
      contentEncoding: requestResponse.headers()["content-encoding"] ?? null,
      cacheControl: requestResponse.headers()["cache-control"] ?? null,
      transferBodyBytes: sizes.responseBodySize,
      transferHeaderBytes: sizes.responseHeadersSize,
      decodedBodyBytes: body?.length ?? null,
      onDiskBytes: null,
      onDiskBrotliBytes: null
    };
    const endpoint = url.pathname.startsWith("/blazor/") ? endpointFiles?.get(decodeURIComponent(url.pathname.slice("/blazor/".length))) : undefined;
    if (endpoint) {
      row.onDiskFile = endpoint.identity;
      row.onDiskBytes = fileSize(path.join(options.publishDir, "wwwroot", endpoint.identity));
      row.onDiskBrotliBytes = endpoint.brotli ? fileSize(path.join(options.publishDir, "wwwroot", endpoint.brotli)) : null;
    }
    rows.push(row);
  }
  await context.close();

  const sum = (selector) => rows.reduce((total, row) => total + (selector(row) ?? 0), 0);
  const assetRows = rows.filter((row) => row.onDiskBytes !== null);
  return {
    publishDir: options.publishDir ?? null,
    requestCount: rows.length,
    transferBodyBytes: sum((row) => row.transferBodyBytes),
    transferHeaderBytes: sum((row) => row.transferHeaderBytes),
    decodedBodyBytes: sum((row) => row.decodedBodyBytes),
    staticAssetRequestCount: assetRows.length,
    staticAssetsMissingBrotliOnDisk: assetRows.filter((row) => row.onDiskBrotliBytes === null).map((row) => row.url),
    transferBodyBytesOfStaticAssets: assetRows.reduce((total, row) => total + (row.transferBodyBytes ?? 0), 0),
    notServedAsBrotli: rows.filter((row) => row.contentEncoding !== "br").map((row) => `${row.contentEncoding} ${row.url}`),
    onDiskBrotliOrIdentityBytes: assetRows.reduce((total, row) => total + (row.onDiskBrotliBytes ?? row.onDiskBytes ?? 0), 0),
    onDiskBrotliBytes: sum((row) => row.onDiskBrotliBytes),
    onDiskUncompressedBytes: sum((row) => row.onDiskBytes),
    ...languages,
    icuFiles: rows.filter((row) => /icudt/i.test(row.url)).map((row) => row.url),
    importMapHasHotReloadModule: /HotReload/.test(html.match(/<script type="importmap"[^>]*>([\s\S]*?)<\/script>/)?.[1] ?? ""),
    webInitializersComment: html.match(/<!--Blazor-Web-Initializers:([^-]+)-->/)
      ? Buffer.from(html.match(/<!--Blazor-Web-Initializers:([^-]+)-->/)[1], "base64").toString("utf8")
      : null,
    violations,
    rows
  };
}

function printSummary(summary) {
  console.log(`${summary.mode} | ${summary.browser} ${summary.browserVersion}`);
  if (summary.policy) {
    const table = Object.entries(summary.policy)
      .filter(([name]) => name !== "loginReturnPath")
      .map(([name, value]) => ({
        case: name,
        status: value.status,
        interactive: value.interactive,
        counter: value.counter?.incremented ?? null,
        chart: value.chartSvgRendered,
        violations: value.violations.map((violation) => `${violation.effectiveDirective}:${violation.blockedURI}`).join(" ; "),
        markers: Object.keys(value.markers).join(","),
        overflow: value.overflowRaisedCount === null ? null : `dom ${value.domOverflowChangeCount}, .NET ${value.overflowRaisedCount}, items ${value.overflowItemCount}`,
        consoleErrors: value.consoleMessages.filter((message) => message.type === "error").length,
        consoleWarnings: value.consoleMessages.filter((message) => message.type === "warning").length,
        pageErrors: value.pageErrors.length,
        httpErrors: value.errorResponses.length
      }));
    console.table(table);
    console.log("loginReturnPath", summary.policy.loginReturnPath);
  }
  if (summary.timing) {
    console.table({ cold: flatten(summary.timing.coldSummary), warm: flatten(summary.timing.warmSummary) });
  }
  if (summary.payload) {
    const { rows, violations, ...totals } = summary.payload;
    console.table(
      rows.map((row) => ({
        path: new URL(row.url).pathname.slice(0, 70),
        status: row.status,
        encoding: row.contentEncoding,
        transfer: row.transferBodyBytes,
        decoded: row.decodedBodyBytes,
        diskBr: row.onDiskBrotliBytes
      }))
    );
    console.log({ ...totals, violationCount: violations.length });
  }
}

function flatten(summary) {
  return Object.fromEntries(
    Object.entries(summary).map(([metric, value]) => [metric, `${value.median} (${value.min}-${value.max})`])
  );
}
