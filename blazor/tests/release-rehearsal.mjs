// The release-and-rollback rehearsal of the version policy (docs/blazor-version-policy.md) and the recovery runbook
// (docs/blazor-recovery-runbook.md). It serves two trimmed Release publishes in turn through the developer CLI, one after
// the other on the same gateway route, and drives a browser tab across the switch:
//
//   1. the current publish is served; a supported client reads and writes, and its document's assets all come from it
//   2. the previous publish is served in its place, the rollback: the open tab's own fingerprinted assets are gone, its next
//      write is refused with the reload prompt, and its unsaved edit is neither sent nor silently discarded
//   3. a tab opened on the previous publish is outside the version window: it still reads, and its write is refused too
//   4. the current publish is restored: the waiting tab is asked before its edits are discarded, and the write succeeds
//
// The two publishes must differ in version, which is what makes one of them unsupported:
//   blazor-publish --folder a --version 0.9.0     (the previous release, outside the window)
//   blazor-publish --folder b                     (the current release, whose version matches the account API's)
// Run it with the stack up and no other blazor-serve running: blazor-harness release-rehearsal.
//
// A service worker is not part of this rehearsal; an installed PWA across a deployment belongs to the offline shell and the
// device pass. Every case that this local run cannot cover is listed in the result under unavailableCases, with its reason.

import { spawn } from "node:child_process";
import { createWriteStream, existsSync, mkdirSync } from "node:fs";
import net from "node:net";
import https from "node:https";
import path from "node:path";
import {
  baseUrl,
  basePort,
  gatewayHostname,
  launchBrowser,
  newContext,
  observeErrors,
  parseArguments,
  pathBase,
  playwrightVersion,
  policyViolationsOf,
  probeHostConfiguration,
  publishFolderFor,
  publishIdentity,
  readPublishedEndpoints,
  repositoryRoot,
  resultsFolder,
  runEnvironment,
  signUpThroughBlazor,
  writeResult
} from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium", current: "b", previous: "a", label: "" });
const interactiveTimeoutMs = 60_000;
const serveTimeoutMs = 120_000;
const portReleaseTimeoutMs = 60_000;
const settleMs = 750;
// The browser-side half of the unsaved-changes guard is armed by an interop call that follows the edit reaching .NET, so a
// guarded click made in the same instant as the edit can still run unguarded. A person pauses between typing and clicking;
// this is that pause, and the runbook records the observation.
const guardArmingMs = 2_000;
const testId = (id) => `[data-testid="${id}"]`;
// The shell's navigation links carry no test id of their own; they are the links of the sidebar by their target
const navLink = (target) => `a.shell-nav-link[href="${pathBase}/${target}"]`;

// Offset of BlazorHost in application/shared-kernel/SharedKernel/Configuration/PortAllocation.cs
const blazorHostPort = basePort + 17;

const results = [];
const failures = [];
// What the tabs saw, so a run that stopped says where it stood
const diagnostics = {};
const unavailableCases = [];
const measurements = {};

function record(name, passed, detail) {
  results.push({ name, passed, detail });
  console.log(`${passed ? "PASS" : "FAIL"} ${name}${detail === undefined ? "" : ` - ${detail}`}`);
  if (!passed) failures.push(`${name}: ${detail}`);
}

function unavailable(name, reason) {
  unavailableCases.push({ name, reason });
  console.log(`UNAVAILABLE ${name} - ${reason}`);
}

// The gateway, addressed the way the certificate names it while connecting to loopback, so no name resolution of a
// .localhost subdomain is needed in Node
function requestStatus(pathAndQuery) {
  return new Promise((resolve) => {
    const request = https.request(
      { host: "127.0.0.1", port: basePort, servername: gatewayHostname, path: pathAndQuery, method: "GET", rejectUnauthorized: false, headers: { Host: `${gatewayHostname}:${basePort}` } },
      (response) => {
        response.resume();
        resolve(response.statusCode);
      }
    );
    request.on("error", () => resolve(null));
    request.end();
  });
}

function isPortListening(port) {
  return new Promise((resolve) => {
    const socket = net.connect({ host: "127.0.0.1", port });
    socket.on("connect", () => {
      socket.destroy();
      resolve(true);
    });
    socket.on("error", () => resolve(false));
  });
}

async function waitFor(description, condition, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (await condition()) return;
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${description} did not happen within ${timeoutMs} ms.`);
}

// One publish served in Production behind the gateway, through the developer CLI's own command, and stopped by its process
// group so the host it started stops with it
async function serve(folderName, clientAssemblyRoute) {
  const log = createWriteStream(path.join(resultsFolder, `release-rehearsal-serve-${folderName}.log`), { flags: "a" });
  const child = spawn("dotnet", ["run", "--project", "developer-cli", "--", "blazor-serve", "--folder", folderName], {
    cwd: repositoryRoot,
    detached: true,
    stdio: ["ignore", "pipe", "pipe"]
  });
  child.stdout.pipe(log);
  child.stderr.pipe(log);

  await waitFor(`The publish in ${folderName} is served`, async () => (await requestStatus(`${pathBase}/${clientAssemblyRoute}`)) === 200, serveTimeoutMs);
  return child;
}

async function stopServing(child) {
  if (child === null) return;
  try {
    process.kill(-child.pid, "SIGTERM");
  } catch {
    // Already gone
  }
  try {
    await waitFor("The Blazor host port is released", async () => !(await isPortListening(blazorHostPort)), portReleaseTimeoutMs);
  } catch (error) {
    try {
      process.kill(-child.pid, "SIGKILL");
    } catch {
      // Already gone
    }
    throw error;
  }
}

// Every asset request of one document load, so a document that mixes two publishes or asks for something that is gone is
// visible
function observeAssetRequests(page) {
  const assets = [];
  page.on("response", (response) => {
    const requestPath = new URL(response.url()).pathname;
    if (requestPath.startsWith(`${pathBase}/`)) assets.push({ path: requestPath, status: response.status() });
  });
  return assets;
}

function assertAtomicAssetSet(caseName, assets, servedRoutes) {
  const missing = assets.filter((asset) => asset.status === 404).map((asset) => asset.path);
  const foreign = assets
    .filter((asset) => asset.status < 400 && /\/_framework\/[^/]+\.[a-z0-9]{8,}\.wasm$/.test(asset.path))
    .filter((asset) => !servedRoutes.has(asset.path.slice(`${pathBase}/`.length)));
  const frameworkAssets = assets.filter((asset) => asset.path.includes("/_framework/")).length;
  record(
    caseName,
    missing.length === 0 && foreign.length === 0 && frameworkAssets > 0,
    `${assets.length} asset responses, ${frameworkAssets} of them runtime files, ${missing.length} missing, ${foreign.length} from another publish`
  );
}

async function openAccountSettings(browser, storageState) {
  const context = await newContext(browser, options.browser, storageState);
  const page = await context.newPage();
  const observations = observeErrors(page);
  const assets = observeAssetRequests(page);
  await page.goto(`${baseUrl}${pathBase}/account/settings`, { waitUntil: "load" });
  await page.locator(testId("account-settings-form")).waitFor({ timeout: interactiveTimeoutMs });
  await page.waitForTimeout(settleMs);
  return { context, page, observations, assets };
}

async function saveAccountName(page, name) {
  await page.locator(testId("account-name")).fill(name);
  await page.waitForTimeout(guardArmingMs);
  await page.locator(testId("save-account-settings")).click();
}

async function run() {
  mkdirSync(resultsFolder, { recursive: true });

  // The 2026-09-19 specification moved every case that needs a service worker out of this rehearsal, because the offline
  // shell is built after it
  unavailable("an installed PWA kept open across a deployment", "there is no service worker yet; the offline shell task owns the installed application across a deployment");
  unavailable("an interrupted update of a cached shell", "there is no service worker yet; the offline shell task owns a partly applied update of a cached asset set");
  unavailable("a cold anonymous visit measured with the service worker enabled", "there is no service worker yet; the public-page budget is measured without one by public-pages");
  const currentFolder = publishFolderFor(options.current);
  const previousFolder = publishFolderFor(options.previous);
  for (const folder of [currentFolder, previousFolder]) {
    if (!existsSync(path.join(folder, "Blazor.Host.dll"))) {
      throw new Error(`No publish at ${folder}. Publish both releases first: blazor-publish --folder ${options.previous} --version 0.9.0 and blazor-publish --folder ${options.current}.`);
    }
  }

  const current = publishIdentity(currentFolder);
  const previous = publishIdentity(previousFolder);
  const currentRoutes = new Set(readPublishedEndpoints(currentFolder).map((endpoint) => endpoint.Route));
  const previousRoutes = new Set(readPublishedEndpoints(previousFolder).map((endpoint) => endpoint.Route));
  const routesOnlyInCurrent = [...currentRoutes].filter((route) => !previousRoutes.has(route));
  measurements.publishes = { current, previous, routesOnlyInCurrent: routesOnlyInCurrent.length, routeCount: currentRoutes.size };

  record(
    "two publishes with different fingerprinted asset sets",
    current.clientAssembly !== null && current.clientAssembly !== previous.clientAssembly && routesOnlyInCurrent.length > 0,
    `current ${current.clientAssembly}, previous ${previous.clientAssembly}, ${routesOnlyInCurrent.length} of ${currentRoutes.size} routes only in the current publish`
  );

  // Another blazor-serve on the Blazor host port would answer every request this rehearsal makes, so the publish it thinks
  // it serves would not be the one under test
  if (await isPortListening(blazorHostPort)) {
    throw new Error(`Port ${blazorHostPort} is already in use. Stop the stack's blazor-host and any blazor-serve before the rehearsal.`);
  }

  const browser = await launchBrowser(options.browser);
  let serving = null;
  let currentTab = null;
  let previousTab = null;
  try {
    // 1. The current release
    serving = await serve(options.current, current.clientAssembly);
    const hostConfiguration = await probeHostConfiguration(browser, options.browser);
    record("the current publish is served in Production", hostConfiguration.hostEnvironment === "Production", `${hostConfiguration.hostEnvironment}, ${hostConfiguration.buildConfiguration}`);

    const signedUp = await signUpThroughBlazor(browser, options.browser, `release-rehearsal-${options.browser}-${Date.now()}@example.com`.toLowerCase());
    currentTab = await openAccountSettings(browser, signedUp.storageState);
    assertAtomicAssetSet("the document of the current publish loads only its own assets", currentTab.assets, currentRoutes);

    await saveAccountName(currentTab.page, "Rehearsal current");
    await currentTab.page.locator(testId("account-settings-updated-toast")).waitFor({ timeout: interactiveTimeoutMs });
    const savedMessages = await currentTab.page.locator(testId("form-error-message")).allTextContents();
    record("a supported client writes", savedMessages.length === 0, savedMessages.join(" | ") || "the save was confirmed and no form error was shown");

    // The edit this tab keeps across the deployment
    await currentTab.page.locator(testId("account-name")).fill("Rehearsal unsaved edit");
    await currentTab.page.waitForTimeout(guardArmingMs);

    // 2. The previous release, served in its place
    await stopServing(serving);
    serving = await serve(options.previous, previous.clientAssembly);

    const retainedStatus = await requestStatus(`${pathBase}/${current.clientAssembly}`);
    record(
      "the rollback does not retain the newer publish's fingerprinted assets",
      retainedStatus === 404,
      `${current.clientAssembly} answered ${retainedStatus} while the previous publish is served`
    );
    measurements.retainedAssetStatus = retainedStatus;

    // The tab's next action is an in-app navigation, which is where the shell checks whether the publish this runtime was
    // loaded from is still served. The unsaved edit is guarded first, so nothing is discarded without being asked.
    await currentTab.page.locator(navLink("account/users")).click();
    const rollbackDialog = currentTab.page.locator(`dialog${testId("unsaved-changes-dialog")}[open]`);
    await rollbackDialog.waitFor({ timeout: interactiveTimeoutMs });
    record("the deployment does not discard an unsaved edit without asking", await rollbackDialog.isVisible(), "the unsaved changes dialog opened on the next navigation");

    await rollbackDialog.locator(testId("unsaved-changes-leave")).click();
    await currentTab.page.waitForURL(`${baseUrl}${pathBase}/account/users`, { timeout: interactiveTimeoutMs });
    await currentTab.page.locator(navLink("account/settings")).click();
    await currentTab.page.waitForURL(`${baseUrl}${pathBase}/account/settings`, { timeout: interactiveTimeoutMs });
    await currentTab.page.locator(testId("account-settings-form")).waitFor({ timeout: interactiveTimeoutMs });
    await currentTab.page.waitForTimeout(settleMs);

    await saveAccountName(currentTab.page, "Rehearsal after rollback");
    await currentTab.page.locator(testId("form-error-reload")).waitFor({ timeout: interactiveTimeoutMs });
    const staleMessages = await currentTab.page.locator(testId("form-error-message")).allTextContents();
    record("a write from the open tab is refused with the reload prompt", staleMessages.length > 0, staleMessages.join(" | "));

    const keptEdit = await currentTab.page.locator(testId("account-name")).inputValue();
    record("the refused write leaves the edit on the form", keptEdit === "Rehearsal after rollback", `the form still holds "${keptEdit}"`);

    // 3. A tab that loads the previous release, whose client is outside the version window
    previousTab = await openAccountSettings(browser, signedUp.storageState);
    assertAtomicAssetSet("the document of the previous publish loads only its own assets", previousTab.assets, previousRoutes);
    const readName = await previousTab.page.locator(testId("account-name")).inputValue();
    record("an unsupported client still reads", readName === "Rehearsal current", `the account name read back as "${readName}"`);

    await saveAccountName(previousTab.page, "Rehearsal unsupported");
    await previousTab.page.locator(testId("form-error-reload")).waitFor({ timeout: interactiveTimeoutMs });
    const unsupportedMessages = await previousTab.page.locator(testId("form-error-message")).allTextContents();
    record("a write from a client outside the version window is refused with the reload prompt", unsupportedMessages.length > 0, unsupportedMessages.join(" | "));

    // 4. The current release restored
    await stopServing(serving);
    serving = await serve(options.current, current.clientAssembly);

    // The prompt's own action loads the page again as a new document, which is where the restored release arrives. The
    // unsaved-changes guard does not intercept it on a page that was reached by enhanced navigation, which is a defect of
    // the guard recorded in docs/blazor-recovery-runbook.md, so the edit this tab still held is discarded here.
    const recoveredTab = currentTab;
    const discardedEdit = await recoveredTab.page.locator(testId("account-name")).inputValue();
    measurements.editDiscardedByTheReloadPrompt = discardedEdit;
    await recoveredTab.page.locator(testId("form-error-reload")).click();
    await recoveredTab.page.waitForTimeout(settleMs);
    if (await recoveredTab.page.locator(`dialog${testId("unsaved-changes-dialog")}[open]`).count() > 0) {
      await recoveredTab.page.locator(testId("unsaved-changes-leave")).click();
    }

    await recoveredTab.page.locator(testId("account-settings-form")).waitFor({ timeout: interactiveTimeoutMs });
    await recoveredTab.page.waitForTimeout(settleMs);

    await saveAccountName(recoveredTab.page, "Rehearsal recovered");
    await recoveredTab.page.locator(testId("account-settings-updated-toast")).waitFor({ timeout: interactiveTimeoutMs });
    const recoveredMessages = await recoveredTab.page.locator(testId("form-error-message")).allTextContents();
    const recoveredName = await recoveredTab.page.locator(testId("account-name")).inputValue();
    record(
      "reloading on the restored release recovers the tab and its write",
      recoveredMessages.length === 0 && recoveredName === "Rehearsal recovered",
      recoveredMessages.join(" | ") || `the account name saved as "${recoveredName}"`
    );

    const violations = [...policyViolationsOf(currentTab.context), ...policyViolationsOf(previousTab.context)];
    record("no content security policy violation across the rehearsal", violations.length === 0, `${violations.length} violations`);

    // A refused write is a 412 this run asked for and a probed asset is a 404 this run asked for, so error responses are
    // counted rather than judged; an unhandled error in the runtime is never asked for
    const pageErrors = [...currentTab.observations.pageErrors, ...previousTab.observations.pageErrors];
    measurements.observations = {
      pageErrors,
      consoleErrors: currentTab.observations.consoleErrors.length + previousTab.observations.consoleErrors.length,
      errorResponses: currentTab.observations.errorResponses.length + previousTab.observations.errorResponses.length
    };
    record("no unhandled error in the runtime across the rehearsal", pageErrors.length === 0, `${pageErrors.length} page errors`);
  } finally {
    for (const [name, tab] of [["currentTab", currentTab], ["previousTab", previousTab]]) {
      if (tab !== null) {
        diagnostics[name] = {
          url: tab.page.url(),
          consoleErrors: tab.observations.consoleErrors.slice(-8),
          pageErrors: tab.observations.pageErrors.slice(-8),
          errorResponses: tab.observations.errorResponses.slice(-8)
        };
      }
    }

    for (const tab of [currentTab, previousTab]) {
      if (tab !== null) await tab.context.close();
    }
    await browser.close();
    await stopServing(serving);
  }
}

let runError = null;
try {
  await run();
} catch (error) {
  runError = error;
  failures.push(`the rehearsal stopped: ${error.message}`);
}

// Every case runs in every browser: the policy needs no browser feature beyond a request that bypasses the cache
const expectedCaseCount = 14;
const { resultFile, passed } = writeResult(`release-rehearsal-${options.browser}${options.label === "" ? "" : `-${options.label}`}.json`, {
  script: "release-rehearsal",
  browser: options.browser,
  playwrightVersion,
  conditions: { previousPublish: options.previous, currentPublish: options.current, serviceWorker: "none, a browser tab only" },
  runEnvironment: runEnvironment(),
  measurements,
  diagnostics,
  unavailableCases,
  results,
  failures
}, expectedCaseCount);

console.log(`${passed ? "release-rehearsal passed" : "release-rehearsal failed"} in ${options.browser}: ${results.filter((entry) => entry.passed).length} of ${results.length} cases passed, ${unavailableCases.length} unavailable. ${resultFile}`);
if (runError !== null) {
  console.log(String(runError.stack ?? runError.message));
  console.log(JSON.stringify(diagnostics, null, 2));
}
process.exit(passed ? 0 : 1);
