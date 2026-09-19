// Measures the nine static server-rendered public pages on the trimmed Release publish through the gateway, and checks them
// against the public-page budget.
//
// Prerequisites are the same as for trimmed-smoke.mjs: the stack started with start-stack --without-blazor-host,
// and blazor-publish then blazor-serve run through the developer CLI. Run one browser at a time:
//   dotnet run --project developer-cli -- blazor-harness public-pages --browser chromium
//   dotnet run --project developer-cli -- blazor-harness public-pages --browser chromium --profile throttled --check-budget
// Options: --profile unthrottled|throttled|all (default all), --samples 7, --observe-ms 3000, --label baseline.
//
// Conditions: each sample opens a fresh browser context and loads the page (cold), waits a fixed observation interval
// after the load event so background requests are captured, then reloads it in the same context (warm) and waits again.
// One warm-up sample per page is discarded. Medians, minimums and maximums are reported per page, profile and load. The
// verification pages are measured with state from a real login and signup start, so they render instead of redirecting.
// Throttling (60 ms latency, 9,000 kbps down, 1,500 kbps up, no CPU throttling) uses the Chromium DevTools protocol;
// Firefox and WebKit have no equivalent in the browser automation library, so their throttled cells are unavailable.
//
// Per load: fresh network requests (a 304 revalidation counts, a response from the HTTP cache does not), transfer bytes
// (resource timing transferSize of the fresh requests, headers included), body bytes (encoded bodies of fresh 200
// responses), Brotli bytes on disk of the static files fetched with a 200, uncompressed bytes (decoded bodies of every 200
// response the page used, from the network or the cache), cached responses, WebAssembly runtime requests (must be zero),
// first contentful paint and the load event, both in milliseconds from navigation start.
//
// Two budgets: the public-page budget for the landing, login, signup and verification pages and the legal index, and a higher
// one for the three legal documents, whose payload is the legal text itself. Both are frozen from medians; see the constants.
//
// Verdict: a case per profile and page (status 200 in Production on every sample, landed on the page itself rather than a
// redirect, no WebAssembly runtime request and no page error on either load, at least one static asset served from this
// publish), a case per profile for the enhanced navigation from landing to terms (same document, landed on terms, no runtime
// request), and with --check-budget a transfer case and a first contentful paint case per page. The result file records the
// commit, the publish identity and the runner; a run that executed fewer cases than expected fails. The published-security
// job in .github/workflows/blazor.yml runs the budget check on Chromium for every pull request, push, nightly run and
// dispatch, and verify-results fails the job when the result is missing, from another commit, incomplete or failed.

import { appendFileSync, mkdirSync, statSync } from "node:fs";
import path from "node:path";
import {
  baseUrl,
  componentLibraryRequestPattern,
  hostConfiguration,
  isProductionPolicy,
  launchBrowser,
  newContext,
  parseArguments,
  pathBase,
  playwrightVersion,
  publishFolder,
  publishIdentity,
  readPublishedEndpoints,
  redact,
  resultsFolder,
  runEnvironment,
  runtimeRequestPattern,
  signUpThroughBlazor,
  startLoginThroughBlazor,
  startSignupThroughBlazor,
  writeResult
} from "./support/stack.mjs";

// The public-page budget, frozen from the baseline medians: Chromium, throttled profile, cold load, applied to every public
// page. A regression is fixed or the budget is re-decided in review; never raise these numbers to make a run pass.
// Baseline on the publish of 2ee942ab6 plus the change that takes the component library's browser bundle out of the host's
// JS module manifest, 2026-09-19, Chromium 149.0.7827.0, Playwright 1.61.0, 7 samples after one warm-up, 3,000 ms
// observation: the six pages this budget holds measured 112,514 (landing), 112,713 (signup), 112,745 (login), 114,543
// (signup-verify), 114,580 (login-verify) and 115,478 (legal index) transfer bytes, each varying by under 100 bytes between
// samples, and the slowest first contentful paint median was 260 ms (landing, legal index). That is about 84,000 bytes less
// per page than the 196,710 to 199,674 of the same pages on 2026-09-18, which was the shared bundle. 125,000 bytes leaves
// about 9.5 KB above the heaviest of the six for content and stylesheet growth; 300 ms covers the 244 to 268 ms sample
// range plus runner variance, since the emulated latency dominates the paint time on this profile and did not move when the
// transfer fell. One signup-verify sample paints at 3,816 ms, a runner stall that a median over 7 samples absorbs.
const publicPageBudget = { transferBytes: 125_000, firstContentfulPaintMs: 300 };

// The legal documents keep their own budget: the page is the legal text, and the rendered document alone is 28,516 (terms),
// 31,030 (privacy) and 33,920 (dpa) transfer bytes, which is 10 to 16 KB more than any other public page's document. That
// difference survived the cut above, so the three documents still do not fit the number the other six are held to, and the
// owner's expectation of 2026-09-18 that this constant could be removed does not hold.
// Baseline under the same conditions as the budget above: terms 123,000 bytes, privacy 125,564 and dpa 128,404, each
// varying by under 100 bytes between samples, and the slowest first contentful paint median was 264 ms (terms, dpa).
// 140,000 bytes leaves about 11.5 KB above the heaviest document for the text to grow. A regression is fixed or the budget
// is re-decided in review; never raise these numbers to make a run pass.
const legalDocumentBudget = { transferBytes: 140_000, firstContentfulPaintMs: 300 };
const legalDocumentPages = new Set(["terms", "privacy", "dpa"]);
const budgetOf = (pageName) => (legalDocumentPages.has(pageName) ? legalDocumentBudget : publicPageBudget);

const numericFields = ["requestCount", "cachedCount", "transferBytes", "bodyBytes", "brotliDiskBytes", "uncompressedBytes", "firstContentfulPaintMs", "loadMs", "lastRequestStartAfterLoadMs", "violations"];

const throttledProfile = { latency: 60, downloadKilobitsPerSecond: 9_000, uploadKilobitsPerSecond: 1_500 };
const options = parseArguments(process.argv.slice(2), { browser: "chromium", profile: "all", samples: "7", "observe-ms": "3000", label: "baseline" });
const sampleCount = Number(options.samples);
const observeMs = Number(options["observe-ms"]);
const warmUpSamples = 1;
const checkBudgetRequested = options["check-budget"] === true;
if (!(Number.isInteger(sampleCount) && sampleCount > 0)) throw new Error(`--samples must be a positive whole number, not '${options.samples}'.`);

mkdirSync(resultsFolder, { recursive: true });
const endpointsByRoute = indexPublishedEndpoints();
const browser = await launchBrowser(options.browser);
if (!["unthrottled", "throttled", "all"].includes(options.profile)) throw new Error(`Unknown profile '${options.profile}'. Use unthrottled, throttled or all.`);
const profiles = (options.profile === "all" ? ["unthrottled", "throttled"] : [options.profile]).map((name) => ({
  name,
  available: name === "unthrottled" || options.browser === "chromium"
}));

const result = {
  label: options.label,
  browser: options.browser,
  browserVersion: browser.version(),
  playwrightVersion,
  baseUrl,
  artifact: publishIdentity(),
  runner: runEnvironment(),
  conditions: { samples: sampleCount, warmUpSamplesDiscarded: warmUpSamples, observeMsAfterLoad: observeMs, throttledProfile, cpuThrottling: "none", headless: true },
  startedAt: new Date().toISOString(),
  profiles: {}
};
const failures = [];
const cases = [];
const pageNames = ["landing", "login", "login-verify", "signup", "signup-verify", "legal", "terms", "privacy", "dpa"];
const availableProfileCount = profiles.filter((profile) => profile.available).length;
const expectedCaseCount = availableProfileCount * (pageNames.length + 1) + (checkBudgetRequested ? pageNames.length * 2 : 0);

try {
  const account = await signUpThroughBlazor(browser, options.browser, `pages-${options.browser}-${Date.now()}@example.com`);
  const loginVerifyUrl = await startLoginThroughBlazor(browser, options.browser, account.email);
  const signupVerifyUrl = await startSignupThroughBlazor(browser, options.browser, `pages-signup-${options.browser}-${Date.now()}@example.com`);
  const pages = [
    { name: "landing", url: `${baseUrl}${pathBase}/` },
    { name: "login", url: `${baseUrl}${pathBase}/login` },
    { name: "login-verify", url: loginVerifyUrl },
    { name: "signup", url: `${baseUrl}${pathBase}/signup` },
    { name: "signup-verify", url: signupVerifyUrl },
    { name: "legal", url: `${baseUrl}${pathBase}/legal` },
    { name: "terms", url: `${baseUrl}${pathBase}/legal/terms` },
    { name: "privacy", url: `${baseUrl}${pathBase}/legal/privacy` },
    { name: "dpa", url: `${baseUrl}${pathBase}/legal/dpa` }
  ];

  for (const profile of profiles) {
    if (!profile.available) {
      result.profiles[profile.name] = { available: false, reason: `no network throttling for ${options.browser} in the browser automation library` };
      continue;
    }
    const pageResults = {};
    for (const pageDefinition of pages) {
      const samples = [];
      for (let sample = 0; sample < warmUpSamples + sampleCount; sample++) {
        const measured = await measurePage(pageDefinition, profile.name);
        if (sample >= warmUpSamples) samples.push(measured);
      }
      pageResults[pageDefinition.name] = summarize(pageDefinition, samples);
      checkPage(pageDefinition.name, profile.name, pageResults[pageDefinition.name]);
    }
    const navigationSamples = [];
    for (let sample = 0; sample < warmUpSamples + sampleCount; sample++) {
      const measured = await measureEnhancedNavigation(profile.name);
      if (sample >= warmUpSamples) navigationSamples.push(measured);
    }
    const navigation = summarizeNavigation(navigationSamples);
    checkNavigation(profile.name, navigation);
    result.profiles[profile.name] = { available: true, pages: pageResults, enhancedNavigationLandingToTerms: navigation };
    result.hostEnvironment ??= pageResults.landing.document.hostEnvironment;
    result.buildConfiguration ??= pageResults.landing.document.buildConfiguration;
  }

  if (checkBudgetRequested) checkBudget();
} catch (error) {
  failures.push(redact(String(error.stack ?? error)).slice(0, 1_000));
} finally {
  await browser.close();
}

result.cases = cases;
result.failures = failures;
result.finishedAt = new Date().toISOString();
const verdict = writeResult(`public-pages-${options.label}-${options.browser}.json`, result, expectedCaseCount);
printTable();
writeJobSummary(verdict);
console.log(`${options.browser} ${result.browserVersion} (${options.label}): ${verdict.passed ? "passed" : `failed: ${redact(verdict.failures.join(" ; "))}`}`);
console.log(`Result file: ${verdict.resultFile}`);
process.exitCode = verdict.passed ? 0 : 1;

function recordCase(name, problems) {
  cases.push({ name, passed: problems.length === 0, problems });
  failures.push(...problems.map((problem) => `${name}: ${problem}`));
}

function indexPublishedEndpoints() {
  const index = new Map();
  for (const endpoint of readPublishedEndpoints()) {
    const isBrotli = endpoint.Selectors.some((selector) => selector.Name === "Content-Encoding" && selector.Value === "br");
    if (!isBrotli) continue;
    index.set(`${pathBase}/${endpoint.Route}`, statSync(path.join(publishFolder, "wwwroot", endpoint.AssetFile)).size);
  }
  return index;
}

async function openPage(context, profileName) {
  const page = await context.newPage();
  if (profileName === "throttled") {
    const session = await context.newCDPSession(page);
    await session.send("Network.enable");
    await session.send("Network.emulateNetworkConditions", {
      offline: false,
      latency: throttledProfile.latency,
      downloadThroughput: (throttledProfile.downloadKilobitsPerSecond * 1_000) / 8,
      uploadThroughput: (throttledProfile.uploadKilobitsPerSecond * 1_000) / 8
    });
  }
  return page;
}

function recordRequests(page) {
  const log = { requests: [], websockets: [], pageErrors: [] };
  page.on("websocket", (socket) => log.websockets.push(socket.url()));
  page.on("pageerror", (error) => log.pageErrors.push(String(error.message).slice(0, 400)));
  page.on("requestfinished", (request) => log.requests.push(readRequest(request)));
  page.on("requestfailed", (request) => log.requests.push(Promise.resolve({ url: request.url(), failed: request.failure()?.errorText ?? "failed" })));
  return log;
}

async function readRequest(request) {
  const response = await request.response();
  const sizes = await request.sizes().catch(() => null);
  const headers = response ? await response.allHeaders().catch(() => ({})) : {};
  const body = response && response.status() === 200 ? await response.body().catch(() => null) : null;
  return {
    url: request.url(),
    type: request.resourceType(),
    status: response?.status() ?? null,
    encoding: headers["content-encoding"] ?? null,
    cacheControl: headers["cache-control"] ?? null,
    headersBytes: sizes?.responseHeadersSize ?? null,
    bodyBytes: sizes?.responseBodySize ?? null,
    decodedBytes: body?.length ?? null
  };
}

async function readTimeline(page) {
  return page.evaluate(() => {
    const navigation = performance.getEntriesByType("navigation")[0];
    const paint = performance.getEntriesByName("first-contentful-paint")[0];
    const entries = [navigation, ...performance.getEntriesByType("resource")].filter(Boolean);
    return {
      firstContentfulPaintMs: paint ? Math.round(paint.startTime) : null,
      loadMs: navigation ? Math.round(navigation.loadEventStart) : null,
      loadEventEnd: navigation?.loadEventEnd ?? null,
      entries: entries.map((entry) => ({
        url: entry.name,
        startTime: entry.startTime,
        transferSize: entry.transferSize,
        encodedBodySize: entry.encodedBodySize,
        decodedBodySize: entry.decodedBodySize
      })),
      violations: window.__policyViolations,
      sameDocumentMarker: window.__sameDocument === true
    };
  });
}

// Sizes come from the resource timing entries, which all three browsers fill for same-origin requests. A response counts
// as served from the HTTP cache when its entry reports no transfer; a 304 revalidation is a fresh network request. Without
// an entry the browser automation sizes are used instead.
function isCached(request, timelineEntry) {
  if (request.failed || request.status !== 200) return false;
  if (timelineEntry) return timelineEntry.transferSize === 0;
  return (request.headersBytes ?? 0) + (request.bodyBytes ?? 0) <= 0;
}

function transferBytes(request, timelineEntry) {
  // Under network emulation Chromium reports the enhanced navigation fetch as aborted although its body arrived
  if (request.failed) return timelineEntry?.transferSize ?? 0;
  // Firefox includes the cached body in the transferSize of a 304 revalidation, which carries no body on the wire
  if (timelineEntry && request.status === 304 && timelineEntry.transferSize >= timelineEntry.encodedBodySize)
    return timelineEntry.transferSize - timelineEntry.encodedBodySize;
  if (timelineEntry) return timelineEntry.transferSize;
  return (request.headersBytes ?? 0) + (request.bodyBytes ?? 0);
}

function bodyBytes(request, timelineEntry) {
  if (request.status !== 200) return 0;
  return timelineEntry ? timelineEntry.encodedBodySize : (request.bodyBytes ?? 0);
}

async function collect(log, timeline, loadStartedAt) {
  const requests = await Promise.all(log.requests);
  const entriesByUrl = new Map(timeline.entries.map((entry) => [entry.url, entry]));
  const fresh = [];
  const cached = [];
  for (const request of requests) (isCached(request, entriesByUrl.get(request.url)) ? cached : fresh).push(request);
  const runtimeFromTimeline = timeline.entries.map((entry) => entry.url).filter((url) => runtimeRequestPattern.test(new URL(url).pathname));
  const runtimeFromNetwork = requests.map((request) => request.url).filter((url) => runtimeRequestPattern.test(new URL(url).pathname));
  const libraryUrls = [...timeline.entries.map((entry) => entry.url), ...requests.map((request) => request.url)];
  const lastStart = Math.max(...timeline.entries.map((entry) => entry.startTime));
  return {
    requestCount: fresh.length,
    cachedCount: cached.length,
    transferBytes: sum(fresh, (request) => transferBytes(request, entriesByUrl.get(request.url))),
    bodyBytes: sum(fresh, (request) => bodyBytes(request, entriesByUrl.get(request.url))),
    brotliDiskBytes: sum(fresh, (request) => (request.status === 200 ? (endpointsByRoute.get(new URL(request.url).pathname) ?? 0) : 0)),
    freshWithoutBrotliFile: fresh.filter((request) => !endpointsByRoute.has(new URL(request.url).pathname)).map((request) => new URL(request.url).pathname),
    publishedAssetRequests: fresh.filter((request) => request.status === 200 && endpointsByRoute.has(new URL(request.url).pathname)).length,
    uncompressedBytes: sum(requests, (request) => request.decodedBytes ?? 0),
    runtimeRequests: [...new Set([...runtimeFromTimeline, ...runtimeFromNetwork])],
    componentLibraryRequests: [...new Set(libraryUrls.filter((url) => componentLibraryRequestPattern.test(new URL(url).pathname)))],
    firstContentfulPaintMs: timeline.firstContentfulPaintMs,
    loadMs: timeline.loadMs,
    lastRequestStartAfterLoadMs: timeline.loadEventEnd === null ? null : Math.round(lastStart - timeline.loadEventEnd),
    observeMs,
    websockets: log.websockets,
    pageErrors: log.pageErrors,
    violations: timeline.violations.length,
    requests: requests.map((request) => {
      const entry = entriesByUrl.get(request.url);
      const timing = entry && { transferSize: entry.transferSize, encodedBodySize: entry.encodedBodySize, decodedBodySize: entry.decodedBodySize };
      return { ...request, url: request.url.replace(baseUrl, ""), cached: cached.includes(request), timing };
    }),
    wallClockMs: Date.now() - loadStartedAt
  };
}

async function measurePage(pageDefinition, profileName) {
  const context = await newContext(browser, options.browser);
  const page = await openPage(context, profileName);

  let log = recordRequests(page);
  let startedAt = Date.now();
  const coldResponse = await page.goto(pageDefinition.url, { waitUntil: "load" });
  await page.waitForTimeout(observeMs);
  const cold = await collect(log, await readTimeline(page), startedAt);
  const coldPolicy = coldResponse.headers()["content-security-policy"];
  const coldDocument = { status: coldResponse.status(), finalUrl: page.url(), encoding: (await coldResponse.allHeaders())["content-encoding"] ?? null, productionPolicy: isProductionPolicy(coldPolicy), ...hostConfiguration(coldPolicy) };

  page.removeAllListeners("requestfinished");
  page.removeAllListeners("requestfailed");
  page.removeAllListeners("websocket");
  page.removeAllListeners("pageerror");
  log = recordRequests(page);
  startedAt = Date.now();
  const warmResponse = await page.reload({ waitUntil: "load" });
  await page.waitForTimeout(observeMs);
  const warm = await collect(log, await readTimeline(page), startedAt);
  const warmDocument = { status: warmResponse.status(), finalUrl: page.url() };

  await context.close();
  return { cold: { ...cold, document: coldDocument }, warm: { ...warm, document: warmDocument } };
}

async function measureEnhancedNavigation(profileName) {
  const context = await newContext(browser, options.browser);
  const page = await openPage(context, profileName);
  await page.goto(`${baseUrl}${pathBase}/`, { waitUntil: "load" });
  await page.waitForFunction(() => window.Blazor !== undefined);
  await page.waitForTimeout(observeMs);
  // A full document load would drop this marker, so its survival proves the navigation was enhanced
  await page.evaluate(() => {
    window.__sameDocument = true;
    performance.clearResourceTimings();
  });
  const log = recordRequests(page);
  const startedAt = Date.now();
  await page.getByRole("link", { name: "Terms", exact: true }).click();
  await page.getByRole("heading", { name: "Terms of Service" }).waitFor();
  const navigationMs = Date.now() - startedAt;
  await page.waitForTimeout(observeMs);
  const timeline = await readTimeline(page);
  const collected = await collect(log, { ...timeline, loadEventEnd: null }, startedAt);
  const finalUrl = page.url();
  await context.close();
  return { ...collected, navigationMs, sameDocument: timeline.sameDocumentMarker, finalUrl };
}

function statistics(values) {
  const numbers = values.filter((value) => typeof value === "number").sort((left, right) => left - right);
  if (numbers.length === 0) return null;
  const middle = Math.floor(numbers.length / 2);
  const median = numbers.length % 2 === 1 ? numbers[middle] : Math.round((numbers[middle - 1] + numbers[middle]) / 2);
  return { median, min: numbers[0], max: numbers.at(-1) };
}

function summarizeLoad(samples) {
  const summary = {};
  for (const field of numericFields) summary[field] = statistics(samples.map((sample) => sample[field]));
  summary.runtimeRequests = [...new Set(samples.flatMap((sample) => sample.runtimeRequests))];
  summary.componentLibraryRequests = [...new Set(samples.flatMap((sample) => sample.componentLibraryRequests))];
  summary.websockets = [...new Set(samples.flatMap((sample) => sample.websockets))];
  summary.pageErrors = samples.flatMap((sample) => sample.pageErrors);
  summary.freshWithoutBrotliFile = [...new Set(samples.flatMap((sample) => sample.freshWithoutBrotliFile))];
  summary.firstSampleRequests = samples[0].requests;
  return summary;
}

function summarize(pageDefinition, samples) {
  return {
    url: pageDefinition.url.replace(baseUrl, ""),
    document: samples[0].cold.document,
    statuses: [...new Set(samples.flatMap((sample) => [sample.cold.document.status, sample.warm.document.status]))],
    productionPolicyOnEverySample: samples.every((sample) => sample.cold.document.productionPolicy),
    minimumPublishedAssetRequests: Math.min(...samples.map((sample) => sample.cold.publishedAssetRequests)),
    finalUrls: [...new Set(samples.flatMap((sample) => [sample.cold.document.finalUrl, sample.warm.document.finalUrl]))],
    cold: summarizeLoad(samples.map((sample) => sample.cold)),
    warm: summarizeLoad(samples.map((sample) => sample.warm))
  };
}

function summarizeNavigation(samples) {
  const summary = summarizeLoad(samples);
  summary.navigationMs = statistics(samples.map((sample) => sample.navigationMs));
  summary.allSameDocument = samples.every((sample) => sample.sameDocument);
  summary.finalUrls = [...new Set(samples.map((sample) => sample.finalUrl))];
  return summary;
}

// Every sample must render the page itself: a redirect to another page, such as a verification page without its flow state
// sending the browser back to login, fails even when that other page is light and loads no runtime
function checkPage(pageName, profileName, summary) {
  const problems = [];
  if (summary.statuses.some((status) => status !== 200)) problems.push(`status ${summary.statuses.join(", ")}`);
  if (!summary.productionPolicyOnEverySample) problems.push("the host does not run in Production");
  const expected = `${baseUrl}${summary.url}`;
  if (summary.finalUrls.some((url) => url !== expected)) problems.push(`landed on ${summary.finalUrls.join(", ")}`);
  if (!(summary.minimumPublishedAssetRequests > 0)) problems.push("no static asset of this publish was fetched on a cold load");
  for (const load of ["cold", "warm"]) {
    if (summary[load].runtimeRequests.length > 0) problems.push(`${load}: WebAssembly runtime requests ${summary[load].runtimeRequests.join(", ")}`);
    if (summary[load].componentLibraryRequests.length > 0) problems.push(`${load}: component library requests ${summary[load].componentLibraryRequests.join(", ")}`);
    if (summary[load].pageErrors.length > 0) problems.push(`${load}: ${summary[load].pageErrors.length} page errors`);
  }
  recordCase(`${profileName} ${pageName}`, problems);
}

function checkNavigation(profileName, navigation) {
  const problems = [];
  const expected = `${baseUrl}${pathBase}/legal/terms`;
  if (navigation.runtimeRequests.length > 0) problems.push(`WebAssembly runtime requests ${navigation.runtimeRequests.join(", ")}`);
  if (navigation.componentLibraryRequests.length > 0) problems.push(`component library requests ${navigation.componentLibraryRequests.join(", ")}`);
  if (!navigation.allSameDocument) problems.push("a full document load");
  if (navigation.finalUrls.some((url) => url !== expected)) problems.push(`landed on ${navigation.finalUrls.join(", ")}`);
  if (navigation.pageErrors.length > 0) problems.push(`${navigation.pageErrors.length} page errors`);
  recordCase(`${profileName} enhanced navigation landing to terms`, problems);
}

// Transfer and first contentful paint are separate cases, so a result shows which of the two a regression broke
function checkBudget() {
  const throttled = result.profiles.throttled;
  if (options.browser !== "chromium" || !throttled?.available) {
    failures.push("the budget is defined on Chromium with the throttled profile; run with --browser chromium and --profile throttled or all");
    return;
  }
  result.budget = { ...publicPageBudget, legalDocuments: legalDocumentBudget, basis: "median of the cold loads, Chromium, throttled profile", pages: {} };
  for (const pageName of pageNames) {
    const budget = budgetOf(pageName);
    const summary = throttled.pages[pageName];
    const transferBytes = summary?.cold.transferBytes?.median ?? null;
    const firstContentfulPaintMs = summary?.cold.firstContentfulPaintMs?.median ?? null;
    result.budget.pages[pageName] = { transferBytes, firstContentfulPaintMs, transferBytesBudget: budget.transferBytes, firstContentfulPaintMsBudget: budget.firstContentfulPaintMs };
    recordCase(
      `budget transfer ${pageName}`,
      transferBytes === null ? ["no transfer measurement"] : transferBytes > budget.transferBytes ? [`median ${transferBytes} > ${budget.transferBytes} bytes`] : []
    );
    recordCase(
      `budget first contentful paint ${pageName}`,
      firstContentfulPaintMs === null
        ? ["no first contentful paint measurement"]
        : firstContentfulPaintMs > budget.firstContentfulPaintMs
          ? [`median ${firstContentfulPaintMs} > ${budget.firstContentfulPaintMs} ms`]
          : []
    );
  }
}

// On GitHub, the per-page medians and the verdict go to the job summary next to the uploaded result file
function writeJobSummary(verdict) {
  if (!process.env.GITHUB_STEP_SUMMARY) return;
  const lines = [
    `### Public pages: ${options.browser} ${result.browserVersion}, ${options.label}, ${verdict.passed ? "passed" : "failed"}`,
    "",
    `Publish ${result.artifact.clientAssembly}, endpoint manifest ${result.artifact.endpointManifestSha256.slice(0, 12)}; runner ${result.runner.runnerImage ?? result.runner.platform}, ${result.runner.cpus} CPUs; ${sampleCount} samples after ${warmUpSamples} warm-up, ${observeMs} ms observation.`,
    ""
  ];
  if (result.budget) {
    lines.push(
      `Budget: ${publicPageBudget.transferBytes} transfer bytes and ${publicPageBudget.firstContentfulPaintMs} ms first contentful paint, ${legalDocumentBudget.transferBytes} bytes and ${legalDocumentBudget.firstContentfulPaintMs} ms for the legal documents, ${result.budget.basis}.`,
      ""
    );
  }
  lines.push("| Profile | Page | Cold transfer | Cold FCP | Warm transfer | Runtime requests |", "| --- | --- | --- | --- | --- | --- |");
  for (const [profileName, profile] of Object.entries(result.profiles)) {
    if (!profile.available) continue;
    for (const [pageName, summary] of Object.entries(profile.pages)) {
      lines.push(`| ${profileName} | ${pageName} | ${summary.cold.transferBytes?.median} | ${summary.cold.firstContentfulPaintMs?.median} | ${summary.warm.transferBytes?.median} | ${summary.cold.runtimeRequests.length + summary.warm.runtimeRequests.length} |`);
    }
  }
  if (!verdict.passed) lines.push("", ...verdict.failures.map((failure) => `- ${redact(failure)}`));
  appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${lines.join("\n")}\n\n`);
}

function sum(items, selector) {
  return items.reduce((total, item) => total + selector(item), 0);
}

function printTable() {
  const rows = [];
  for (const [profileName, profile] of Object.entries(result.profiles)) {
    if (!profile.available) {
      rows.push({ profile: profileName, page: "unavailable" });
      continue;
    }
    for (const [pageName, summary] of Object.entries(profile.pages)) {
      for (const load of ["cold", "warm"]) {
        const cell = summary[load];
        rows.push({
          profile: profileName,
          page: pageName,
          load,
          requests: cell.requestCount?.median,
          cached: cell.cachedCount?.median,
          transfer: cell.transferBytes?.median,
          brotliDisk: cell.brotliDiskBytes?.median,
          uncompressed: cell.uncompressedBytes?.median,
          fcp: cell.firstContentfulPaintMs && `${cell.firstContentfulPaintMs.median} (${cell.firstContentfulPaintMs.min}-${cell.firstContentfulPaintMs.max})`,
          load_ms: cell.loadMs && `${cell.loadMs.median} (${cell.loadMs.min}-${cell.loadMs.max})`,
          runtime: cell.runtimeRequests.length
        });
      }
    }
  }
  console.table(rows);
}
