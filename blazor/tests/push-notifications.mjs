// Push notifications of the Blazor edition in one browser: the preferences section, the subscription the browser makes and
// the account API keeps, the test notification the account API sends, and what the service worker does with a push.
//
// 1. The section: it renders on the preferences page with the switch off, the test button disabled and no policy violation.
// 2. Subscribing: the switch posts the browser's subscription to the account API, the switch reports it on, the test button
//    becomes usable and this device remembers which row the account kept.
// 3. The test notification: the button posts to the account API, which answers, and the confirmation is shown.
// 4. The push event: a push delivered to the registration shows the notification the payload names, with its destination.
// 5. A payload this application did not send shows nothing, which leaves the browser's own generic message.
// 6. Unsubscribing: the switch removes the row through the account API and forgets it on this device.
// 7. A subscription revoked in the browser is removed from the account on the next visit.
// 8. A permission denied in the browser settings while the browser keeps its subscription, which Safari does, reads the
//    switch off with the blocked notice on the next visit, unsubscribes the browser and removes the account's row.
// 9. A denied permission disables the switch and says where to change it.
// 10. Logging out leaves nothing of the subscription on the device: the identifier this device stored is gone, and the next
//     account to sign in on the same browser reads the switch as off instead of the previous account's subscription.
//
// Two things this harness cannot do, and neither is worked around:
// - Chromium in the automation library has no push service it can reach: pushManager.subscribe answers "Registration
//   failed - permission denied" whatever the permission is. The three PushManager methods are therefore replaced in the
//   page with a subscription of the right shape, so every other step is the real one: the real client code, the real
//   account API, the real service worker. The delivery in case 4 goes to the registration through the DevTools protocol,
//   which is what a push service would otherwise cause.
// - A notification cannot be clicked from the automation library, so the worker's notificationclick handler, which is what
//   focuses or opens the application under the path base, is not exercised here.
//
// This script launches its own browser: notifications need the browser's full headless mode, which the shared launcher
// does not use because the other scripts measure with the headless shell.
//
// Prerequisites: the AppHost stack running through the aspire-restart skill, with the Blazor host resource started in
// Development and a VAPID key pair configured, which the AppHost generates on first start.
// Run: dotnet run --project developer-cli -- blazor-harness push-notifications --browser chromium

import {
  baseUrl,
  completeWelcomeThroughBlazor,
  newContext,
  observeErrors,
  parseArguments,
  pathBase,
  playwright,
  policyViolationsOf,
  readOneTimePassword,
  signUpThroughBlazor,
  submitOneTimePasswordThroughBlazor,
  writeResult
} from "./support/stack.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
if (options.browser !== "chromium") {
  console.log(`SKIPPED push-notifications is a Chromium case; ${options.browser} has no way to deliver a push to a worker.`);
  process.exit(0);
}

const preferencesUrl = `${baseUrl}${pathBase}/user/preferences`;
const subscriptionsPath = "/api/account/users/me/push-subscriptions";
const interactiveTimeoutMs = 60_000;
const workerTimeoutMs = 30_000;
const expectedCaseCount = 10;

// The three PushManager methods a browser without a reachable push service cannot answer. The endpoint is an address no
// push service resolves, which is what makes the account API report a test notification as undelivered rather than
// delivered to someone. The subscription is kept whatever happens to the permission afterwards, which is what Safari does
// when the permission is denied in its settings.
const pushManagerStub = () => {
  const storageKey = "__harness-push-subscription";
  const encode = (bytes) => btoa(String.fromCharCode(...new Uint8Array(bytes))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  const decode = (value) => Uint8Array.from(atob(value.replace(/-/g, "+").replace(/_/g, "/")), (character) => character.charCodeAt(0)).buffer;
  const describe = (record) => ({
    endpoint: record.endpoint,
    getKey: (name) => decode(name === "p256dh" ? record.p256dh : record.auth),
    unsubscribe: () => {
      localStorage.removeItem(storageKey);
      return Promise.resolve(true);
    }
  });

  PushManager.prototype.subscribe = async function subscribe() {
    if (Notification.permission !== "granted") throw new DOMException("Registration failed", "NotAllowedError");
    // A real P-256 point and a 16 byte secret, because the account API encrypts a payload for exactly these
    const keyPair = await crypto.subtle.generateKey({ name: "ECDH", namedCurve: "P-256" }, true, ["deriveBits"]);
    const record = {
      endpoint: `https://push.harness.invalid/${crypto.randomUUID()}`,
      p256dh: encode(await crypto.subtle.exportKey("raw", keyPair.publicKey)),
      auth: encode(crypto.getRandomValues(new Uint8Array(16)))
    };
    localStorage.setItem(storageKey, JSON.stringify(record));
    return describe(record);
  };

  PushManager.prototype.getSubscription = function getSubscription() {
    const stored = localStorage.getItem(storageKey);
    return Promise.resolve(stored === null ? null : describe(JSON.parse(stored)));
  };
};

const browser = await playwright.chromium.launch({ channel: "chromium", args: ["--ignore-certificate-errors"] });
const browserVersion = browser.version();
const results = [];
const measurements = {};
const failures = [];

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

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

const testId = (id) => `[data-testid="${id}"]`;

const signedUp = await signUpThroughBlazor(browser, options.browser, `push-${Date.now()}@platformplatform.net`);
const context = await newContext(browser, options.browser, signedUp.storageState);
await context.grantPermissions(["notifications"], { origin: baseUrl });
await context.addInitScript(pushManagerStub);
const page = await context.newPage();
const observations = observeErrors(page);

// Every account API call the section makes, so a case asserts what reached the server rather than what the page showed
const apiCalls = [];
page.on("response", (response) => {
  const url = new URL(response.url());
  if (url.pathname.startsWith(subscriptionsPath)) apiCalls.push({ method: response.request().method(), path: url.pathname, status: response.status() });
});

function callsSince(index) {
  return apiCalls.slice(index);
}

async function openPreferences() {
  await page.goto(preferencesUrl, { waitUntil: "load" });
  await page.locator(testId("app-shell")).waitFor({ timeout: interactiveTimeoutMs });
  await page.locator(testId("preferences-notifications")).waitFor({ timeout: interactiveTimeoutMs });
}

const switchLocator = () => page.locator(testId("notifications-switch"));
const testButtonLocator = () => page.locator(testId("notifications-send-test"));

async function waitForSwitch(state) {
  await page.locator(`${testId("notifications-switch")}[aria-checked="${state}"]`).waitFor({ timeout: interactiveTimeoutMs });
}

// A push can only be delivered to a worker that is activated and controls this document; a worker still installing, which
// is what a new build leaves behind, would receive nothing
async function waitForActiveWorker() {
  await page.evaluate(async (timeout) => {
    const registration = await navigator.serviceWorker.ready;
    const deadline = Date.now() + timeout;
    while ((registration.active === null || registration.active.state !== "activated" || navigator.serviceWorker.controller === null) && Date.now() < deadline) {
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    if (registration.active === null || registration.active.state !== "activated") throw new Error("No activated service worker.");
  }, workerTimeoutMs);
}

// The registration the DevTools protocol addresses when a push is delivered
async function readRegistrationId() {
  const cdp = await context.newCDPSession(page);
  const registrations = [];
  cdp.on("ServiceWorker.workerRegistrationUpdated", (event) => registrations.push(...event.registrations));
  await cdp.send("ServiceWorker.enable");
  const deadline = Date.now() + workerTimeoutMs;
  while (Date.now() + 0 < deadline) {
    const registration = registrations.find((entry) => entry.scopeURL.endsWith(`${pathBase}/`) && !entry.isDeleted);
    if (registration !== undefined) return { cdp, registrationId: registration.registrationId };
    await new Promise((resolve) => setTimeout(resolve, 200));
  }
  throw new Error("No service worker registration for the path base.");
}

function readNotifications() {
  return page.evaluate(async () => {
    const registration = await navigator.serviceWorker.ready;
    const notifications = await registration.getNotifications();
    return notifications.map((notification) => ({ title: notification.title, body: notification.body, data: notification.data }));
  });
}

function closeNotifications() {
  return page.evaluate(async () => {
    const registration = await navigator.serviceWorker.ready;
    for (const notification of await registration.getNotifications()) notification.close();
  });
}

// The delivery is fire and forget: the protocol acknowledges the message, not the worker's receipt of it, and a worker
// being woken can miss one. The message is therefore sent again once before the case gives up; what is asserted about
// what the worker shows is unchanged.
async function deliver(cdp, registrationId, payload) {
  for (let attempt = 0; attempt < 2; attempt++) {
    await cdp.send("ServiceWorker.deliverPushMessage", { origin: baseUrl, registrationId, data: payload });
    const deadline = Date.now() + workerTimeoutMs / 2;
    while (Date.now() < deadline) {
      const shown = await readNotifications();
      if (shown.length > 0) return shown;
      await new Promise((resolve) => setTimeout(resolve, 200));
    }
  }
  return readNotifications();
}

let pushSession = null;

try {
  await check("section renders with notifications off", async () => {
    await openPreferences();
    const permission = await page.evaluate(() => Notification.permission);
    assert(permission === "granted", `The browser reports the notification permission as ${permission}.`);
    await waitForSwitch("false");
    assert(await testButtonLocator().isDisabled(), "The test button is usable before anything is subscribed.");
    assert((await page.locator(testId("notifications-notice")).count()) === 0, "The section shows a notice although the permission was granted.");
    assert(policyViolationsOf(context).length === 0, "The preferences page reported a content security policy violation.");
    return { permission };
  });

  await check("the switch subscribes this browser and the account keeps it", async () => {
    const before = apiCalls.length;
    await switchLocator().click();
    await page.locator(testId("notifications-turned-on-toast")).waitFor({ timeout: interactiveTimeoutMs });
    await waitForSwitch("true");

    const calls = callsSince(before);
    const saved = calls.find((call) => call.method === "POST" && call.path === subscriptionsPath);
    assert(saved !== undefined, `No subscription was posted; the calls were ${JSON.stringify(calls)}.`);
    assert(saved.status === 200, `The account API answered ${saved.status} to the subscription.`);
    assert(!(await testButtonLocator().isDisabled()), "The test button is still disabled after subscribing.");

    const storedSubscriptionId = await page.evaluate(() => localStorage.getItem("blazor-push-subscription"));
    assert(storedSubscriptionId !== null, "This device did not remember which row the account kept.");
    measurements.subscriptionSaved = saved;
    return { status: saved.status, remembered: storedSubscriptionId.startsWith("psub_") };
  });

  await check("the test notification reaches the account API", async () => {
    const before = apiCalls.length;
    await testButtonLocator().click();
    await page.locator(testId("test-notification-sent-toast")).waitFor({ timeout: interactiveTimeoutMs });

    const sent = callsSince(before).find((call) => call.path === `${subscriptionsPath}/test`);
    assert(sent !== undefined, "The test notification was not requested.");
    assert(sent.status === 200, `The account API answered ${sent.status} to the test notification.`);
    return { status: sent.status };
  });

  await check("a push shows the notification the payload names", async () => {
    await waitForActiveWorker();
    pushSession = await readRegistrationId();
    await closeNotifications();
    const payload = JSON.stringify({ title: "Test notification", body: "Notifications are working on this device.", url: `${pathBase}/app` });

    const shown = await deliver(pushSession.cdp, pushSession.registrationId, payload);

    assert(shown.length === 1, `The worker showed ${shown.length} notifications.`);
    assert(shown[0].title === "Test notification", `The notification's title was ${shown[0].title}.`);
    assert(shown[0].body === "Notifications are working on this device.", `The notification's body was ${shown[0].body}.`);
    assert(shown[0].data?.url === `${pathBase}/app`, `The notification's destination was ${shown[0].data?.url}.`);
    measurements.pushNotification = shown[0];
    return shown[0];
  });

  await check("a payload this application did not send shows nothing", async () => {
    await closeNotifications();

    await pushSession.cdp.send("ServiceWorker.deliverPushMessage", { origin: baseUrl, registrationId: pushSession.registrationId, data: "not the shape this application sends" });
    await new Promise((resolve) => setTimeout(resolve, 2000));

    const shown = await readNotifications();
    assert(shown.length === 0, `The worker showed ${JSON.stringify(shown)}.`);
    return { shown: shown.length };
  });

  await check("the switch unsubscribes this browser and the account drops the row", async () => {
    await closeNotifications();
    const before = apiCalls.length;

    await switchLocator().click();
    await page.locator(testId("notifications-turned-off-toast")).waitFor({ timeout: interactiveTimeoutMs });
    await waitForSwitch("false");

    const removed = callsSince(before).find((call) => call.method === "DELETE");
    assert(removed !== undefined, "No subscription was removed.");
    assert(removed.status === 204 || removed.status === 200, `The account API answered ${removed.status} to the removal.`);
    assert(await testButtonLocator().isDisabled(), "The test button is still usable after unsubscribing.");
    assert((await page.evaluate(() => localStorage.getItem("blazor-push-subscription"))) === null, "This device still remembers a row the account no longer has.");
    return { status: removed.status };
  });

  await check("a subscription revoked in the browser is removed on the next visit", async () => {
    await switchLocator().click();
    await page.locator(testId("notifications-turned-on-toast")).waitFor({ timeout: interactiveTimeoutMs });
    await waitForSwitch("true");

    // What revoking notifications in the browser settings leaves behind: the account still has the row, the browser does not
    await page.evaluate(() => localStorage.removeItem("__harness-push-subscription"));
    const before = apiCalls.length;

    await openPreferences();
    await waitForSwitch("false");

    const removed = callsSince(before).find((call) => call.method === "DELETE");
    assert(removed !== undefined, `The revoked subscription was not removed; the calls were ${JSON.stringify(callsSince(before))}.`);
    assert((await page.evaluate(() => localStorage.getItem("blazor-push-subscription"))) === null, "This device still remembers the revoked row.");
    return { status: removed.status };
  });

  await check("a permission denied while the browser keeps its subscription ends it on the next visit", async () => {
    // A context of its own, because the permission this case denies cannot be granted again to a context that goes on
    const revokedContext = await newContext(browser, options.browser, signedUp.storageState);
    await revokedContext.grantPermissions(["notifications"], { origin: baseUrl });
    await revokedContext.addInitScript(pushManagerStub);
    const browserSession = await browser.newBrowserCDPSession();
    try {
      const revokedPage = await revokedContext.newPage();
      const revokedCalls = [];
      revokedPage.on("response", (response) => {
        const url = new URL(response.url());
        if (url.pathname.startsWith(subscriptionsPath)) revokedCalls.push({ method: response.request().method(), path: url.pathname, status: response.status() });
      });
      const revokedSwitch = revokedPage.locator(testId("notifications-switch"));
      const openRevokedPreferences = async () => {
        await revokedPage.goto(preferencesUrl, { waitUntil: "load" });
        await revokedPage.locator(testId("preferences-notifications")).waitFor({ timeout: interactiveTimeoutMs });
      };

      await openRevokedPreferences();
      await revokedPage.locator(`${testId("notifications-switch")}[aria-checked="false"]`).waitFor({ timeout: interactiveTimeoutMs });
      await revokedSwitch.click();
      await revokedPage.locator(testId("notifications-turned-on-toast")).waitFor({ timeout: interactiveTimeoutMs });
      await revokedPage.locator(`${testId("notifications-switch")}[aria-checked="true"]`).waitFor({ timeout: interactiveTimeoutMs });
      const storedSubscriptionId = await revokedPage.evaluate(() => localStorage.getItem("blazor-push-subscription"));
      assert(storedSubscriptionId !== null, "This device did not remember which row the account kept.");

      // What denying notifications in the browser settings does: the permission changes and the subscription stays
      const pageSession = await revokedContext.newCDPSession(revokedPage);
      const { targetInfo } = await pageSession.send("Target.getTargetInfo");
      await pageSession.detach();
      await browserSession.send("Browser.setPermission", { permission: { name: "notifications" }, setting: "denied", origin: baseUrl, browserContextId: targetInfo.browserContextId });
      const before = revokedCalls.length;

      await openRevokedPreferences();
      await revokedPage.locator(testId("notifications-notice")).waitFor({ timeout: interactiveTimeoutMs });
      await revokedPage.locator(`${testId("notifications-switch")}[aria-checked="false"]`).waitFor({ timeout: interactiveTimeoutMs });

      const permission = await revokedPage.evaluate(() => Notification.permission);
      assert(permission === "denied", `The browser reports the notification permission as ${permission}.`);
      assert(await revokedSwitch.isDisabled(), "The switch is usable although the permission is denied.");
      assert(await revokedPage.locator(testId("notifications-send-test")).isDisabled(), "The test button is usable although the permission is denied.");

      const calls = revokedCalls.slice(before);
      const removed = calls.find((call) => call.method === "DELETE" && call.path === `${subscriptionsPath}/${storedSubscriptionId}`);
      assert(removed !== undefined, `The row of the revoked subscription was not removed; the calls were ${JSON.stringify(calls)}.`);
      assert(removed.status === 204 || removed.status === 200, `The account API answered ${removed.status} to the removal.`);
      assert((await revokedPage.evaluate(() => localStorage.getItem("__harness-push-subscription"))) === null, "The browser still holds the revoked subscription.");
      assert((await revokedPage.evaluate(() => localStorage.getItem("blazor-push-subscription"))) === null, "This device still remembers the revoked row.");

      const rows = await revokedPage.evaluate(async (path) => {
        const response = await fetch(path, { headers: { accept: "application/json" } });
        return { status: response.status, ids: response.ok ? (await response.json()).subscriptions.map((subscription) => subscription.id) : [] };
      }, subscriptionsPath);
      assert(rows.status === 200, `The account API answered ${rows.status} to reading the subscriptions.`);
      assert(!rows.ids.includes(storedSubscriptionId), `The account still has the revoked row ${storedSubscriptionId}.`);
      assert(policyViolationsOf(revokedContext).length === 0, "The preferences page reported a content security policy violation.");
      return { permission, status: removed.status, rowsLeft: rows.ids.length };
    } finally {
      await browserSession.detach();
      await revokedContext.close();
    }
  });

  await check("a denied permission disables the switch and says where to change it", async () => {
    const deniedContext = await newContext(browser, options.browser, signedUp.storageState);
    await deniedContext.addInitScript(pushManagerStub);
    try {
      const deniedPage = await deniedContext.newPage();
      await deniedPage.goto(preferencesUrl, { waitUntil: "load" });
      const deniedSwitch = deniedPage.locator(testId("notifications-switch"));
      await deniedSwitch.waitFor({ timeout: interactiveTimeoutMs });

      // The permission has not been answered in this context, and a browser with no one to ask refuses it
      if (!(await deniedSwitch.isDisabled())) await deniedSwitch.click();

      const notice = deniedPage.locator(testId("notifications-notice"));
      await notice.waitFor({ timeout: interactiveTimeoutMs });
      const permission = await deniedPage.evaluate(() => Notification.permission);
      assert(permission === "denied", `The browser reports the notification permission as ${permission}.`);
      assert(await deniedSwitch.isDisabled(), "The switch is usable although the permission is denied.");
      assert((await deniedSwitch.getAttribute("aria-checked")) === "false", "The switch reports notifications as on although the permission is denied.");
      assert((await notice.innerText()).length > 0, "The section shows no sentence about the denied permission.");
      assert(policyViolationsOf(deniedContext).length === 0, "The preferences page reported a content security policy violation.");
      return { permission, notice: await notice.innerText() };
    } finally {
      await deniedContext.close();
    }
  });

  await check("logging out leaves nothing of the subscription for the next account on this browser", async () => {
    await openPreferences();
    await switchLocator().click();
    await page.locator(testId("notifications-turned-on-toast")).waitFor({ timeout: interactiveTimeoutMs });
    await waitForSwitch("true");
    const rememberedBeforeTheLogout = await page.evaluate(() => localStorage.getItem("blazor-push-subscription"));
    assert(rememberedBeforeTheLogout !== null, "This device did not remember the row before the logout.");

    await page.locator("#user-menu-trigger").click({ timeout: interactiveTimeoutMs });
    await page.locator('[role="menu"] [role="menuitem"]').last().click();
    await page.waitForURL(`${baseUrl}${pathBase}/login`, { timeout: interactiveTimeoutMs });

    const rememberedAfterTheLogout = await page.evaluate(() => localStorage.getItem("blazor-push-subscription"));
    assert(rememberedAfterTheLogout === null, "This device still remembers the row of the account that logged out.");

    // The second person on this browser, signed up in the same context so the storage the first one wrote is still there
    const secondEmail = `push-second-${Date.now()}@platformplatform.net`;
    await page.goto(`${baseUrl}${pathBase}/signup`, { waitUntil: "load" });
    await page.locator(testId("email")).fill(secondEmail);
    const sentAfter = Date.now();
    await page.locator(testId("submit")).click();
    await page.waitForURL(/\/blazor\/signup\/verify\?/, { timeout: interactiveTimeoutMs });
    await submitOneTimePasswordThroughBlazor(page, await readOneTimePassword(secondEmail, sentAfter));
    await completeWelcomeThroughBlazor(page, "Second harness account");

    await openPreferences();
    await waitForSwitch("false");

    const rememberedForTheSecondAccount = await page.evaluate(() => localStorage.getItem("blazor-push-subscription"));
    assert(rememberedForTheSecondAccount === null, "The second account inherited the identifier the first one stored.");
    assert(await testButtonLocator().isDisabled(), "The second account can send a test notification to a device it never subscribed.");

    // The browser's own subscription is unsubscribed on the way out, which is started before the document goes away and
    // is therefore reported rather than asserted; the switch above is off whether or not it finished
    const browserStillSubscribed = (await page.evaluate(() => localStorage.getItem("__harness-push-subscription"))) !== null;
    return { rememberedForTheSecondAccount, browserStillSubscribed };
  });

} finally {
  const violations = policyViolationsOf(context);
  if (violations.length > 0) failures.push(`${violations.length} content security policy violations`);
  if (observations.pageErrors.length > 0) failures.push(`${observations.pageErrors.length} page errors: ${observations.pageErrors.join(" | ")}`);
  measurements.policyViolations = violations.length;
  measurements.pageErrors = observations.pageErrors;
  await context.close();
  await browser.close();
}

const { resultFile, passed } = writeResult(
  `push-notifications-${options.browser}.json`,
  { browser: options.browser, browserVersion, baseUrl, startedAt: new Date().toISOString(), results, measurements, failures },
  expectedCaseCount
);

console.log(`${passed ? "PASSED" : "FAILED"} ${results.filter((entry) => entry.passed).length} of ${results.length} cases, result ${resultFile}`);
process.exit(passed ? 0 : 1);
