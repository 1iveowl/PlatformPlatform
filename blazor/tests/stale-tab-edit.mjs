// What a document merged from another publish does to a form's event wiring, and what happens to an edit typed on it.
//
// After a deployment, a tab that navigates inside the application gets a document from the publish that is served now
// while its runtime is still the one it was loaded with. This script serves two trimmed Release publishes in turn, the
// way release-rehearsal.mjs does, and probes the account settings form on four documents:
//
//   1. a freshly loaded document of the publish that is served
//   2. the same tab after an enhanced navigation inside that publish, with no deployment in between
//   3. a tab whose runtime is stale but whose document is its own, because it never navigated after the deployment
//   4. a tab that navigated after the deployment, so its document comes from the publish served now
//   5. the same, reached through the guard's dialog and its Leave, on a tab that had saved once
//
// Each probe drives the same events on the same elements, records what reached .NET, which is the table T016 rests on,
// and asserts two things per document: that every event of the form reaches .NET, and that an edit arms the unload
// guard while it is unsaved and releases it once the saved value is back.
// Run it with the stack up and no other blazor-serve running: blazor-harness stale-tab-edit.

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
// The browser-side half of the unsaved-changes guard is armed by an interop call that follows the edit reaching .NET, so
// a reading taken in the same instant as the edit can still find it unarmed. A person pauses between typing and acting;
// this is that pause.
const guardArmingMs = 2_000;
const testId = (id) => `[data-testid="${id}"]`;
// The shell's navigation links carry no test id of their own; they are the links of the sidebar by their target
const navLink = (target) => `a.shell-nav-link[href="${pathBase}/${target}"]`;

// Offset of BlazorHost in application/shared-kernel/SharedKernel/Configuration/PortAllocation.cs
const blazorHostPort = basePort + 17;

const results = [];
const failures = [];
const diagnostics = {};
const measurements = { eventWiring: {} };

function record(name, passed, detail) {
  results.push({ name, passed, detail });
  console.log(`${passed ? "PASS" : "FAIL"} ${name}${detail === undefined ? "" : ` - ${detail}`}`);
  if (!passed) failures.push(`${name}: ${detail}`);
}

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

async function serve(folderName, clientAssemblyRoute) {
  const log = createWriteStream(path.join(resultsFolder, `stale-tab-edit-serve-${folderName}.log`), { flags: "a" });
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

async function openAccountSettings(browser, storageState) {
  const context = await newContext(browser, options.browser, storageState);
  const page = await context.newPage();
  const observations = observeErrors(page);
  await page.goto(`${baseUrl}${pathBase}/account/settings`, { waitUntil: "load" });
  await page.locator(testId("account-settings-form")).waitFor({ timeout: interactiveTimeoutMs });
  await page.waitForTimeout(settleMs);
  return { context, page, observations };
}

function isGuardArmed(page) {
  return page.evaluate(() => {
    const event = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  });
}

// The event handler attributes the renderer writes on an element, which is how a browser event finds its .NET handler
function eventHandlerAttributes(page, selector) {
  return page.locator(selector).evaluate((element) => element.getAttributeNames().filter((name) => name.startsWith("_bl")));
}

// Drives the same events on the same elements of the account settings form and records what reached .NET. The field is
// left holding savedName and the form left clean, so the next probe starts where this one did.
async function probeFormWiring(page, arm, savedName) {
  const nameField = page.locator(testId("account-name"));
  const validation = page.locator(testId("account-settings-form")).locator(".field-validation");
  const probe = { arm, handlerAttributes: {} };

  probe.handlerAttributes.accountName = await eventHandlerAttributes(page, testId("account-name"));
  probe.handlerAttributes.saveButton = await eventHandlerAttributes(page, testId("save-account-settings"));
  probe.handlerAttributes.deleteButton = await eventHandlerAttributes(page, testId("delete-account"));

  // The input event, raised while the field keeps the focus. The class attribute carries the EditContext's own verdict
  // (modified valid or modified invalid), so it says whether the edit reached .NET without asking the guard.
  await nameField.fill(`${arm} edit`);
  await page.waitForTimeout(guardArmingMs);
  probe.typedWithFocusKept = {
    value: await nameField.inputValue(),
    fieldClass: await nameField.getAttribute("class"),
    guardArmed: await isGuardArmed(page)
  };

  // The change event, raised when the field loses the focus
  await nameField.blur();
  await page.waitForTimeout(guardArmingMs);
  probe.afterTheFieldLostFocus = {
    fieldClass: await nameField.getAttribute("class"),
    guardArmed: await isGuardArmed(page)
  };

  // An empty name breaks the form model's data annotation, which only the validator inside .NET can report
  await nameField.fill("");
  await page.waitForTimeout(settleMs);
  probe.emptiedField = {
    ariaInvalid: await nameField.getAttribute("aria-invalid"),
    validationMessage: (await validation.first().textContent())?.trim() ?? ""
  };

  // A plain click handler on a button, and the dialog it opens: data-open is what .NET rendered, open is what the
  // dialog's own interop did with it
  await page.locator(testId("delete-account")).click();
  await page.waitForTimeout(settleMs);
  const dialog = page.locator(`dialog${testId("delete-account-dialog")}`);
  probe.clickedTheDeleteButton = {
    renderedOpen: (await dialog.getAttribute("data-open")) === "true",
    shownByTheBrowser: (await dialog.getAttribute("open")) !== null
  };
  if (probe.clickedTheDeleteButton.renderedOpen) {
    await page.locator(testId("delete-account-close")).click();
    await page.waitForTimeout(settleMs);
  }

  // Back to the saved name, so the form is clean again for the next arm
  await nameField.fill(savedName);
  await nameField.blur();
  await page.waitForTimeout(guardArmingMs);
  probe.restoredToTheSavedName = { guardArmed: await isGuardArmed(page), value: await nameField.inputValue() };

  measurements.eventWiring[arm] = probe;

  const reachedDotNet =
    probe.typedWithFocusKept.fieldClass?.includes("modified") === true &&
    probe.afterTheFieldLostFocus.fieldClass?.includes("modified") === true &&
    probe.emptiedField.ariaInvalid === "true" &&
    probe.emptiedField.validationMessage.length > 0 &&
    probe.clickedTheDeleteButton.renderedOpen &&
    probe.clickedTheDeleteButton.shownByTheBrowser;
  record(
    `every event of the form reaches .NET on ${arm}`,
    reachedDotNet,
    `the field carries ${probe.typedWithFocusKept.fieldClass} as it is typed and ${probe.afterTheFieldLostFocus.fieldClass} once it is left, an empty name is refused with "${probe.emptiedField.validationMessage}", and the delete dialog was ${probe.clickedTheDeleteButton.renderedOpen ? "rendered open" : "not opened"}`
  );

  const guarded = probe.typedWithFocusKept.guardArmed && probe.afterTheFieldLostFocus.guardArmed && !probe.restoredToTheSavedName.guardArmed;
  record(
    `an edit arms the unload guard on ${arm}`,
    guarded,
    `armed as typed: ${probe.typedWithFocusKept.guardArmed}, armed once the field was left: ${probe.afterTheFieldLostFocus.guardArmed}, still armed with the saved name back: ${probe.restoredToTheSavedName.guardArmed}`
  );

  return probe;
}

async function navigateAwayAndBack(page, throughTheDialog = false) {
  await page.locator(navLink("account/users")).click();
  if (throughTheDialog) {
    const dialog = page.locator(`dialog${testId("unsaved-changes-dialog")}[open]`);
    await dialog.waitFor({ timeout: interactiveTimeoutMs });
    await dialog.locator(testId("unsaved-changes-leave")).click();
  }

  await page.waitForURL(`${baseUrl}${pathBase}/account/users`, { timeout: interactiveTimeoutMs });
  await page.locator(navLink("account/settings")).click();
  await page.waitForURL(`${baseUrl}${pathBase}/account/settings`, { timeout: interactiveTimeoutMs });
  await page.locator(testId("account-settings-form")).waitFor({ timeout: interactiveTimeoutMs });
  await page.waitForTimeout(settleMs);
}

async function run() {
  mkdirSync(resultsFolder, { recursive: true });
  const currentFolder = publishFolderFor(options.current);
  const previousFolder = publishFolderFor(options.previous);
  for (const folder of [currentFolder, previousFolder]) {
    if (!existsSync(path.join(folder, "Blazor.Host.dll"))) {
      throw new Error(`No publish at ${folder}. Publish both releases first: blazor-publish --folder ${options.previous} --version 0.9.0 and blazor-publish --folder ${options.current}.`);
    }
  }

  const current = publishIdentity(currentFolder);
  const previous = publishIdentity(previousFolder);
  measurements.publishes = { current, previous };

  if (await isPortListening(blazorHostPort)) {
    throw new Error(`Port ${blazorHostPort} is already in use. Stop the stack's blazor-host and any blazor-serve before this script.`);
  }

  const browser = await launchBrowser(options.browser);
  let serving = null;
  let navigatingTab = null;
  let stayingTab = null;
  let savingTab = null;
  const savedName = "Stale tab probe";
  try {
    serving = await serve(options.current, current.clientAssembly);
    const hostConfiguration = await probeHostConfiguration(browser, options.browser);
    record("the current publish is served in Production", hostConfiguration.hostEnvironment === "Production", `${hostConfiguration.hostEnvironment}, ${hostConfiguration.buildConfiguration}`);

    const signedUp = await signUpThroughBlazor(browser, options.browser, `stale-tab-edit-${options.browser}-${Date.now()}@example.com`.toLowerCase());

    // The tab that saves, which is the release rehearsal's own tab: it saves the name every probe returns the field to,
    // keeps an unsaved edit across the deployment and crosses it through the guard's dialog. It saves first and alone,
    // because the account API has the gateway issue new session cookies on that write and a second tab saving would
    // leave the others holding a rotated refresh token.
    savingTab = await openAccountSettings(browser, signedUp.storageState);
    await savingTab.page.locator(testId("account-name")).fill(savedName);
    await savingTab.page.waitForTimeout(guardArmingMs);
    await savingTab.page.locator(testId("save-account-settings")).click();
    await savingTab.page.locator(testId("account-settings-updated-toast")).waitFor({ timeout: interactiveTimeoutMs });
    await savingTab.page.waitForTimeout(settleMs);

    navigatingTab = await openAccountSettings(browser, signedUp.storageState);

    await probeFormWiring(navigatingTab.page, "a freshly loaded document", savedName);
    await navigateAwayAndBack(navigatingTab.page);
    await probeFormWiring(navigatingTab.page, "an enhanced navigation inside one publish", savedName);

    // The same navigation, but blocked by the guard and let through with Leave, which is the path the release rehearsal
    // takes across the deployment. Without a deployment it separates the Leave from the merged document.
    await navigatingTab.page.locator(testId("account-name")).fill("Edit discarded by a Leave");
    await navigatingTab.page.waitForTimeout(guardArmingMs);
    await navigateAwayAndBack(navigatingTab.page, true);
    await probeFormWiring(navigatingTab.page, "an enhanced navigation reached through a Leave, one publish", savedName);

    // A tab that will not navigate after the deployment, so its document stays its own
    stayingTab = await openAccountSettings(browser, signedUp.storageState);

    // The edit the saving tab holds across the deployment
    await savingTab.page.locator(testId("account-name")).fill("Edit held across the deployment after a save");
    await savingTab.page.waitForTimeout(guardArmingMs);

    // The deployment: the previous release is served in the current one's place
    await stopServing(serving);
    serving = await serve(options.previous, previous.clientAssembly);
    measurements.retainedAssetStatus = await requestStatus(`${pathBase}/${current.clientAssembly}`);

    await probeFormWiring(stayingTab.page, "a stale runtime on its own document", savedName);

    await navigateAwayAndBack(navigatingTab.page);
    await probeFormWiring(navigatingTab.page, "a document merged from the other publish", savedName);

    await navigateAwayAndBack(savingTab.page, true);
    await probeFormWiring(savingTab.page, "a document merged from the other publish, reached through a Leave on a tab that had saved", savedName);

    const violations = [...policyViolationsOf(navigatingTab.context), ...policyViolationsOf(stayingTab.context), ...policyViolationsOf(savingTab.context)];
    record("no content security policy violation across the probe", violations.length === 0, `${violations.length} violations`);
  } finally {
    for (const [name, tab] of [["navigatingTab", navigatingTab], ["stayingTab", stayingTab], ["savingTab", savingTab]]) {
      if (tab !== null) {
        diagnostics[name] = {
          url: tab.page.url(),
          consoleErrors: tab.observations.consoleErrors.slice(-8),
          pageErrors: tab.observations.pageErrors.slice(-8),
          errorResponses: tab.observations.errorResponses.slice(-8)
        };
      }
    }

    for (const tab of [navigatingTab, stayingTab, savingTab]) {
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
  failures.push(`the probe stopped: ${error.message}`);
}

const expectedCaseCount = 14;
const { resultFile, passed } = writeResult(`stale-tab-edit-${options.browser}${options.label === "" ? "" : `-${options.label}`}.json`, {
  script: "stale-tab-edit",
  browser: options.browser,
  playwrightVersion,
  conditions: { previousPublish: options.previous, currentPublish: options.current, serviceWorker: "none, a browser tab only" },
  runEnvironment: runEnvironment(),
  measurements,
  diagnostics,
  results,
  failures
}, expectedCaseCount);

console.log(`${passed ? "stale-tab-edit passed" : "stale-tab-edit failed"} in ${options.browser}: ${results.filter((entry) => entry.passed).length} of ${results.length} cases passed. ${resultFile}`);
if (runError !== null) {
  console.log(String(runError.stack ?? runError.message));
  console.log(JSON.stringify(diagnostics, null, 2));
}
process.exit(passed ? 0 : 1);
