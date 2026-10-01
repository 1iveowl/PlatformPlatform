// Helpers for running the harness against a deployed environment instead of the local stack: Azure reads through the az
// CLI, the owner's stored sessions kept outside the repository, the interactive sign-in on a browser desktop shown through noVNC (the
// dev container has no display), the session checks that ask for a new sign-in instead of failing a check, and the record
// of every request and answer. Nothing here changes Azure; the one Azure change of a run, the probe job, is in
// staging-acceptance.mjs behind the owner's approval.

import { execFileSync, spawn, spawnSync } from "node:child_process";
import { randomBytes } from "node:crypto";
import { chmodSync, existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import readline from "node:readline/promises";
import { playwright, redact, registerSensitiveValue, repositoryRoot } from "./stack.mjs";
import { settleAndClosePages } from "./surfaces.mjs";

// The identities the owner signs in as: the admin is the owner's own Entra account, which also signs in to the app edition
// as the user the admin write targets; the non-admin is the owner's outside account, a back-office user without the admins
// group (owner decisions, 2026-09-30, on EP-227)
export const identities = ["admin", "non-admin"];

const signInTimeoutMs = 15 * 60_000;
const signInPollMs = 3_000;

// Runs az with the subscription named on every call, so a run never depends on or changes the CLI's default subscription
export function createAzure(subscription) {
  const run = (argumentList) => {
    try {
      return execFileSync("az", [...argumentList, "--subscription", subscription, "--only-show-errors"], { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"], maxBuffer: 64 * 1024 * 1024 });
    } catch (error) {
      throw new Error(`az ${argumentList.join(" ")} failed: ${String(error.stderr ?? error.message).trim().slice(0, 600)}`);
    }
  };
  return {
    json: (argumentList) => JSON.parse(run([...argumentList, "--output", "json"]) || "null"),
    text: (argumentList) => run([...argumentList, "--output", "tsv"]).trim(),
    raw: run
  };
}

// The folder the sessions are stored in. The default is outside the repository, under the user's state folder, and a folder
// inside the repository is refused, because a stored session is a credential (owner decision, 2026-09-30)
export function sessionStore(folder, backOfficeHost) {
  const resolved = path.resolve(folder ?? path.join(process.env.XDG_STATE_HOME ?? path.join(os.homedir(), ".local", "state"), "platformplatform", "blazor-staging"));
  const relative = path.relative(repositoryRoot, resolved);
  if (relative === "" || (!relative.startsWith("..") && !path.isAbsolute(relative))) throw new Error(`The session folder ${resolved} is inside the repository; choose a folder outside it.`);
  mkdirSync(resolved, { recursive: true, mode: 0o700 });
  chmodSync(resolved, 0o700);

  const fileOf = (name) => path.join(resolved, `${name}-${backOfficeHost}.json`);
  const writePrivate = (file, value) => {
    const temporary = `${file}.${process.pid}.tmp`;
    writeFileSync(temporary, JSON.stringify(value, null, 2), { mode: 0o600 });
    renameSync(temporary, file);
  };
  return {
    folder: resolved,
    load(identity) {
      const file = fileOf(identity);
      if (!existsSync(file)) return undefined;
      const state = JSON.parse(readFileSync(file, "utf8"));
      for (const cookie of state.cookies ?? []) registerSensitiveValue(cookie.value);
      return state;
    },
    save(identity, state) {
      for (const cookie of state.cookies ?? []) registerSensitiveValue(cookie.value);
      writePrivate(fileOf(identity), state);
    },
    // The state of the admin write's target before the write, kept until the restore is verified, so an interrupted run
    // restores it on the next start
    readPendingRestore: () => (existsSync(fileOf("pending-restore")) ? JSON.parse(readFileSync(fileOf("pending-restore"), "utf8")) : undefined),
    writePendingRestore: (value) => writePrivate(fileOf("pending-restore"), value),
    clearPendingRestore: () => rmSync(fileOf("pending-restore"), { force: true }),
    pendingRestoreFile: fileOf("pending-restore")
  };
}

// Asks the person running the command a question on the terminal; without one there is nobody to ask and it answers null
export async function ask(question) {
  if (!process.stdin.isTTY) return null;
  const terminal = readline.createInterface({ input: process.stdin, output: process.stdout });
  try {
    return (await terminal.question(question)).trim();
  } finally {
    terminal.close();
  }
}

// The back-office identity a stored session carries, read from the account API through the platform authentication:
// 200 names the identity; 401 or a redirect to the sign-in means the session has expired
export async function readBackOfficeSession(storageState, backOfficeUrl) {
  const request = await playwright.request.newContext({ storageState });
  try {
    const response = await request.get(`${backOfficeUrl}/api/back-office/me`, { maxRedirects: 0, headers: { Accept: "application/json" } });
    const me = response.status() === 200 ? await response.json() : null;
    return { status: response.status(), me, storageState: await request.storageState() };
  } finally {
    await request.dispose();
  }
}

// The app edition's identity a stored session carries, read from the bootstrap endpoint through the gateway, which refreshes
// the access token when it has to; the returned storage state holds the rotated cookies and must be stored
export async function readAppSession(storageState, appUrl) {
  const request = await playwright.request.newContext({ storageState });
  try {
    const response = await request.get(`${appUrl}/api/account/bootstrap`, { maxRedirects: 0, headers: { Accept: "application/json" } });
    const bootstrap = response.status() === 200 ? await response.json() : null;
    if (bootstrap?.antiforgeryToken) registerSensitiveValue(bootstrap.antiforgeryToken);
    return { status: response.status(), bootstrap, storageState: await request.storageState() };
  } finally {
    await request.dispose();
  }
}

// Brings a context's app-edition cookies up to date before they are stored. Without its access-token cookie the next request
// makes the gateway refresh with the stored refresh token: the current one rotates normally, and the one a request just
// rotated away (its Set-Cookie lost when the page closed) is still accepted for 30 s and answered with the session's current
// tokens (RefreshAuthenticationTokens, the grace period through PreviousRefreshTokenJti). Either way the jar ends up with the
// token the session holds now. Returns the signed-in user, or null when the context has no app-edition session.
export async function reconcileAppSession(context, appUrl) {
  const cookies = await context.cookies(appUrl);
  if (!cookies.some((cookie) => cookie.name === "__Host-refresh-token")) return null;
  await context.clearCookies({ name: "__Host-access-token", domain: new URL(appUrl).hostname });
  const response = await context.request.get(`${appUrl}/api/account/bootstrap`, { maxRedirects: 0, headers: { Accept: "application/json" } });
  const bootstrap = response.status() === 200 ? await response.json() : null;
  if (bootstrap?.antiforgeryToken) registerSensitiveValue(bootstrap.antiforgeryToken);
  return bootstrap?.isAuthenticated === true ? bootstrap.user : null;
}

// Whether a stored session still signs the identity in, and why not when it does not. Keeps the stored cookies current.
export async function verifyStoredSession(store, identity, { backOfficeUrl, appUrl, appUserEmail }) {
  let state = store.load(identity);
  if (state === undefined) return { valid: false, reason: `no stored ${identity} session in ${store.folder}` };

  const backOffice = await readBackOfficeSession(state, backOfficeUrl);
  state = backOffice.storageState;
  store.save(identity, state);
  if (backOffice.me === null) return { valid: false, reason: `the ${identity} back-office session has expired (GET /api/back-office/me answered ${backOffice.status})` };
  if (backOffice.me.isAdmin !== (identity === "admin")) return { valid: false, reason: `the stored ${identity} session signs in ${backOffice.me.email}, who is ${backOffice.me.isAdmin ? "" : "not "}an admin` };
  if (identity !== "admin") return { valid: true, state, me: backOffice.me };

  const app = await readAppSession(state, appUrl);
  state = app.storageState;
  store.save(identity, state);
  const email = app.bootstrap?.user?.email;
  if (app.bootstrap?.isAuthenticated !== true) return { valid: false, reason: `the ${identity} app-edition session has expired (GET /api/account/bootstrap answered ${app.status}, not signed in)` };
  if (email?.toLowerCase() !== appUserEmail.toLowerCase()) return { valid: false, reason: `the stored ${identity} app-edition session signs in ${email}, not ${appUserEmail}` };
  return { valid: true, state, me: backOffice.me, appUser: app.bootstrap.user };
}

async function pollUntil(read, describe) {
  const deadline = Date.now() + signInTimeoutMs;
  while (Date.now() < deadline) {
    const value = await read();
    if (value !== undefined) return value;
    await new Promise((resolve) => setTimeout(resolve, signInPollMs));
  }
  throw new Error(`No sign-in within ${signInTimeoutMs / 60_000} minutes: ${describe}.`);
}

// The programs the browser desktop needs, which the dev container installs with apt
const desktopPrograms = ["Xvfb", "x11vnc", "websockify"];
const noVncFolder = "/usr/share/novnc";

// Starts a background program and returns it; its output is dropped, since only its port tells whether it is ready
function startProgram(command, argumentList, environment = {}) {
  const child = spawn(command, argumentList, { stdio: "ignore", env: { ...process.env, ...environment } });
  child.on("error", () => undefined);
  return child;
}

async function waitForPort(port, name) {
  const deadline = Date.now() + 15_000;
  while (Date.now() < deadline) {
    const open = await new Promise((resolve) => {
      const socket = net.connect({ host: "127.0.0.1", port }, () => {
        socket.end();
        resolve(true);
      });
      socket.on("error", () => resolve(false));
    });
    if (open) return;
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`${name} did not start listening on port ${port}.`);
}

// A virtual display with a VNC server on it and the noVNC web client in front, all on the container's loopback only. The
// editor forwards the web client's port to the owner's machine, where a browser tab shows the display. The VNC password is
// new for every sign-in and printed on the terminal only.
async function startBrowserDesktop(webPort) {
  const missing = desktopPrograms.filter((program) => spawnSync("which", [program]).status !== 0);
  if (missing.length > 0 || !existsSync(`${noVncFolder}/vnc.html`)) {
    throw new Error(`The browser desktop needs ${[...desktopPrograms, "noVNC"].join(", ")}. Install them with: sudo apt-get install -y xvfb x11vnc novnc websockify`);
  }
  const displayNumber = [99, 98, 97, 96, 95].find((candidate) => !existsSync(`/tmp/.X11-unix/X${candidate}`) && !existsSync(`/tmp/.X${candidate}-lock`));
  if (displayNumber === undefined) throw new Error("No free X display between :95 and :99.");
  const display = `:${displayNumber}`;
  const vncPort = 5900 + displayNumber;
  const password = randomBytes(6).toString("base64url").slice(0, 8);
  const programs = [];
  try {
    programs.push(startProgram("Xvfb", [display, "-screen", "0", "1280x860x24", "-nolisten", "tcp"]));
    await new Promise((resolve) => setTimeout(resolve, 500));
    programs.push(startProgram("x11vnc", ["-display", display, "-rfbport", String(vncPort), "-localhost", "-passwd", password, "-forever", "-shared", "-quiet"]));
    await waitForPort(vncPort, "x11vnc");
    programs.push(startProgram("websockify", ["--web", noVncFolder, `127.0.0.1:${webPort}`, `127.0.0.1:${vncPort}`]));
    await waitForPort(webPort, "websockify");
  } catch (error) {
    for (const program of programs) program.kill();
    throw error;
  }
  return { display, password, stop: () => programs.reverse().forEach((program) => program.kill()) };
}

// Opens a page for the person signing in. A host that scaled to zero can take 20 to 46 s for its first answer, so the load
// gets two minutes, and a load that still has not finished leaves the window for the person to reload instead of failing
async function openSlowly(page, url) {
  await page.goto(url, { waitUntil: "domcontentloaded", timeout: 120_000 }).catch((error) => console.log(`${url} has not loaded yet (${error.message.split("\n")[0]}); reload it in the window if it stays blank.`));
}

// The interactive sign-in, in a container with no display of its own: a Chromium window on a virtual display, shown to the
// owner through noVNC on a port the editor forwards (owner decision, 2026-09-30, after the DevTools screencast would not
// attach on the owner's machine). Nothing is typed into or read from the sign-in pages by this code: it waits until the
// account API answers for the identity, checks it is the one asked for, and stores the cookies.
export async function captureSession(store, identity, { backOfficeUrl, appUrl, appUserEmail, desktopPort }) {
  const desktop = await startBrowserDesktop(desktopPort);
  try {
    const browser = await playwright.chromium.launch({
      headless: false,
      channel: "chromium",
      args: ["--window-position=0,0", "--window-size=1280,860"],
      env: { ...process.env, DISPLAY: desktop.display }
    });
    try {
      // A retry starts from the cookies an earlier attempt stored, so a confirmed back-office sign-in is not asked again
      const context = await browser.newContext({ viewport: null, locale: "en-US", storageState: store.load(identity) });
      const page = await context.newPage();
      await openSlowly(page, `${backOfficeUrl}/blazor/back-office`);

      console.log(`
Sign in as the ${identity} identity${identity === "admin" ? " (your own Entra account)" : " (your outside account without the admins group)"}:
  1. In VS Code's Ports view, choose Forward a Port and enter 127.0.0.1:${desktopPort} (with the address, so the forward
     connects over IPv4). Note the Forwarded Address it shows, usually localhost:${desktopPort}.
  2. In your browser, open http://<forwarded address>/vnc.html?autoconnect=true&resize=scale and enter the password
     ${desktop.password}
  3. Sign in on the browser window shown there.
Waiting up to ${signInTimeoutMs / 60_000} minutes for the back-office sign-in...`);

      const me = await pollUntil(async () => {
        const response = await context.request.get(`${backOfficeUrl}/api/back-office/me`, { maxRedirects: 0, headers: { Accept: "application/json" } });
        return response.status() === 200 ? response.json() : undefined;
      }, `${backOfficeUrl}/api/back-office/me never answered 200`);
      if (me.isAdmin !== (identity === "admin")) throw new Error(`Signed in as ${me.email}, who is ${me.isAdmin ? "" : "not "}an admin; sign in with the ${identity} account instead. Nothing was stored.`);
      console.log(`Back office: signed in as ${me.email} (${me.isAdmin ? "admin" : "not admin"}).`);
      store.save(identity, await context.storageState());

      if (identity === "admin") {
        await openSlowly(page, `${appUrl}/blazor/login`);
        console.log(`
Now sign in to the app edition as ${appUserEmail} in the same window (it shows ${new URL(appUrl).host} now).
Waiting up to ${signInTimeoutMs / 60_000} minutes for the app-edition sign-in...`);
        const user = await pollUntil(async () => {
          const response = await context.request.get(`${appUrl}/api/account/bootstrap`, { maxRedirects: 0, headers: { Accept: "application/json" } });
          if (response.status() !== 200) return undefined;
          const bootstrap = await response.json();
          return bootstrap.isAuthenticated ? bootstrap.user : undefined;
        }, `${appUrl}/api/account/bootstrap never reported a signed-in user`);
        if (user.email.toLowerCase() !== appUserEmail.toLowerCase()) throw new Error(`Signed in to the app edition as ${user.email}, not ${appUserEmail}. Nothing was stored.`);
        console.log(`App edition: signed in as ${user.email}.`);
      }

      // The window's own requests can rotate the refresh token right after the sign-in, so they finish and the pages close
      // first, and the app-edition cookies are reconciled with the session before they are stored
      await settleAndClosePages(context);
      if (identity === "admin") {
        const confirmed = await reconcileAppSession(context, appUrl);
        if (confirmed?.email?.toLowerCase() !== appUserEmail.toLowerCase()) throw new Error("The app-edition sign-in did not hold once the window closed. Nothing was stored.");
      }
      store.save(identity, await context.storageState());
      console.log(`Stored the ${identity} session in ${store.folder} (readable by you only). The browser desktop is closed.`);
      return me;
    } finally {
      await browser.close();
    }
  } finally {
    desktop.stop();
  }
}

// One recorded request and its answer: the method, URL, the request headers that matter, the status, the answer's headers
// that matter and the start of its body. Cookie and token values never reach the record (redact in writeResult).
export async function recordResponse(method, url, response, requestHeaders = {}) {
  const headers = response.headers();
  const body = await response.text().catch(() => "");
  return {
    method,
    url,
    requestHeaders,
    status: response.status(),
    location: headers.location ?? null,
    contentType: headers["content-type"] ?? null,
    body: redact(body.slice(0, 600))
  };
}
