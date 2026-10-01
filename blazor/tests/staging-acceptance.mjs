// The staging acceptance of the Blazor back office in one command: the checks of "Deployment procedure for G6b" in
// docs/blazor-back-office-hosting.md, run against the deployed hosts with the owner's stored sessions, with every request
// and answer written to one result file. The owner's part is the interactive sign-ins and the approval of the probe job.
//
// Order, each step recorded:
// - Sessions: the admin identity (the owner's Entra account, signed in to the back office and to the app edition as the
//   admin write's target user) and the non-admin identity (the owner's outside account). A missing or expired session stops
//   the run before any check, says which, and offers the interactive sign-in; it is never a failed check.
// - The image and fingerprint gate, before every other check: each of account-api, back-office, account-workers and
//   blazor-host runs the tag being verified on every active revision, and the documents the app host and the back-office
//   host serve reference only fingerprinted assets of that tag's image. A mismatch fails the run, naming the app and image.
// - Checks 1 to 7, 5b, 9, 10 and 11, and every back-office surface and app page at desktop and phone width.
// - Check 8, the admin write through the Blazor back office: a user override of the flag on the target user, seen on the
//   app edition's preferences page, the same write refused with 403 for the non-admin, then the state before restored
//   exactly, also when a step fails or the run is interrupted (a pending-restore file outside the repository).
// - The non-admin checks: no write offered on the flag detail, and check 3 under that identity.
// - Checks 3b and 3c from a Container Apps job created from staging/probe-job.yaml and deleted again, only after the owner
//   approves that run on the terminal; without the approval they are recorded as not run.
//
// Sign in once per identity (stored outside the repository, reused until it expires):
//   dotnet run --project developer-cli -- blazor-harness staging-acceptance --tag <tag> --resource-group <rg> \
//     --subscription "<subscription>" --sign-in admin
// then the same with --sign-in non-admin. Run the checks:
//   dotnet run --project developer-cli -- blazor-harness staging-acceptance --tag <tag> --resource-group <rg> \
//     --subscription "<subscription>"
// Options: --app-user <email> (the admin write's target, default jasper@etara.dk), --tenant-id <id> (when that user has more
// than one account), --flag <key> (default experimental-ui), --session-folder <folder>, --desktop-port <port> (6080, the browser desktop for a sign-in),
// --without-write (check 8 is recorded as not run, for a first run that proves the read-only checks).
// Exit codes: 0 every check passed, 1 a check failed or did not run, 3 a sign-in is needed first.

import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { ask, captureSession, createAzure, identities, reconcileAppSession, recordResponse, sessionStore, verifyStoredSession } from "./support/deployed.mjs";
import { newContext, parseArguments, pathBase, playwright, readEndpointManifest, redact, repositoryRoot, writeResult } from "./support/stack.mjs";
import { appSurfaces, backOfficeSurfaces, checkSurfaces, readBackOfficeIds, settleAndClosePages, surfaceViewports } from "./support/surfaces.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium", "app-user": "jasper@etara.dk", flag: "experimental-ui", "desktop-port": "6080" });
for (const required of ["tag", "resource-group", "subscription"]) {
  if (typeof options[required] !== "string") throw new Error(`Name the --${required}.`);
}
const resourceGroup = options["resource-group"];
const appUserEmail = options["app-user"];
const flagKey = options.flag;
const azure = createAzure(options.subscription);
const interactiveTimeoutMs = 90_000;

// The apps the deployment procedure deploys, and the image repository each runs (back-office runs the account API image)
const deployedApps = { "account-api": "account-api", "back-office": "account-api", "account-workers": "account-workers", "blazor-host": "blazor-host" };
// Paths the Blazor host generates when it runs rather than publishes, so they are not in the image's endpoint manifest:
// the brand stylesheet and the web manifest (HostApplication) and the framework's resource collection. Observed on
// 2026-09-30 as the only references of the served documents outside the manifest of 2026.09.30.1815.
const hostGeneratedPaths = [/^brand\.css$/, /^manifest\.webmanifest$/, /^_framework\/resource-collection(\.[a-z0-9]+)?\.js(\.gz)?$/];

// ---------------------------------------------------------------------------------------------------------------------
// The deployment as Azure describes it (read only)

const readApp = (name) => azure.json(["containerapp", "show", "--name", name, "--resource-group", resourceGroup]);
const apps = Object.fromEntries(Object.keys(deployedApps).map((name) => [name, readApp(name)]));
const environmentOf = (app) => Object.fromEntries((app.properties.template.containers[0].env ?? []).map((variable) => [variable.name, variable.value ?? `secretRef:${variable.secretRef}`]));
const variables = Object.fromEntries(Object.entries(apps).map(([name, app]) => [name, environmentOf(app)]));
const backOfficeHost = apps["back-office"].properties.configuration.ingress.customDomains?.[0]?.name;
if (!backOfficeHost) throw new Error("The back-office app has no custom domain.");
const backOfficeUrl = `https://${backOfficeHost}`;
const appUrl = variables["blazor-host"].PUBLIC_URL;
const managedEnvironment = azure.json(["containerapp", "env", "show", "--ids", apps["back-office"].properties.managedEnvironmentId]);
const environmentDomain = managedEnvironment.properties.defaultDomain;

const store = sessionStore(options["session-folder"], backOfficeHost);
const target = { backOfficeUrl, appUrl, appUserEmail };
const desktopPort = Number(options["desktop-port"]);
const signInCommand = (identity) =>
  `dotnet run --project developer-cli -- blazor-harness staging-acceptance --tag ${options.tag} --resource-group ${resourceGroup} --subscription "${options.subscription}" --sign-in ${identity}`;

if (typeof options["sign-in"] === "string") {
  if (!identities.includes(options["sign-in"])) throw new Error(`--sign-in takes ${identities.join(" or ")}.`);
  await captureSession(store, options["sign-in"], { ...target, desktopPort });
  process.exit(0);
}

// ---------------------------------------------------------------------------------------------------------------------
// Sessions: every identity signs in before any check runs

const sessions = {};
for (const identity of identities) {
  let verdict = await verifyStoredSession(store, identity, target);
  if (!verdict.valid) {
    console.log(`The ${identity} session cannot be used: ${verdict.reason}.`);
    const answer = await ask(`Sign in as ${identity} now? [y/N] `);
    if (answer?.toLowerCase() === "y") {
      await captureSession(store, identity, { ...target, desktopPort });
      verdict = await verifyStoredSession(store, identity, target);
    }
  }
  if (!verdict.valid) {
    console.log(`No check ran. Sign in as ${identity} first:\n  ${signInCommand(identity)}`);
    process.exit(3);
  }
  sessions[identity] = verdict;
  console.log(`Session ${identity}: ${verdict.me.email}${verdict.appUser ? `, app edition ${verdict.appUser.email}` : ""}.`);
}

const stateOf = (identity) => store.load(identity);
const keep = (identity) => async (state) => store.save(identity, state);

// The browser stays open on an interrupt, so the handler below can still restore the admin write's target
const browser = await playwright[options.browser].launch({ handleSIGINT: false, handleSIGTERM: false });
const browserVersion = browser.version();

// A context of the identity, or of nobody; closing it stores the cookies it ended with, since the account API rotates the
// refresh token and revokes a session whose previous token comes back
async function openContext(identity, contextOptions = {}) {
  const context = await newContext(browser, options.browser, identity ? stateOf(identity) : undefined, "en-US", { ignoreHTTPSErrors: false, ...contextOptions });
  return {
    context,
    close: async () => {
      await settleAndClosePages(context);
      if (identity === "admin") await reconcileAppSession(context, appUrl);
      if (identity) store.save(identity, await context.storageState());
      await context.close();
    }
  };
}

async function withPage(identity, url, action) {
  const { context, close } = await openContext(identity);
  try {
    const page = await context.newPage();
    const response = await page.goto(url, { waitUntil: "load" });
    return await action(page, response, context);
  } finally {
    await close();
  }
}

async function waitForBackOffice(page) {
  await page.locator('[data-testid="back-office-shell"][data-identity-state="loaded"]').waitFor({ timeout: interactiveTimeoutMs });
}

// A request as the identity, or with no session at all, recorded with its answer
async function send(identity, method, url, { headers = {}, data } = {}) {
  const request = await playwright.request.newContext({ storageState: identity ? stateOf(identity) : undefined });
  try {
    const response = await request.fetch(url, { method, headers, data, maxRedirects: 0, failOnStatusCode: false });
    const recorded = await recordResponse(method, url, response, headers);
    return { status: response.status(), headers: response.headers(), body: await response.text(), recorded };
  } finally {
    if (identity) store.save(identity, await request.storageState());
    await request.dispose();
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// The record

const checks = [];
async function check(id, name, action) {
  const requests = [];
  try {
    const detail = await action(requests);
    checks.push({ id, name, passed: true, detail, requests });
    console.log(`PASS ${id} ${name}`);
  } catch (error) {
    checks.push({ id, name, passed: false, detail: redact(error.message), requests });
    console.log(`FAIL ${id} ${name}: ${redact(error.message)}`);
  }
}

function notRun(id, name, reason) {
  checks.push({ id, name, passed: false, notRun: true, detail: reason, requests: [] });
  console.log(`NOT RUN ${id} ${name}: ${reason}`);
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function finish(extra = {}) {
  const hostConfiguration = { hostEnvironment: "Production", buildConfiguration: `staging images ${options.tag}` };
  const verdict = writeResult(
    `staging-acceptance-${options.tag}-${options.browser}-${new Date().toISOString().replaceAll(":", "").slice(0, 15)}.json`,
    {
      browser: options.browser,
      browserVersion,
      culture: "en-US",
      ...hostConfiguration,
      tag: options.tag,
      resourceGroup,
      subscription: options.subscription,
      backOfficeUrl,
      appUrl,
      identities: Object.fromEntries(Object.entries(sessions).map(([identity, session]) => [identity, { backOffice: session.me.email, isAdmin: session.me.isAdmin, appEdition: session.appUser?.email ?? null }])),
      ...extra,
      checks
    },
    1
  );
  console.log(`Result file: ${verdict.resultFile}`);
  return verdict;
}

// ---------------------------------------------------------------------------------------------------------------------
// The admin write's restore, which also runs for a leftover from an interrupted run

async function readUserFlag(userId, tenantId) {
  const answer = await send("admin", "GET", `${backOfficeUrl}/api/back-office/users/${encodeURIComponent(userId)}/feature-flags`, { headers: { Accept: "application/json" } });
  assert(answer.status === 200, `GET the user's flags answered ${answer.status}.`);
  const entry = JSON.parse(answer.body).flags.find((flag) => flag.flagKey === flagKey && String(flag.tenantId) === String(tenantId));
  assert(entry !== undefined, `The user ${userId} has no ${flagKey} row for account ${tenantId}.`);
  return { isEnabled: entry.isEnabled, source: entry.source };
}

const sameFlagState = (left, right) => left.isEnabled === right.isEnabled && left.source === right.source;

// The flag detail's users list, searched for the target user, and the row keys (user id and account id) of that user
async function openOverrideList(page) {
  await page.goto(`${backOfficeUrl}${pathBase}/back-office/feature-flags/${encodeURIComponent(flagKey)}`, { waitUntil: "load" });
  await waitForBackOffice(page);
  // The search box is a FluentTextInput, whose input sits in its shadow root; the role query reaches it, as in trimmed-smoke
  await page.locator('[data-testid="feature-flag-users-toolbar"]').getByRole("textbox", { name: "Search users" }).fill(appUserEmail);
  await page.waitForFunction(
    (email) => {
      const list = document.querySelector('.data-list[data-testid="feature-flag-users-grid"]');
      const rows = [...document.querySelectorAll('[data-testid^="feature-flag-user-override-"]')].filter((element) => element.closest("tr")?.textContent?.includes(email));
      return list?.getAttribute("data-list-state") !== "loading" && rows.length > 0;
    },
    appUserEmail,
    { timeout: interactiveTimeoutMs }
  );
  return page.locator('[data-testid^="feature-flag-user-override-"]').evaluateAll(
    (switches, email) =>
      switches
        .filter((element) => element.closest("tr")?.textContent?.includes(email) && !element.getAttribute("data-testid").endsWith("-manual"))
        .map((element) => element.getAttribute("data-testid").replace("feature-flag-user-override-", "")),
    appUserEmail
  );
}

// The switch of the one row of the target's account on the flag detail's users list
async function openOverrideRow(page, userId, tenantId) {
  await openOverrideList(page);
  const toggle = page.locator(`[data-testid="feature-flag-user-override-${userId}-${tenantId}"]`);
  await toggle.waitFor({ timeout: interactiveTimeoutMs });
  return toggle;
}

async function waitForFlagChange(userId, tenantId, from) {
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline) {
    const current = await readUserFlag(userId, tenantId);
    if (!sameFlagState(current, from)) return current;
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  throw new Error(`The ${flagKey} override of ${userId} did not change from ${JSON.stringify(from)} within 30 s.`);
}

// Puts the target's override back to the recorded state through the same UI: a removed override when there was none, the
// switch otherwise, whose press follows FeatureFlagOverrides.GetSwitchChange. Bounded, and verified against the API.
async function restoreOverride(pending) {
  const { userId, tenantId, before } = pending;
  const { context, close } = await openContext("admin");
  try {
    const page = await context.newPage();
    for (let attempt = 0; attempt < 4; attempt++) {
      const current = await readUserFlag(userId, tenantId);
      if (sameFlagState(current, before)) {
        store.clearPendingRestore();
        return current;
      }
      const toggle = await openOverrideRow(page, userId, tenantId);
      if (before.source !== "manual_override" && current.source === "manual_override") {
        const row = page.locator("tr", { has: toggle });
        await row.locator('[data-testid="feature-flag-user-actions"]').click();
        await page.locator('[data-testid="feature-flag-user-remove-override"]').click();
      } else {
        await toggle.click();
      }
      await waitForFlagChange(userId, tenantId, current);
    }
    const current = await readUserFlag(userId, tenantId);
    assert(sameFlagState(current, before), `The ${flagKey} override of ${userId} in account ${tenantId} is ${JSON.stringify(current)}, not the recorded ${JSON.stringify(before)}.`);
    store.clearPendingRestore();
    return current;
  } finally {
    await close();
  }
}

// An interrupt while the override is changed restores it before the process ends; the pending-restore file covers a run
// that is killed outright, and the next run restores it first
let interrupted = false;
for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, async () => {
    if (interrupted) process.exit(130);
    interrupted = true;
    const pending = store.readPendingRestore();
    if (pending !== undefined) {
      console.log(`Interrupted: restoring the ${pending.flagKey} override of ${pending.email} to ${JSON.stringify(pending.before)}...`);
      try {
        console.log(`Restored: ${JSON.stringify(await restoreOverride(pending))}.`);
      } catch (error) {
        console.log(`The restore failed: ${error.message}. The state to restore is in ${store.pendingRestoreFile}; the next run restores it first.`);
      }
    }
    await browser.close().catch(() => undefined);
    process.exit(130);
  });
}

const leftover = store.readPendingRestore();
if (leftover !== undefined) {
  console.log(`An earlier run left the ${leftover.flagKey} override of ${leftover.email} changed; the state before it was ${JSON.stringify(leftover.before)}.`);
  const answer = await ask("Restore it now, before the checks? [y/N] ");
  if (answer?.toLowerCase() !== "y") {
    console.log(`No check ran. The state to restore is in ${store.pendingRestoreFile}.`);
    await browser.close();
    process.exit(1);
  }
  console.log(`Restored: ${JSON.stringify(await restoreOverride(leftover))}.`);
}

// ---------------------------------------------------------------------------------------------------------------------
// The image and fingerprint gate

const imageTag = (image) => image.slice(image.lastIndexOf(":") + 1);
const imageRepository = (image) => image.slice(image.indexOf("/") + 1, image.lastIndexOf(":"));

function readImageManifest(image) {
  const registry = image.slice(0, image.indexOf("/"));
  azure.raw(["acr", "login", "--name", registry.split(".")[0]]);
  execFileSync("docker", ["pull", "--quiet", image], { stdio: "ignore" });
  const container = execFileSync("docker", ["create", image], { encoding: "utf8" }).trim();
  const folder = mkdtempSync(path.join(os.tmpdir(), "staging-acceptance-"));
  try {
    const file = path.join(folder, "endpoints.json");
    execFileSync("docker", ["cp", `${container}:/app/Blazor.Host.staticwebassets.endpoints.json`, file], { stdio: "ignore" });
    return new Set(readEndpointManifest(file).map((endpoint) => endpoint.Route));
  } finally {
    execFileSync("docker", ["rm", container], { stdio: "ignore" });
    rmSync(folder, { recursive: true, force: true });
  }
}

// Every asset path below the path base that a document references: attributes, the import map and the preload list
function referencedAssets(html) {
  const assets = new Set();
  const pattern = /["'](?:\/blazor\/|\.\/)?((?:_framework|_content|js|css|fonts|images)\/[^"'?#\s]+|[A-Za-z0-9._-]+\.(?:css|js|webmanifest|ico|png|svg))(?:\?[^"'\s]*)?["']/g;
  for (const match of html.matchAll(pattern)) assets.add(match[1]);
  return [...assets];
}

let gatePassed = false;
await check("gate", `Every app runs ${options.tag} and the documents reference that publish's assets`, async (requests) => {
  const failures = [];
  const revisions = {};
  for (const [name, repository] of Object.entries(deployedApps)) {
    const active = azure.json(["containerapp", "revision", "list", "--name", name, "--resource-group", resourceGroup]).filter((revision) => revision.properties.active);
    revisions[name] = active.map((revision) => ({
      name: revision.name,
      image: revision.properties.template.containers[0].image,
      trafficWeight: revision.properties.trafficWeight ?? null,
      healthState: revision.properties.healthState,
      runningState: revision.properties.runningState
    }));
    if (active.length === 0) failures.push(`${name} has no active revision`);
    for (const revision of revisions[name]) {
      if (imageTag(revision.image) !== options.tag || imageRepository(revision.image) !== repository) failures.push(`${name} revision ${revision.name} runs ${revision.image}, not ${repository}:${options.tag}`);
      if (revision.healthState !== "Healthy") failures.push(`${name} revision ${revision.name} is ${revision.healthState}`);
    }
  }
  assert(failures.length === 0, failures.join("; "));

  const image = revisions["blazor-host"][0].image;
  const routes = readImageManifest(image);
  const appCss = [...routes].find((route) => /^app\.[a-z0-9]+\.css$/.test(route));
  const blazorWeb = [...routes].find((route) => /^_framework\/blazor\.web\.[a-z0-9]+\.js$/.test(route));
  const documents = {};
  for (const [label, identity, url] of [
    ["app host", null, `${appUrl}${pathBase}/`],
    ["back-office host", "admin", `${backOfficeUrl}${pathBase}/back-office`]
  ]) {
    const answer = await send(identity, "GET", url, { headers: { Accept: "text/html" } });
    requests.push(answer.recorded);
    if (answer.status !== 200) {
      failures.push(`${label}: ${url} answered ${answer.status}`);
      continue;
    }
    const assets = referencedAssets(answer.body);
    const foreign = assets.filter((asset) => !routes.has(asset) && !hostGeneratedPaths.some((pattern) => pattern.test(asset)));
    documents[label] = { url, assets: assets.length, notInImage: foreign };
    if (foreign.length > 0) failures.push(`${label}: ${url} references ${foreign.join(", ")}, which the image ${image} does not contain`);
    if (!assets.includes(appCss)) failures.push(`${label}: ${url} does not reference ${appCss} of ${image}`);
    if (!assets.includes(blazorWeb)) failures.push(`${label}: ${url} does not reference ${blazorWeb} of ${image}`);
  }
  assert(failures.length === 0, failures.join("; "));
  gatePassed = true;
  return { revisions, image, imageRoutes: routes.size, appCss, blazorWeb, documents };
});

if (!gatePassed) {
  notRun("rest", "Every other check", "the image and fingerprint gate failed");
  await browser.close();
  const verdict = finish();
  process.exit(verdict.passed ? 0 : 1);
}

// ---------------------------------------------------------------------------------------------------------------------
// Read-only checks

const admin = sessions.admin.me;
const nonAdmin = sessions["non-admin"].me;
const adminsGroupId = variables["back-office"].BackOffice__AdminsGroupId;
// A principal naming another identity with the admins group, which no request may turn into that identity (check 3)
const forged = {
  name: "forged-admin@example.com",
  id: "00000000-0000-4000-8000-0000000000f0",
  get principal() {
    const claims = [
      { typ: "name", val: this.name },
      { typ: "http://schemas.microsoft.com/identity/claims/objectidentifier", val: this.id },
      { typ: "groups", val: adminsGroupId }
    ];
    return Buffer.from(JSON.stringify({ auth_typ: "aad", name_typ: "name", role_typ: "roles", claims })).toString("base64");
  },
  get headers() {
    return { "X-MS-CLIENT-PRINCIPAL-NAME": this.name, "X-MS-CLIENT-PRINCIPAL-ID": this.id, "X-MS-CLIENT-PRINCIPAL": this.principal };
  }
};

await check("1", "Host: one back-office host name everywhere, and /me answers the session's identity", async (requests) => {
  const publicHost = new URL(variables["blazor-host"].BACK_OFFICE_PUBLIC_URL).host;
  const configuredHost = variables["back-office"].BackOffice__Host;
  assert(publicHost === backOfficeHost && configuredHost === backOfficeHost, `Custom domain ${backOfficeHost}, BACK_OFFICE_PUBLIC_URL ${publicHost}, BackOffice__Host ${configuredHost}.`);
  const answer = await send("admin", "GET", `${backOfficeUrl}/api/back-office/me`, { headers: { Accept: "application/json" } });
  requests.push(answer.recorded);
  assert(answer.status === 200 && JSON.parse(answer.body).email === admin.email, `/me answered ${answer.status}.`);
  return { host: backOfficeHost, me: JSON.parse(answer.body).email };
});

await check("2", "Shared key ring: the identity page renders the signed-in admin", () =>
  withPage("admin", `${backOfficeUrl}${pathBase}/back-office/identity`, async (page, response) => {
    assert(response.status() === 200, `The identity page answered ${response.status()}.`);
    const name = (await page.locator('[data-testid="back-office-name"]').textContent())?.trim();
    const marker = (await page.locator('[data-testid="back-office-admin-marker"]').textContent())?.trim();
    assert(name === admin.displayName && marker === "Admin", `The identity page shows ${name} and ${marker}.`);
    return { name, marker };
  })
);

// Check 3: the forged headers from a signed-in page of each identity, then with no session at all
async function forgedFromPage(identity, me, expectedMarker) {
  return withPage(identity, `${backOfficeUrl}${pathBase}/back-office`, async (page) => {
    const answers = await page.evaluate(async (headers) => {
      const meAnswer = await fetch("/api/back-office/me", { headers: { ...headers, Accept: "application/json" } });
      const pageAnswer = await fetch("/blazor/back-office/identity", { headers });
      return { me: { status: meAnswer.status, body: await meAnswer.text() }, page: { status: pageAnswer.status, body: await pageAnswer.text() } };
    }, forged.headers);
    const answeredMe = JSON.parse(answers.me.body);
    const document = await page.evaluate((html) => {
      const parsed = new DOMParser().parseFromString(html, "text/html");
      return {
        name: parsed.querySelector('[data-testid="back-office-name"]')?.textContent?.trim(),
        marker: parsed.querySelector('[data-testid="back-office-admin-marker"]')?.textContent?.trim()
      };
    }, answers.page.body);
    assert(answers.me.status === 200 && answeredMe.email === me.email && answeredMe.isAdmin === me.isAdmin, `/me with forged headers answered ${answers.me.status} for ${answeredMe.email} (admin: ${answeredMe.isAdmin}).`);
    assert(answers.page.status === 200 && document.name === me.displayName && document.marker === expectedMarker, `The identity page with forged headers shows ${document.name} and ${document.marker}.`);
    return { me: { email: answeredMe.email, isAdmin: answeredMe.isAdmin }, page: document };
  });
}

await check("3", "Principal header overwrite: forged headers never change the identity, and need a session", async (requests) => {
  const asAdmin = await forgedFromPage("admin", admin, "Admin");
  const asNonAdmin = await forgedFromPage("non-admin", nonAdmin, "Not admin");
  const noSession = [];
  for (const accept of ["text/html", "application/json"]) {
    const answer = await send(null, "GET", `${backOfficeUrl}/api/back-office/me`, { headers: { ...forged.headers, Accept: accept } });
    requests.push(answer.recorded);
    noSession.push({ accept, status: answer.status, location: answer.headers.location ?? null });
    assert([302, 401].includes(answer.status), `With no session and Accept ${accept}, /me answered ${answer.status}.`);
    if (answer.status === 302) assert(answer.headers.location?.includes("/.auth/login/aad"), `The redirect goes to ${answer.headers.location}.`);
  }
  return { admin: asAdmin, nonAdmin: asNonAdmin, noSession };
});

await check("4", "App path refuses: the back office is not served on the app host", async (requests) => {
  const variants = [
    { label: "forged principal", headers: forged.headers },
    { label: "forged principal with X-Forwarded-Host", headers: { ...forged.headers, "X-Forwarded-Host": backOfficeHost } },
    { label: "made-up X-Back-Office-Identity", headers: { "X-Back-Office-Identity": forged.principal } }
  ];
  const statuses = {};
  for (const variant of variants) {
    const answer = await send(null, "GET", `${appUrl}${pathBase}/back-office`, { headers: { ...variant.headers, Accept: "text/html" } });
    requests.push(answer.recorded);
    statuses[variant.label] = answer.status;
  }
  assert(Object.values(statuses).every((status) => status === 404), `Statuses: ${JSON.stringify(statuses)}.`);
  return statuses;
});

await check("5", "Internal ingress stays internal", async (requests) => {
  const external = apps["blazor-host"].properties.configuration.ingress.external;
  assert(external === false, `blazor-host ingress external is ${external}.`);
  const url = `https://blazor-host.${environmentDomain}/`;
  let outcome;
  try {
    const answer = await send(null, "GET", url, { headers: { Accept: "text/html" } });
    requests.push(answer.recorded);
    outcome = { status: answer.status, fromApp: answer.body.includes("/blazor/_framework/") };
  } catch (error) {
    outcome = { status: null, error: error.message.split("\n")[0] };
  }
  assert(outcome.status !== 200 && !outcome.fromApp, `${url} answered from outside: ${JSON.stringify(outcome)}.`);
  return { external, outside: outcome };
});

await check("5b", "The proxy stays on back-office", () => {
  const values = {
    "account-api BACK_OFFICE_BLAZOR_HOST_URL": variables["account-api"].BACK_OFFICE_BLAZOR_HOST_URL ?? null,
    "back-office BackOffice__IsBackOfficeContainer": variables["back-office"].BackOffice__IsBackOfficeContainer ?? null,
    "account-api BackOffice__IsBackOfficeContainer": variables["account-api"].BackOffice__IsBackOfficeContainer ?? null
  };
  assert(
    values["account-api BACK_OFFICE_BLAZOR_HOST_URL"] === null && values["back-office BackOffice__IsBackOfficeContainer"] === "true" && values["account-api BackOffice__IsBackOfficeContainer"] === null,
    `Values: ${JSON.stringify(values)}.`
  );
  return values;
});

await check("6", "Content security policy: one nonce policy naming the back-office origin only", async (requests) => {
  const answer = await send("admin", "GET", `${backOfficeUrl}${pathBase}/back-office`, { headers: { Accept: "text/html" } });
  requests.push(answer.recorded);
  const policies = answer.headers["content-security-policy"]?.split("\n") ?? [];
  const policy = policies[0] ?? "";
  const failures = [];
  if (policies.length !== 1) failures.push(`${policies.length} policy headers`);
  if (!/script-src-elem [^;]*'nonce-/.test(policy)) failures.push("no nonce source");
  if (policy.includes("'unsafe-inline'")) failures.push("'unsafe-inline'");
  if (policy.includes("style-src-attr")) failures.push("style-src-attr");
  // Whole sources, since the app host is a suffix of the back-office host (staging.ppdemo.etara.dk in back-office.staging...)
  const sources = policy.split(/[;\s]+/);
  const appOrigin = new URL(appUrl).origin;
  if (!sources.some((source) => source === backOfficeUrl || source.startsWith(`${backOfficeUrl}/`))) failures.push(`no ${backOfficeUrl}`);
  if (sources.some((source) => source === appOrigin || source.startsWith(`${appOrigin}/`))) failures.push(`names ${appOrigin}`);
  assert(failures.length === 0, failures.join("; "));
  return { policy: policy.replace(/'nonce-[^']+'/g, "'nonce-...'") };
});

await check("7", "Offline shell, push and version policy stay off the back office", async (requests) => {
  const statuses = {};
  for (const file of ["service-worker.js", "manifest.webmanifest"]) {
    const answer = await send("admin", "GET", `${backOfficeUrl}${pathBase}/${file}`);
    requests.push(answer.recorded);
    statuses[file] = answer.status;
  }
  const page = await withPage("admin", `${backOfficeUrl}${pathBase}/back-office`, async (loaded) => {
    await waitForBackOffice(loaded);
    await loaded.waitForTimeout(2_000);
    return loaded.evaluate(async () => ({
      backOfficeAttribute: document.documentElement.hasAttribute("data-back-office"),
      workers: (await navigator.serviceWorker.getRegistrations()).map((registration) => registration.scope)
    }));
  });
  assert(statuses["service-worker.js"] === 404 && statuses["manifest.webmanifest"] === 404, `Statuses: ${JSON.stringify(statuses)}.`);
  assert(page.backOfficeAttribute && page.workers.length === 0, `Page: ${JSON.stringify(page)}.`);
  return { statuses, ...page };
});

await check("9", "Subscription setting: equal values, and the billing tabs show in both back offices exactly when it is on", async () => {
  const blazorSetting = variables["blazor-host"].BACK_OFFICE_SUBSCRIPTION_ENABLED;
  const reactSetting = variables["back-office"].PUBLIC_SUBSCRIPTION_ENABLED;
  assert(blazorSetting === reactSetting, `BACK_OFFICE_SUBSCRIPTION_ENABLED ${blazorSetting}, PUBLIC_SUBSCRIPTION_ENABLED ${reactSetting}.`);
  const tenantId = await withPage("admin", `${backOfficeUrl}${pathBase}/back-office/identity`, (page) => readBackOfficeIds(page).then((ids) => ids.tenantId));
  const blazorTabs = await withPage("admin", `${backOfficeUrl}${pathBase}/back-office/accounts/${tenantId}`, async (page) => {
    await page.locator('[data-testid="account-tabs"] a').first().waitFor({ timeout: interactiveTimeoutMs });
    return page.locator('[data-testid="account-tabs"] a').evaluateAll((links) => links.map((link) => new URL(link.href).searchParams.get("tab") ?? "overview"));
  });
  const reactTabs = await withPage("admin", `${backOfficeUrl}/accounts/${tenantId}`, async (page) => {
    await page.getByRole("tab").first().waitFor({ timeout: interactiveTimeoutMs });
    return page.getByRole("tab").allTextContents();
  });
  const expectedCount = blazorSetting === "true" ? 5 : 3;
  assert(blazorTabs.length === expectedCount && reactTabs.length === expectedCount, `Setting ${blazorSetting}: Blazor tabs ${blazorTabs.join(", ")}; React tabs ${reactTabs.join(", ")}.`);
  return { setting: blazorSetting, tenantId, blazorTabs, reactTabs };
});

await check("10", "Nothing else moved: the React back office at the root and the app edition's landing page", async (requests) => {
  const react = await send("admin", "GET", `${backOfficeUrl}/`, { headers: { Accept: "text/html" } });
  const landing = await send(null, "GET", `${appUrl}${pathBase}/`, { headers: { Accept: "text/html" } });
  requests.push(react.recorded, landing.recorded);
  assert(react.status === 200 && !react.body.includes("data-back-office"), `The back-office root answered ${react.status}${react.body.includes("data-back-office") ? " with the Blazor document" : ""}.`);
  assert(landing.status === 200 && landing.body.includes("/blazor/_framework/blazor.web."), `The app landing page answered ${landing.status}.`);
  return { backOfficeRoot: react.status, appLanding: landing.status };
});

await check("11", "Assets under load: every framework file three times in parallel, and no forwarder error logged", async () => {
  const loaded = await withPage("admin", `${backOfficeUrl}${pathBase}/back-office`, async (page) => {
    await waitForBackOffice(page);
    return page.evaluate(async () => {
      const files = [...new Set(performance.getEntriesByType("resource").map((entry) => entry.name).filter((name) => /\/blazor\/_framework\/[^/]+\.wasm(\?|$)/.test(name)))];
      const fetchOnce = async (url) => {
        try {
          const response = await fetch(url, { cache: "no-store" });
          return { url, status: response.status, bytes: (await response.arrayBuffer()).byteLength };
        } catch (error) {
          return { url, status: 0, bytes: 0, error: String(error) };
        }
      };
      return { files: files.length, answers: await Promise.all(files.flatMap((url) => [fetchOnce(url), fetchOnce(url), fetchOnce(url)])) };
    });
  });
  const failed = loaded.answers.filter((answer) => answer.status !== 200 || answer.bytes === 0);
  const sizes = new Map();
  for (const answer of loaded.answers) sizes.set(answer.url, [...(sizes.get(answer.url) ?? []), answer.bytes]);
  const cut = [...sizes].filter(([, bytes]) => new Set(bytes).size !== 1).map(([url]) => url);
  const log = azure.raw(["containerapp", "logs", "show", "--name", "back-office", "--resource-group", resourceGroup, "--tail", "300", "--format", "text"]);
  const forwarderErrors = log.split("\n").filter((line) => line.includes("HttpForwarder[48]"));
  assert(loaded.files > 0, "The page loaded no framework file.");
  assert(failed.length === 0 && cut.length === 0 && forwarderErrors.length === 0, `Failed: ${failed.length}, differing sizes: ${cut.length}, forwarder errors: ${forwarderErrors.length}.`);
  return { files: loaded.files, requests: loaded.answers.length, allAnswered200WithBody: true, logLinesRead: log.split("\n").length, forwarderErrors: 0 };
});

// Every back-office surface and app page at both widths, as the admin identity, on the deployed hosts
const surfaceIds = await withPage("admin", `${backOfficeUrl}${pathBase}/back-office/identity`, (page) => readBackOfficeIds(page));
const surfaceResults = [
  ...(await checkSurfaces({ browser, browserName: options.browser, origin: backOfficeUrl, storageState: stateOf("admin"), contextOptions: { ignoreHTTPSErrors: false }, onStorageState: keep("admin") }, backOfficeSurfaces(surfaceIds))),
  ...(await checkSurfaces(
    { browser, browserName: options.browser, origin: appUrl, storageState: stateOf("admin"), contextOptions: { ignoreHTTPSErrors: false }, beforeSnapshot: (context) => reconcileAppSession(context, appUrl), onStorageState: keep("admin") },
    appSurfaces()
  ))
];
for (const result of surfaceResults) checks.push({ id: "surfaces", name: result.name, passed: result.passed, detail: result.detail, requests: [] });

// ---------------------------------------------------------------------------------------------------------------------
// Check 8 and the non-admin checks

async function readPreferencesSwitch() {
  return withPage("admin", `${appUrl}${pathBase}/user/preferences`, async (page) => {
    await page.locator('[data-testid="app-shell"][data-tenants-state="loaded"]').waitFor({ timeout: interactiveTimeoutMs });
    const flagSwitch = page.locator(`[data-testid="preferences-feature-flags"] [data-testid="feature-flag-${flagKey}"]`);
    await flagSwitch.waitFor({ timeout: interactiveTimeoutMs });
    return flagSwitch.getAttribute("aria-checked");
  });
}

const writeCheckName = `Antiforgery write: the admin sets a ${flagKey} override on ${appUserEmail}, the app edition shows it, the non-admin is refused, and the state before is restored`;
if (options["without-write"] === true) notRun("8", writeCheckName, "left out of this run with --without-write");
else await check("8", writeCheckName, async (requests) => {
  const { context, close } = await openContext("admin");
  let pending;
  let outcome;
  let failure;
  try {
    const page = await context.newPage();
    const rows = await openOverrideList(page);
    const candidates = rows.filter((key) => options["tenant-id"] === undefined || key.endsWith(`-${options["tenant-id"]}`));
    assert(candidates.length === 1, `${appUserEmail} has ${candidates.length} rows on the ${flagKey} flag (${rows.join(", ")}); name the account with --tenant-id.`);
    const key = candidates[0];
    const userId = key.slice(0, key.lastIndexOf("-"));
    const tenantId = key.slice(key.lastIndexOf("-") + 1);

    const before = await readUserFlag(userId, tenantId);
    const preferencesBefore = await readPreferencesSwitch();
    pending = { flagKey, email: appUserEmail, userId, tenantId, before, recordedAt: new Date().toISOString() };
    store.writePendingRestore(pending);

    const toggle = await openOverrideRow(page, userId, tenantId);
    const written = page.waitForResponse((response) => response.url().includes(`/feature-flags/${flagKey}/user-override`) && response.request().method() !== "GET");
    await toggle.click();
    const writeResponse = await written;
    requests.push({ method: writeResponse.request().method(), url: writeResponse.url(), status: writeResponse.status(), source: "the Blazor back office's override switch" });
    assert(writeResponse.status() >= 200 && writeResponse.status() < 300, `The admin's write answered ${writeResponse.status()}.`);
    const after = await waitForFlagChange(userId, tenantId, before);

    const preferencesAfter = await readPreferencesSwitch();
    assert(preferencesAfter === String(after.isEnabled), `The app edition's preferences show ${flagKey} as ${preferencesAfter}; the override is ${after.isEnabled}.`);

    // The same write as the non-admin, with the value now set, so an accepted write would change nothing
    const refused = await withPage("non-admin", `${backOfficeUrl}${pathBase}/back-office`, (nonAdminPage) =>
      nonAdminPage.evaluate(
        async ({ key: flag, body }) => {
          const response = await fetch(`/api/back-office/feature-flags/${flag}/user-override`, { method: "PUT", body, headers: { "content-type": "application/json" } });
          return response.status;
        },
        { key: flagKey, body: JSON.stringify({ userId, tenantId: Number(tenantId), enabled: after.isEnabled }) }
      )
    );
    requests.push({ method: "PUT", url: `${backOfficeUrl}/api/back-office/feature-flags/${flagKey}/user-override`, status: refused, identity: "non-admin" });
    assert(refused === 403, `The non-admin's same write answered ${refused}.`);
    outcome = { userId, tenantId, before, after, preferencesBefore, preferencesAfter, nonAdminWrite: refused };
  } catch (error) {
    failure = error;
  }
  await close();

  // The restore runs whatever happened above, and a failure of either is reported with the other
  if (pending !== undefined) {
    try {
      const restored = await restoreOverride(pending);
      const preferencesRestored = await readPreferencesSwitch();
      if (outcome !== undefined) Object.assign(outcome, { restored, preferencesRestored });
      assert(preferencesRestored === String(restored.isEnabled), `After the restore the preferences show ${preferencesRestored}; the flag is ${restored.isEnabled}.`);
    } catch (error) {
      const restoreFailure = `The restore failed: ${error.message} The state before is in ${store.pendingRestoreFile}; the next run restores it first.`;
      throw new Error(failure === undefined ? restoreFailure : `${failure.message} ${restoreFailure}`);
    }
  }
  if (failure !== undefined) throw failure;
  return outcome;
});

await check("8-non-admin", `No write offered to the non-admin on the ${flagKey} flag detail`, () =>
  withPage("non-admin", `${backOfficeUrl}${pathBase}/back-office/feature-flags/${encodeURIComponent(flagKey)}`, async (page) => {
    await waitForBackOffice(page);
    await page.locator('[data-testid="feature-flag-header"]').waitFor({ timeout: interactiveTimeoutMs });
    const offered = {
      activate: await page.locator('[data-testid="feature-flag-activate"]').count(),
      deactivate: await page.locator('[data-testid="feature-flag-deactivate"]').count(),
      rollout: await page.locator('[data-testid="feature-flag-rollout-form"]').count(),
      enabledSwitches: await page.locator('[data-testid^="feature-flag-user-override-"]:not([disabled]), [data-testid^="feature-flag-tenant-override-"]:not([disabled])').count()
    };
    assert(Object.values(offered).every((count) => count === 0), `Offered to the non-admin: ${JSON.stringify(offered)}.`);
    return offered;
  })
);

// ---------------------------------------------------------------------------------------------------------------------
// Checks 3b and 3c from the probe job, only with the owner's approval of this run

const jobName = `bo-probe-${new Date().toISOString().replace(/[^0-9]/g, "").slice(2, 12)}`;
const probeName = "Forged headers inside the environment (3b and 3c)";
console.log(`
Checks 3b and 3c need a probe job inside the Container Apps environment. It is an Azure change:
  create  Container Apps job ${jobName} in ${resourceGroup}, environment ${managedEnvironment.name}
  image   the curl image pinned in blazor/tests/staging/probe-job.yaml, manual trigger, no ingress, no secret
  then    start one execution, read its log, delete the job`);
const approval = await ask(`Type the job name (${jobName}) to approve this run, or press Enter to skip: `);
if (approval !== jobName) {
  notRun("3b", "Forged Host on the internal account API", approval === null ? "no terminal to ask for the probe job's approval" : "the owner did not approve the probe job for this run");
  notRun("3c", "Forged headers on the back-office app from inside", approval === null ? "no terminal to ask for the probe job's approval" : "the owner did not approve the probe job for this run");
} else {
  await check("3b-3c", probeName, async () => {
    const template = readFileSync(path.join(repositoryRoot, "blazor/tests/staging/probe-job.yaml"), "utf8");
    const filled = template
      .replaceAll("{{LOCATION}}", managedEnvironment.location)
      .replaceAll("{{ENVIRONMENT_ID}}", managedEnvironment.id)
      .replaceAll("{{ENVIRONMENT_DOMAIN}}", environmentDomain)
      .replaceAll("{{BACK_OFFICE_HOST}}", backOfficeHost)
      .replaceAll("{{BACK_OFFICE_FQDN}}", apps["back-office"].properties.configuration.ingress.fqdn)
      .replaceAll("{{FORGED_NAME}}", forged.name)
      .replaceAll("{{FORGED_ID}}", forged.id)
      .replaceAll("{{FORGED_PRINCIPAL}}", forged.principal);
    const folder = mkdtempSync(path.join(os.tmpdir(), "probe-job-"));
    const file = path.join(folder, "probe-job.yaml");
    writeFileSync(file, filled);
    let created = false;
    try {
      azure.raw(["containerapp", "job", "create", "--name", jobName, "--resource-group", resourceGroup, "--yaml", file]);
      created = true;
      const execution = azure.json(["containerapp", "job", "start", "--name", jobName, "--resource-group", resourceGroup]);
      const executionName = execution.name ?? execution.id?.split("/").pop();
      const deadline = Date.now() + 10 * 60_000;
      let status;
      do {
        await new Promise((resolve) => setTimeout(resolve, 10_000));
        status = azure.json(["containerapp", "job", "execution", "show", "--name", jobName, "--resource-group", resourceGroup, "--job-execution-name", executionName]).properties.status;
      } while (!["Succeeded", "Failed", "Stopped", "Degraded"].includes(status) && Date.now() < deadline);
      assert(status === "Succeeded", `The probe execution ${executionName} ended ${status}.`);
      const lines = azure
        .raw(["containerapp", "job", "logs", "show", "--name", jobName, "--resource-group", resourceGroup, "--execution", executionName, "--container", "probe", "--format", "text"])
        .split("\n")
        .filter((line) => line.includes("PROBE "))
        .map((line) => line.slice(line.indexOf("PROBE ") + 6).trim());
      assert(lines.includes("done"), `The probe log has no end marker: ${lines.join(" | ")}.`);
      const answers = lines.filter((line) => line !== "done").map((line) => {
        const [probe, status, url, host, accept] = line.split(" ");
        return { check: probe, status: Number(status), url, host, accept };
      });
      const wrong = answers.filter((answer) => (answer.check === "3b" ? ![401, 404].includes(answer.status) : ![302, 401].includes(answer.status)));
      assert(answers.length === 5 && wrong.length === 0, `Answers: ${JSON.stringify(answers)}.`);
      return { job: jobName, execution: executionName, answers };
    } finally {
      rmSync(folder, { recursive: true, force: true });
      if (created) {
        try {
          azure.raw(["containerapp", "job", "delete", "--name", jobName, "--resource-group", resourceGroup, "--yes"]);
          console.log(`Deleted the probe job ${jobName}.`);
        } catch (error) {
          throw new Error(`The probe job ${jobName} could not be deleted; delete it by hand: ${error.message}`);
        }
      }
    }
  });
}

await browser.close();
const verdict = finish({ viewports: surfaceViewports, surfaceIds });
process.exit(verdict.passed ? 0 : 1);
