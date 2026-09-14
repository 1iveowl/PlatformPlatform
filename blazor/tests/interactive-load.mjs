// Measures cold and warm loads of the authenticated WebAssembly page on the trimmed Release publish, with the cache outcome
// of every runtime resource, to show where warm time goes in each browser.
//
// Prerequisites are the same as for trimmed-smoke.mjs. Run one browser at a time:
//   dotnet run --project developer-cli -- blazor-harness interactive-load --browser firefox
// Options: --samples 7, --label baseline, --firefox-preferences <name=value,...> (Firefox only, for experiments).
//
// Time to interactive: navigation start to the DOM mutation that turns the page's render-mode text into "Interactive: True",
// which the WebAssembly component renders once it runs. Cold is a fresh browser context, warm a reload in the same context;
// one warm-up sample is discarded and medians, minimums and maximums are reported. Unthrottled only.

import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { baseUrl, launchBrowser, newContext, parseArguments, pathBase, playwright, playwrightVersion, resultsFolder, signUpThroughBlazor } from "./support/stack.mjs";

const interactiveTimeoutMs = 60_000;
const warmUpSamples = 1;
const options = parseArguments(process.argv.slice(2), { browser: "chromium", samples: "7", label: "baseline" });
const sampleCount = Number(options.samples);

mkdirSync(resultsFolder, { recursive: true });
const browser = options["firefox-preferences"] && options.browser === "firefox" ? await launchFirefoxWithPreferences(options["firefox-preferences"]) : await launchBrowser(options.browser);
const result = { label: options.label, browser: options.browser, browserVersion: browser.version(), playwrightVersion, firefoxPreferences: options["firefox-preferences"] ?? null, startedAt: new Date().toISOString() };
const failures = [];

try {
  const account = await signUpThroughBlazor(browser, options.browser, `interactive-${options.browser}-${Date.now()}@example.com`);
  const samples = [];
  for (let sample = 0; sample < warmUpSamples + sampleCount; sample++) {
    const measured = await measureSample(account.storageState);
    if (sample >= warmUpSamples) samples.push(measured);
  }
  result.cold = summarize(samples.map((sample) => sample.cold));
  result.warm = summarize(samples.map((sample) => sample.warm));
  result.firstSample = samples[0];
  for (const load of ["cold", "warm"]) {
    if (result[load].timeToInteractiveMs === null) failures.push(`${load}: never interactive`);
    if (result[load].pageErrors.length > 0) failures.push(`${load}: ${result[load].pageErrors.length} page errors`);
  }
} catch (error) {
  failures.push(String(error.stack ?? error).slice(0, 1_000));
} finally {
  await browser.close();
}

result.failures = failures;
result.passed = failures.length === 0;
result.finishedAt = new Date().toISOString();
const resultFile = path.join(resultsFolder, `interactive-load-${options.label}-${options.browser}.json`);
writeFileSync(resultFile, JSON.stringify(result, null, 2));
console.table(
  ["cold", "warm"].map((load) => ({
    load,
    tti: format(result[load]?.timeToInteractiveMs),
    lastRuntimeResponseEnd: format(result[load]?.lastRuntimeResponseEndMs),
    requests: result[load]?.requestCount?.median,
    cached: result[load]?.cachedCount?.median,
    revalidated: result[load]?.revalidatedCount?.median,
    transfer: result[load]?.transferBytes?.median
  }))
);
console.log(`${options.browser} ${result.browserVersion} (${options.label}): ${result.passed ? "passed" : `failed: ${failures.join(" ; ")}`}`);
console.log(`Result file: ${resultFile}`);
process.exitCode = result.passed ? 0 : 1;

function launchFirefoxWithPreferences(preferenceList) {
  const firefoxUserPrefs = Object.fromEntries(
    preferenceList.split(",").map((pair) => {
      const [name, value] = pair.split("=");
      return [name, value === "true" ? true : value === "false" ? false : Number.isNaN(Number(value)) ? value : Number(value)];
    })
  );
  return playwright.firefox.launch({ firefoxUserPrefs });
}

async function measureSample(storageState) {
  const context = await newContext(browser, options.browser, storageState);
  await context.addInitScript(() => {
    new MutationObserver((_, observer) => {
      if (document.querySelector('[data-testid="render-mode"]')?.textContent === "Interactive: True") {
        window.__interactiveAt = performance.now();
        observer.disconnect();
      }
    }).observe(document, { subtree: true, childList: true, characterData: true });
  });
  const page = await context.newPage();
  const url = `${baseUrl}${pathBase}/app`;
  const pageErrors = [];
  page.on("pageerror", (error) => pageErrors.push(String(error.message).slice(0, 400)));

  await page.goto(url, { waitUntil: "load" });
  const cold = await readLoad(page, pageErrors.splice(0));
  await page.reload({ waitUntil: "load" });
  const warm = await readLoad(page, pageErrors.splice(0));
  await context.close();
  return { cold, warm };
}

async function readLoad(page, pageErrors) {
  const interactive = await page.waitForFunction(() => typeof window.__interactiveAt === "number", null, { timeout: interactiveTimeoutMs }).then(() => true, () => false);
  // The page loads the tenant list once it runs; a reload while that request is in flight aborts it, which WebKit reports as
  // a page error of the old document
  await page.locator('[data-testid="switch-tenant"]').first().waitFor({ timeout: interactiveTimeoutMs }).catch(() => {});
  const timeline = await page.evaluate(() => {
    const resources = performance.getEntriesByType("resource");
    const navigation = performance.getEntriesByType("navigation")[0];
    return {
      timeToInteractiveMs: typeof window.__interactiveAt === "number" ? Math.round(window.__interactiveAt) : null,
      loadMs: Math.round(navigation.loadEventStart),
      finalUrl: location.href,
      resources: resources.map((entry) => ({
        url: entry.name,
        initiatorType: entry.initiatorType,
        responseStatus: entry.responseStatus ?? null,
        transferSize: entry.transferSize,
        encodedBodySize: entry.encodedBodySize,
        startTime: Math.round(entry.startTime),
        responseEnd: Math.round(entry.responseEnd)
      })),
      navigationTransferSize: navigation.transferSize
    };
  });
  const runtime = timeline.resources.filter((entry) => new URL(entry.url).pathname.includes("/_framework/"));
  return {
    interactive,
    finalUrl: timeline.finalUrl,
    timeToInteractiveMs: timeline.timeToInteractiveMs,
    loadMs: timeline.loadMs,
    lastRuntimeResponseEndMs: runtime.length === 0 ? null : Math.max(...runtime.map((entry) => entry.responseEnd)),
    requestCount: timeline.resources.filter((entry) => entry.transferSize > 0).length + 1,
    cachedCount: timeline.resources.filter((entry) => entry.transferSize === 0).length,
    revalidatedCount: timeline.resources.filter((entry) => entry.responseStatus === 304).length,
    transferBytes: timeline.navigationTransferSize + timeline.resources.reduce((total, entry) => total + entry.transferSize, 0),
    runtimeResources: runtime.map(({ url, ...rest }) => ({ path: new URL(url).pathname, ...rest })),
    pageErrors
  };
}

function statistics(values) {
  const numbers = values.filter((value) => typeof value === "number").sort((left, right) => left - right);
  if (numbers.length === 0) return null;
  const middle = Math.floor(numbers.length / 2);
  const median = numbers.length % 2 === 1 ? numbers[middle] : Math.round((numbers[middle - 1] + numbers[middle]) / 2);
  return { median, min: numbers[0], max: numbers.at(-1) };
}

function summarize(loads) {
  const summary = {};
  for (const field of ["timeToInteractiveMs", "loadMs", "lastRuntimeResponseEndMs", "requestCount", "cachedCount", "revalidatedCount", "transferBytes"]) {
    summary[field] = statistics(loads.map((load) => load[field]));
  }
  summary.finalUrls = [...new Set(loads.map((load) => load.finalUrl))];
  summary.pageErrors = loads.flatMap((load) => load.pageErrors);
  return summary;
}

function format(value) {
  return value ? `${value.median} (${value.min}-${value.max})` : null;
}
