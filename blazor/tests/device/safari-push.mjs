// Push notifications in real Safari on macOS, through the Apple push service, driven by safaridriver. Nothing in the page is
// replaced: the real client code, the real account API, the real service worker and the real push service.
//
// Cases:
// 1. The notification permission can be answered by script, without a person at the prompt.
// 2. The switch subscribes this browser at the Apple push service and the account keeps the row.
// 3. The test notification is accepted by the account API and confirmed on screen.
// 4. The test notification is delivered: the worker shows it, read back through the registration, not by eye. When nothing
//    is shown, Safari's push daemon log for the send is kept beside the result as the evidence of where the push went.
// 5. A second test within the limit's interval is refused.
// 6. A permission revoked by script while the browser keeps its subscription reads the switch off with the blocked notice on
//    the next visit and removes the account's row (T022). Manual when the driver answers that it cannot set a permission.
// 7. Logging out deletes the row this device stored, read back by signing in again.
//
// A case that cannot be reached because an earlier step is impossible by script is recorded as failed with that reason, so
// the result says exactly where automation stops on this browser. Only the macOS target: web push on iOS needs an app added
// to the Home Screen, which a driver does not reach.
//
// Run on the Mac, from the repository folder, or through run.mjs:
//   node blazor/tests/device/safari-push.mjs [--upstream 19000]

import { execFileSync } from "node:child_process";
import {
  baseUrl,
  basePort,
  caseRecorder,
  checkPreconditions,
  exitCodeFor,
  fail,
  HostNetwork,
  logOut,
  manual,
  parseArguments,
  pathBase,
  publishIdentity,
  servedWorkerVersion,
  signIn,
  signUp,
  sleep,
  startSafariDriver,
  targetEnvironment,
  targets,
  WebDriverSession,
  writeArtifact,
  writeResult
} from "./support.mjs";

const options = parseArguments(process.argv.slice(2), { upstream: "19000", "driver-port": "4444" });
const upstreamPort = Number(options.upstream);
const expectedCaseCount = 7;
const preferencesUrl = `${baseUrl}${pathBase}/user/preferences`;
const subscriptionsPath = "/api/account/users/me/push-subscriptions";
const deliveryTimeoutMs = 45_000;
const deliveryLogFile = "safari-push-delivery.log";
// The processes that carry a web push on macOS: the push daemon of the operating system, Safari's own push daemon, and
// Safari and its web content, whose push lines are the ones that mention it
const deliveryLogPredicate = [
  'process == "apsd"',
  'process == "webpushd"',
  '(process == "Safari" AND composedMessage CONTAINS[c] "push")',
  '(process BEGINSWITH "com.apple.WebKit" AND composedMessage CONTAINS[c] "push")'
].join(" OR ");
const testId = (id) => `[data-testid="${id}"]`;

const problems = await checkPreconditions(upstreamPort);
if (problems.length > 0) {
  for (const problem of problems) console.log(`SETUP ${problem}`);
  process.exit(2);
}

const network = new HostNetwork(basePort, upstreamPort);
await network.online();
const workerVersion = await servedWorkerVersion();
const driver = await startSafariDriver(Number(options["driver-port"]));
// What the page showed when a case failed: where it was, whether its window had focus and was visible, which element held
// focus and a screenshot, so a press that had no effect can be told apart from a page that was elsewhere or in the background
const { results, check } = caseRecorder(async (name, index) => ({
  page: await session.execute(`return { url: location.href, title: document.title, hasFocus: document.hasFocus(), visibilityState: document.visibilityState, activeElement: document.activeElement?.getAttribute("data-testid") ?? document.activeElement?.tagName ?? null, toasts: [...document.querySelectorAll('[data-testid$="-toast"]')].map((toast) => toast.getAttribute("data-testid")) }`),
  windowHandles: await session.command("GET", "/window/handles").catch((error) => `unreadable: ${error.message}`),
  screenshot: await session.screenshot(`safari-push-failed-case-${index + 1}.png`).catch((error) => `not taken: ${error.message}`)
}));
const readings = {};
const failures = [];
let session;
let permissionByScript = null;

const pageState = `
  const [path, done] = arguments;
  (async () => {
    const registration = await navigator.serviceWorker.getRegistration("${pathBase}/");
    const subscription = registration ? await registration.pushManager.getSubscription() : null;
    const response = await fetch(path, { headers: { accept: "application/json" } });
    const rows = response.ok ? (await response.json()).subscriptions.map((row) => row.id) : [];
    const switchElement = document.querySelector('[data-testid="notifications-switch"]');
    done({
      permission: Notification.permission,
      subscriptionHost: subscription ? new URL(subscription.endpoint).host : null,
      remembered: localStorage.getItem("blazor-push-subscription"),
      rowsStatus: response.status,
      rows,
      switchChecked: switchElement?.getAttribute("aria-checked") ?? null,
      switchDisabled: switchElement ? switchElement.hasAttribute("disabled") || switchElement.getAttribute("aria-disabled") === "true" : null,
      notice: document.querySelector('[data-testid="notifications-notice"]')?.innerText ?? null
    });
  })().catch((error) => done({ error: String(error) }));`;

const readNotifications = `
  const [done] = arguments;
  navigator.serviceWorker.getRegistration("${pathBase}/")
    .then((registration) => registration.getNotifications())
    .then((notifications) => done(notifications.map((notification) => ({ title: notification.title, body: notification.body }))))
    .catch((error) => done({ error: String(error) }));`;

async function openPreferences() {
  await session.navigate(preferencesUrl);
  await session.waitFor(testId("preferences-notifications"), 60_000);
  await session.waitFor(testId("notifications-switch"), 60_000);
}

async function state() {
  return session.executeAsync(pageState, [subscriptionsPath]);
}

async function waitForSwitch(checked, timeoutMs = 30_000) {
  await session.waitFor(`${testId("notifications-switch")}[aria-checked="${checked}"]`, timeoutMs);
}

// The W3C permissions extension, which a driver may or may not implement
async function setPermission(value) {
  return session.command("POST", "/permissions", { descriptor: { name: "notifications" }, state: value }).then(
    () => "accepted",
    (error) => `refused: ${error.message}`
  );
}

// The unified log accepts a start in the Mac's local time, to the second
function localLogTime(date) {
  const pad = (value) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

// Where the push went after the push service accepted it, read from the Mac's own log rather than assumed; the whole log is
// kept as a file and the lines that name push, delivery or the session are quoted in the result
function readDeliveryLog(since) {
  let text;
  try {
    text = execFileSync("log", ["show", "--style", "compact", "--start", localLogTime(since), "--predicate", deliveryLogPredicate], { encoding: "utf8", maxBuffer: 64 * 2 ** 20, timeout: 120_000 });
  } catch (error) {
    return { unavailable: String(error.stderr || error.message).trim().split("\n")[0] };
  }
  const lines = text.split("\n").filter((line) => line.trim().length > 0);
  const file = writeArtifact(deliveryLogFile, text);
  const excerpt = lines.filter((line) => /push|deliver|drop|discard|ephemeral|automation|data ?store|subscription|topic/i.test(line)).slice(0, 80);
  // The lines that say what Safari did with the push: which of its connections took it, whether a worker's push event
  // fired, and whether it completed successfully
  const evidence = {
    routing: lines.filter((line) => /getPendingPushMessages .*Fetched/.test(line)),
    fired: lines.filter((line) => /Firing push event|queueTaskToFirePushEvent/.test(line)),
    completed: lines.filter((line) => /Push message processing complete\. Callback result: 1/.test(line))
  };
  return { file, since: since.toISOString(), predicate: deliveryLogPredicate, lineCount: lines.length, evidence, excerpt };
}

function requireGranted() {
  if (!permissionByScript) throw fail("Not reached: the notification permission could not be granted by script.", readings.permission);
}

try {
  session = await WebDriverSession.create(driver.url, { browserName: "safari" });
  readings.capabilities = session.capabilities;
  // The driver's default window is small enough for a toast to cover the controls a case presses
  readings.window = await session.command("POST", "/window/rect", { width: 1280, height: 1000 }).catch((error) => `not resized: ${error.message}`);
  const email = `device-push-${Date.now()}@example.com`;
  await signUp(session, email);
  await openPreferences();

  // Recorded as it is found: an automation session may start with the permission already granted, which is not the same
  // as a script answering the prompt; the reading says which it was
  await check("the notification permission is granted without a person at the prompt", async () => {
    readings.permission = { before: (await state()).permission, setPermissionGranted: await setPermission("granted") };
    readings.permission.afterSetPermission = (await state()).permission;
    if (readings.permission.afterSetPermission !== "granted") {
      // Without the extension, the switch asks, and the prompt is not a script dialog a driver can accept; record what is
      await session.click(testId("notifications-switch"));
      await sleep(3_000);
      readings.permission.alert = await session.command("GET", "/alert/text").then((text) => `open: ${text}`, (error) => `none: ${error.webDriverError ?? error.message}`);
      readings.permission.acceptAlert = await session.command("POST", "/alert/accept", {}).then(() => "accepted", (error) => `refused: ${error.webDriverError ?? error.message}`);
      await sleep(2_000);
      readings.permission.afterSwitch = (await state()).permission;
    }
    permissionByScript = (await state()).permission === "granted";
    if (!permissionByScript) throw fail("The permission stays unanswered or denied without a person at the prompt.", readings.permission);
    return readings.permission;
  });

  await check("the switch subscribes at the Apple push service and the account keeps the row", async () => {
    requireGranted();
    await openPreferences();
    if ((await state()).switchChecked !== "true") await session.click(testId("notifications-switch"));
    await waitForSwitch("true");
    const subscribed = await state();
    readings.subscribed = subscribed;
    if (subscribed.subscriptionHost === null || !subscribed.subscriptionHost.endsWith("push.apple.com")) throw fail("The subscription is not at the Apple push service.", subscribed);
    if (subscribed.remembered === null || !subscribed.rows.includes(subscribed.remembered)) throw fail("The account does not hold the row this device remembers.", subscribed);
    return { subscriptionHost: subscribed.subscriptionHost, rowStored: true };
  });

  await check("the test notification is accepted and confirmed", async () => {
    requireGranted();
    // Observes, never changes, the page's own request: the answer names how many subscriptions the push service accepted
    await session.execute(`
      const original = window.fetch;
      window.__deviceTestSend = null;
      window.fetch = async (...args) => {
        const response = await original(...args);
        const url = String(args[0] instanceof Request ? args[0].url : args[0]);
        if (url.endsWith("${subscriptionsPath}/test")) response.clone().text().then((body) => (window.__deviceTestSend = { status: response.status, body }));
        return response;
      };`);
    // A few seconds early, so the log read for delivery includes the send itself
    readings.testSendStartedAt = new Date(Date.now() - 5_000).toISOString();
    await session.click(testId("notifications-send-test"));
    await session.waitFor(testId("test-notification-sent-toast"), 30_000);
    await sleep(500);
    readings.testSend = await session.execute("return window.__deviceTestSend");
    return { confirmed: true, accountApi: readings.testSend };
  });

  await check("the test notification is delivered through the push service and shown by the worker", async () => {
    requireGranted();
    const deadline = Date.now() + deliveryTimeoutMs;
    let shown = [];
    while (Date.now() < deadline) {
      shown = await session.executeAsync(readNotifications);
      if (Array.isArray(shown) && shown.length > 0) break;
      await sleep(500);
    }
    readings.delivered = shown;
    if (!Array.isArray(shown) || shown.length === 0) {
      // Tells "the push never reached the worker" from "this browser does not list what it shows": a notification shown
      // through the same registration by the page, then read back the same way
      readings.listingProbe = await session.executeAsync(`
        const [done] = arguments;
        (async () => {
          const registration = await navigator.serviceWorker.getRegistration("${pathBase}/");
          await registration.showNotification("Device pass listing probe", { body: "Shown by the page to test the listing", tag: "device-pass-probe" });
          await new Promise((resolve) => setTimeout(resolve, 1000));
          const listed = await registration.getNotifications();
          for (const notification of listed) notification.close();
          done({ listed: listed.map((notification) => notification.title) });
        })().catch((error) => done({ error: error.name + ": " + error.message }));`);
      readings.deliveryLog = readDeliveryLog(new Date(readings.testSendStartedAt ?? Date.now() - deliveryTimeoutMs - 10_000));
      // Manual only on this run's own evidence: the listing works in this session, and Safari's log shows the push event
      // fired in a worker and completed, so the push was handled outside the automation session, where no script can see
      // what was shown. Without that evidence the case fails.
      const listingWorks = Array.isArray(readings.listingProbe?.listed) && readings.listingProbe.listed.includes("Device pass listing probe");
      const evidence = readings.deliveryLog.evidence;
      if (listingWorks && evidence !== undefined && evidence.fired.length > 0 && evidence.completed.length > 0) {
        throw manual(
          `The push service delivered the test push and Safari's log shows a worker's push event fired and completed, but this automation session lists no notification. The log shows which of Safari's connections took the push (evidence.routing): in the runs so far, the one without a data store identifier, while those with one took none. Whether the notification is shown is read by a person. Evidence: ${deliveryLogFile}.`,
          { shown, listingProbe: readings.listingProbe, accountApi: readings.testSend, evidence }
        );
      }
      throw fail(`No notification was shown within ${deliveryTimeoutMs} ms.`, { shown, listingProbe: readings.listingProbe, accountApi: readings.testSend, deliveryLog: readings.deliveryLog.file ?? readings.deliveryLog });
    }
    return shown;
  });

  await check("a second test within the interval is refused", async () => {
    requireGranted();
    // The account API's own answer decides, read by the observer the previous case installed; a toast may be gone already
    await session.execute("window.__deviceTestSend = null");
    await session.click(testId("notifications-send-test")).catch((error) => {
      readings.secondTestClick = error.message;
    });
    const deadline = Date.now() + 10_000;
    let answer = null;
    while (answer === null && Date.now() < deadline) {
      answer = await session.execute("return window.__deviceTestSend");
      if (answer === null) await sleep(250);
    }
    const toasts = await session.execute(`return [...document.querySelectorAll('[data-testid$="-toast"]')].map((toast) => ({ id: toast.getAttribute("data-testid"), text: toast.innerText }))`);
    readings.secondTest = { answer, toasts };
    if (answer === null) throw fail("The second press sent no test request.", readings.secondTest);
    if (answer.status !== 429) throw fail(`The account API answered ${answer.status} to a second test within the interval.`, readings.secondTest);
    return readings.secondTest;
  });

  await check("a permission revoked while the browser keeps its subscription reads off and removes the row", async () => {
    requireGranted();
    const before = await state();
    readings.revocation = { before, setPermissionDenied: await setPermission("denied") };
    if (/not implemented|unknown command|unsupported operation/i.test(readings.revocation.setPermissionDenied)) {
      throw manual(
        "The driver cannot set a notification permission on this browser, so a revocation on the device is run by a person; the container harness covers the behaviour in push-notifications (a permission denied while the browser keeps its subscription ends it on the next visit).",
        readings.revocation
      );
    }
    if (readings.revocation.setPermissionDenied !== "accepted") throw fail("The driver refused to revoke the permission.", readings.revocation);
    await openPreferences();
    await session.waitFor(testId("notifications-notice"), 30_000);
    await sleep(2_000);
    const after = await state();
    readings.revocation.after = after;
    if (after.switchChecked !== "false") throw fail("The switch still reads on after the revocation.", readings.revocation);
    if (before.remembered !== null && after.rows.includes(before.remembered)) throw fail("The account still holds the row after the revocation.", readings.revocation);
    if (after.subscriptionHost !== null) throw fail("The browser still holds its subscription after the revocation.", readings.revocation);
    return { permission: after.permission, notice: after.notice, rowsLeft: after.rows.length };
  });

  await check("logging out deletes the row this device stored", async () => {
    requireGranted();
    await setPermission("granted");
    await openPreferences();
    if ((await state()).switchChecked !== "true") await session.click(testId("notifications-switch"));
    await waitForSwitch("true");
    const before = await state();
    if (before.remembered === null) throw fail("This device did not remember a row before the logout.", before);
    await logOut(session);
    await signIn(session, email);
    await openPreferences();
    const after = await state();
    readings.departure = { before, after };
    if (after.rows.includes(before.remembered)) throw fail("The row this device stored survived the logout.", readings.departure);
    if (after.remembered !== null) throw fail("This device still remembers a row after the logout.", readings.departure);
    return { rowDeleted: true, switchAfterSignIn: after.switchChecked };
  });
} catch (error) {
  failures.push(`The run stopped: ${error.message}`);
  console.log(`ERROR ${error.message}`);
} finally {
  await session?.close();
  driver.process.kill();
  await network.offline().catch(() => undefined);
}

const { resultFile, verdict } = writeResult(
  "safari-push.json",
  { target: targets.mac.label, publish: publishIdentity(), servedWorkerVersion: workerVersion, environment: targetEnvironment(targets.mac, session?.capabilities), readings, results, failures },
  expectedCaseCount
);
console.log(`${verdict.toUpperCase()} safari-push: ${resultFile}`);
process.exit(exitCodeFor(verdict));
