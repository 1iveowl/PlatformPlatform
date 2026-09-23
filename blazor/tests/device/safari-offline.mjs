// Spike (EP-187): the offline shell in a real Safari tab on macOS, driven through safaridriver, with the network refused
// at the host by the runner's own proxy on the gateway port. It also takes the first step of T023 (EP-185): whether the
// worker registers in a Safari tab, whether it takes control, what Cache Storage holds and what the offline navigation does.
//
// Cases:
// 1. The page is a secure context with the service worker and Cache Storage interfaces.
// 2. The worker registers under the path base and reaches the activated state.
// 3. After a navigation inside the authenticated surface, the worker controls the page.
// 4. Cache Storage holds the offline shell document and nothing but assets beside it.
// 5. With the network refused, a navigation into the authenticated surface shows the offline shell.
// 6. With the network refused, a public route is not answered from any cache.
// 7. With the network back, the authenticated surface loads from the server again.
//
// Readings recorded beside the cases, never judged: an explicit registration attempt when case 2 finds none (the
// application swallows registration errors), the worker's state at each step, and a screenshot per offline navigation.
//
// Run on the Mac, from the repository folder: node blazor/tests/device/safari-offline.mjs [--upstream 19000]

import {
  baseUrl,
  basePort,
  caseRecorder,
  checkPreconditions,
  fail,
  HostNetwork,
  macEnvironment,
  parseArguments,
  pathBase,
  publishIdentity,
  servedWorkerVersion,
  signUp,
  sleep,
  startSafariDriver,
  WebDriverSession,
  writeResult
} from "./support.mjs";

const options = parseArguments(process.argv.slice(2), { upstream: "19000", "driver-port": "4444" });
const upstreamPort = Number(options.upstream);
const expectedCaseCount = 7;
const scope = `${pathBase}/`;
const appUrl = `${baseUrl}${pathBase}/app`;
const publicUrl = `${baseUrl}${pathBase}/legal/terms`;
const offlineSettleMs = 4_000;

const problems = await checkPreconditions(upstreamPort);
if (problems.length > 0) {
  for (const problem of problems) console.log(`SETUP ${problem}`);
  process.exit(2);
}

const network = new HostNetwork(basePort, upstreamPort);
await network.online();
const workerVersion = await servedWorkerVersion();
const driver = await startSafariDriver(Number(options["driver-port"]));
const { results, check } = caseRecorder();
const readings = {};
const failures = [];
let session;

// Reads the worker registration, the controller and Cache Storage in the page, in one call
const readWorkerState = `
  const [scope, done] = arguments;
  (async () => {
    const registration = await navigator.serviceWorker.getRegistration(scope);
    const describe = (worker) => (worker ? { state: worker.state, scriptURL: new URL(worker.scriptURL).pathname } : null);
    const cacheNames = await caches.keys();
    const caches_ = {};
    for (const name of cacheNames) {
      const keys = await (await caches.open(name)).keys();
      caches_[name] = keys.map((request) => new URL(request.url).pathname);
    }
    const navigation = performance.getEntriesByType("navigation")[0];
    done({
      registration: registration ? { scope: new URL(registration.scope).pathname, installing: describe(registration.installing), waiting: describe(registration.waiting), active: describe(registration.active) } : null,
      controller: describe(navigator.serviceWorker.controller),
      workerStart: navigation ? navigation.workerStart : null,
      caches: caches_
    });
  })().catch((error) => done({ error: String(error) }));`;

const waitForActivation = `
  const [scope, timeoutMs, done] = arguments;
  (async () => {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      const registration = await navigator.serviceWorker.getRegistration(scope);
      if (registration?.active?.state === "activated") return done({ activated: true });
      await new Promise((resolve) => setTimeout(resolve, 250));
    }
    const registration = await navigator.serviceWorker.getRegistration(scope);
    done({ activated: false, found: registration !== undefined });
  })().catch((error) => done({ error: String(error) }));`;

const explicitRegistration = `
  const [scope, done] = arguments;
  navigator.serviceWorker.register(scope + "service-worker.js", { scope, updateViaCache: "none" })
    .then((registration) => done({ registered: true, scope: new URL(registration.scope).pathname }))
    .catch((error) => done({ registered: false, error: error.name + ": " + error.message }));`;

async function pageReading() {
  return {
    url: await session.currentUrl().catch((error) => `unreadable: ${error.message}`),
    title: await session.title().catch((error) => `unreadable: ${error.message}`),
    offlinePage: (await session.find('[data-testid="offline-page"]').catch(() => null)) !== null,
    applicationElements: await session.execute("return document.querySelectorAll('[data-testid]').length").catch((error) => `unreadable: ${error.message}`),
    styles: await session.execute(styleReading).catch((error) => `unreadable: ${error.message}`)
  };
}

// Which linked stylesheets applied and what the body computes, so a shell served without its styles is told apart from one
// served with them; a sheet whose rules cannot be read or that has none did not load
const styleReading = `
  const sheets = [...document.querySelectorAll('link[rel="stylesheet"]')].map((link) => {
    let rules = null;
    try { rules = link.sheet ? link.sheet.cssRules.length : null; } catch (error) { rules = "unreadable: " + error.name; }
    return { href: new URL(link.href).pathname + new URL(link.href).search, rules };
  });
  const body = getComputedStyle(document.body);
  return { sheets, bodyFontFamily: body.fontFamily, bodyBackground: body.backgroundColor };`;

async function navigateOffline(url, screenshotName) {
  const navigation = await session.navigate(url).then(
    () => "returned",
    (error) => `raised ${error.message}`
  );
  await sleep(offlineSettleMs);
  const reading = { navigation, ...(await pageReading()) };
  reading.screenshot = await session.screenshot(screenshotName).catch((error) => `not taken: ${error.message}`);
  return reading;
}

try {
  session = await WebDriverSession.create(driver.url, { browserName: "safari" });
  readings.capabilities = session.capabilities;
  const email = `device-safari-${Date.now()}@example.com`;
  await signUp(session, email);

  await check("the page is a secure context with the service worker and Cache Storage interfaces", async () => {
    const support = await session.execute("return { secureContext: isSecureContext, serviceWorker: 'serviceWorker' in navigator, cacheStorage: 'caches' in self }");
    readings.support = support;
    if (!support.secureContext || !support.serviceWorker || !support.cacheStorage) throw fail("An interface the offline shell needs is missing.", support);
    return support;
  });

  await check("the worker registers under the path base and is activated", async () => {
    const activation = await session.executeAsync(waitForActivation, [scope, 20_000]);
    readings.afterSignup = await session.executeAsync(readWorkerState, [scope]);
    if (activation.activated) return readings.afterSignup.registration;
    readings.explicitRegistration = await session.executeAsync(explicitRegistration, [scope]);
    throw fail("No activated worker under the path base.", { activation, state: readings.afterSignup, explicitRegistration: readings.explicitRegistration });
  });

  await check("after a navigation inside the authenticated surface the worker controls the page", async () => {
    await session.navigate(appUrl);
    await session.waitFor('[data-testid]');
    await sleep(1_000);
    readings.afterNavigation = await session.executeAsync(readWorkerState, [scope]);
    if (readings.afterNavigation.controller === null) throw fail("The page has no controlling worker.", readings.afterNavigation);
    return { controller: readings.afterNavigation.controller, workerStart: readings.afterNavigation.workerStart };
  });

  await check("Cache Storage holds the offline shell document and only assets beside it", async () => {
    const cachesRead = readings.afterNavigation.caches ?? {};
    const stored = Object.values(cachesRead).flat();
    const shell = `${pathBase}/app/offline`;
    const documents = stored.filter((entry) => !entry.slice(entry.lastIndexOf("/")).includes(".") && entry !== shell);
    const summary = Object.fromEntries(Object.entries(cachesRead).map(([name, entries]) => [name, entries.length]));
    readings.storedAssetKeys = await session.executeAsync(
      `const [done] = arguments; (async () => { const names = (await caches.keys()).filter((name) => name.includes("assets")); const keys = names.length ? await (await caches.open(names[0])).keys() : []; done(keys.map((request) => { const url = new URL(request.url); return url.pathname + url.search; }).filter((key) => key.includes(".css"))); })().catch((error) => done(String(error)));`
    );
    if (!stored.includes(shell)) throw fail("The offline shell is not stored.", { summary });
    if (documents.length > 0) throw fail("Documents other than the shell are stored.", { summary, documents });
    return { summary, workerVersion };
  });

  // The shell online, as the reference its offline rendering is compared with
  await session.navigate(`${baseUrl}${pathBase}/app/offline`);
  await session.waitFor('[data-testid="offline-page"]');
  readings.shellOnline = await pageReading();
  readings.shellOnline.screenshot = await session.screenshot("safari-online-shell.png").catch((error) => `not taken: ${error.message}`);

  await network.offline();

  await check("with the network refused at the host an authenticated navigation shows the offline shell", async () => {
    readings.offlineApp = await navigateOffline(appUrl, "safari-offline-app.png");
    if (!readings.offlineApp.offlinePage) throw fail("The offline shell was not shown.", readings.offlineApp);
    return readings.offlineApp;
  });

  await check("with the network refused at the host a public route is not answered from a cache", async () => {
    readings.offlinePublic = await navigateOffline(publicUrl, "safari-offline-public.png");
    if (readings.offlinePublic.offlinePage || (typeof readings.offlinePublic.applicationElements === "number" && readings.offlinePublic.applicationElements > 0)) {
      throw fail("A page of the application answered a public route while offline.", readings.offlinePublic);
    }
    return readings.offlinePublic;
  });

  await network.online();

  await check("with the network back the authenticated surface loads from the server", async () => {
    await session.navigate(appUrl);
    await session.waitFor('[data-testid]');
    const reading = await pageReading();
    if (reading.offlinePage) throw fail("The offline shell is still shown with the network back.", reading);
    return reading;
  });
} catch (error) {
  failures.push(`The run stopped: ${error.message}`);
  console.log(`ERROR ${error.message}`);
} finally {
  await session?.close();
  driver.process.kill();
  await network.offline().catch(() => undefined);
}

const { resultFile, passed } = writeResult(
  "safari-offline-shell.json",
  {
    target: "real Safari, macOS",
    publish: publishIdentity(),
    servedWorkerVersion: workerVersion,
    environment: macEnvironment(),
    network: `refused at the host: the runner's loopback proxy on ${basePort} closed, upstream ${upstreamPort}`,
    readings,
    results,
    failures
  },
  expectedCaseCount
);
console.log(`${passed ? "PASSED" : "FAILED"} safari-offline-shell: ${resultFile}`);
process.exit(passed ? 0 : 1);
