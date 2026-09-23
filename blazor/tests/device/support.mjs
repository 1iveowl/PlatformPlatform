// Spike (EP-187): shared support for the device pass on the macOS host. It runs under Node on the Mac, outside the
// development container, and uses Node's built-in modules only, so nothing is installed on the Mac to run it.
//
// It provides:
// - a minimal W3C WebDriver client, spoken over HTTP to safaridriver;
// - a loopback proxy the runner owns on the gateway port, so the network can be refused at the host by script;
// - the one-time password read from the local mail server, as the container harness does;
// - result files in the same shape as the container harness writes under .workspace/blazor-tests/, stamped with the
//   commit, the publish identity and the device, operating system and browser versions.

import { execFileSync, spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import tls from "node:tls";

// The development certificate is trusted in the login keychain, which Safari and curl read and Node does not by default.
// Adding the operating system's trusted certificates to Node's defaults lets the runner's own requests to the gateway
// verify it the same way, instead of turning verification off.
tls.setDefaultCACertificates([...new Set([...tls.getCACertificates("default"), ...tls.getCACertificates("system")])]);

export const repositoryRoot = path.resolve(import.meta.dirname, "../../..");
export const basePort = Number(readFileSync(path.join(repositoryRoot, ".workspace/port.txt"), "utf8").trim());
export const gatewayHostname = "app.dev.localhost";
export const baseUrl = `https://${gatewayHostname}:${basePort}`;
export const pathBase = "/blazor";
export const resultsFolder = path.join(repositoryRoot, ".workspace/blazor-tests/device");
const publishFolder = path.join(repositoryRoot, ".workspace/blazor-publish");
// Offset of MailpitHttp in application/shared-kernel/SharedKernel/Configuration/PortAllocation.cs
const mailpitUrl = `http://localhost:${basePort + 5}`;
const oneTimePasswordPattern = /^([A-Z]{6})$/m;

export const sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));

export function parseArguments(argumentList, defaults) {
  const options = { ...defaults };
  for (let index = 0; index < argumentList.length; index++) {
    const argument = argumentList[index];
    if (!argument.startsWith("--")) continue;
    const next = argumentList[index + 1];
    if (next === undefined || next.startsWith("--")) options[argument.slice(2)] = true;
    else options[argument.slice(2)] = argumentList[++index];
  }
  return options;
}

function run(command, argumentList, environment = {}) {
  try {
    return execFileSync(command, argumentList, { encoding: "utf8", env: { ...process.env, ...environment }, stdio: ["ignore", "pipe", "pipe"] }).trim();
  } catch (error) {
    return `unavailable: ${String(error.stderr || error.message).trim().split("\n")[0]}`;
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Run identity
// ---------------------------------------------------------------------------------------------------------------------

export function runCommit() {
  return { commit: run("git", ["-C", repositoryRoot, "rev-parse", "HEAD"]), uncommittedChanges: run("git", ["-C", repositoryRoot, "status", "--porcelain"]).length > 0 };
}

// The same identity the container harness records: the fingerprinted client assembly route and a hash of the endpoint manifest
export function publishIdentity() {
  const manifestFile = path.join(publishFolder, "Blazor.Host.staticwebassets.endpoints.json");
  if (!existsSync(manifestFile)) return { clientAssembly: null, endpointManifestSha256: null, note: "no publish folder" };
  const endpoints = JSON.parse(readFileSync(manifestFile, "utf8")).Endpoints;
  const clientRoutes = endpoints.map((endpoint) => endpoint.Route).filter((route) => /^_framework\/Blazor\.Client\.[a-z0-9]+\.wasm$/.test(route));
  return { clientAssembly: [...new Set(clientRoutes)].join(", ") || null, endpointManifestSha256: createHash("sha256").update(readFileSync(manifestFile)).digest("hex") };
}

export async function servedWorkerVersion() {
  const text = await (await fetch(`${baseUrl}${pathBase}/service-worker.js`)).text();
  return text.match(/cacheVersion = "([^"]*)"/)?.[1] ?? null;
}

export function macEnvironment() {
  return {
    platform: `${os.type()} ${os.release()}`,
    architecture: os.arch(),
    macOS: `${run("sw_vers", ["-productVersion"])} (${run("sw_vers", ["-buildVersion"])})`,
    safari: run("defaults", ["read", "/Applications/Safari.app/Contents/Info.plist", "CFBundleShortVersionString"]),
    safariBuild: run("defaults", ["read", "/Applications/Safari.app/Contents/Info.plist", "CFBundleVersion"]),
    safaridriver: run("safaridriver", ["--version"]),
    node: process.version,
    cpuModel: os.cpus()[0]?.model ?? null,
    memoryGiB: Math.round(os.totalmem() / 2 ** 30)
  };
}

// ---------------------------------------------------------------------------------------------------------------------
// Results
// ---------------------------------------------------------------------------------------------------------------------

const sensitiveValues = new Set();

export function redact(text) {
  let redacted = String(text).replace(/eyJ[\w-]+\.[\w-]+\.[\w-]*/g, "[redacted token]");
  for (const value of sensitiveValues) redacted = redacted.split(value).join("[redacted]");
  return redacted;
}

// A run that executed fewer cases than expected, or none, fails, so a runner that skipped cases can never read as passed
export function writeResult(fileName, result, expectedCaseCount) {
  const cases = result.results;
  const failures = [...(result.failures ?? [])];
  const minimum = Math.max(1, expectedCaseCount ?? 1);
  if (cases.length < minimum) failures.push(`${cases.length} of ${minimum} cases ran`);
  const passed = failures.length === 0 && cases.every((entry) => entry.passed);
  const complete = { ...runCommit(), ...result, caseCount: cases.length, expectedCaseCount: minimum, failures, passed };
  mkdirSync(resultsFolder, { recursive: true });
  const resultFile = path.join(resultsFolder, fileName);
  writeFileSync(resultFile, redact(JSON.stringify(complete, null, 2)));
  return { resultFile, passed, failures };
}

export function writeArtifact(fileName, content) {
  mkdirSync(resultsFolder, { recursive: true });
  const file = path.join(resultsFolder, fileName);
  writeFileSync(file, content);
  return file;
}

export function caseRecorder() {
  const results = [];
  async function check(name, action) {
    try {
      const detail = await action();
      results.push({ name, passed: true, detail });
      console.log(`PASS ${name}${detail === undefined ? "" : `: ${redact(JSON.stringify(detail))}`}`);
    } catch (error) {
      results.push({ name, passed: false, detail: error.detail ?? error.message });
      console.log(`FAIL ${name}: ${redact(error.message)}`);
    }
  }
  return { results, check };
}

export function fail(message, detail) {
  const error = new Error(message);
  error.detail = detail ?? message;
  return error;
}

// ---------------------------------------------------------------------------------------------------------------------
// The mail server
// ---------------------------------------------------------------------------------------------------------------------

export async function readOneTimePassword(email, sentAfter, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const search = await (await fetch(`${mailpitUrl}/api/v1/search?query=${encodeURIComponent(`to:"${email}"`)}`)).json();
    const message = search.messages.find((candidate) => new Date(candidate.Created).getTime() >= sentAfter - 1_000);
    if (message !== undefined) {
      const detail = await (await fetch(`${mailpitUrl}/api/v1/message/${message.ID}`)).json();
      const match = detail.Text.match(oneTimePasswordPattern);
      if (match === null) throw new Error(`No one-time password in the mail to ${email}.`);
      sensitiveValues.add(match[1]);
      return match[1];
    }
    await sleep(250);
  }
  throw new Error(`No mail to ${email} within ${timeoutMs} ms.`);
}

// ---------------------------------------------------------------------------------------------------------------------
// Preconditions on the Mac
// ---------------------------------------------------------------------------------------------------------------------

async function canConnect(port, host = "127.0.0.1") {
  return new Promise((resolve) => {
    const socket = net.connect({ port, host });
    socket.once("connect", () => {
      socket.destroy();
      resolve(true);
    });
    socket.once("error", () => resolve(false));
  });
}

// The editor forwards the container's gateway port to the Mac. The runner needs that forward moved to another local port so
// it can own the gateway port itself and refuse connections on it (README.md in this folder).
export async function checkPreconditions(upstreamPort) {
  const problems = [];
  if (!(await canConnect(upstreamPort))) {
    problems.push(`Nothing answers on 127.0.0.1:${upstreamPort}. In the editor's Ports view, change the local address of the forwarded port ${basePort} to ${upstreamPort}.`);
  }
  if (await canConnect(basePort)) {
    problems.push(`Port ${basePort} on the Mac is already taken, most likely by the editor's own forward. Change its local address to ${upstreamPort} so this runner can own ${basePort}.`);
  }
  try {
    await fetch(`${mailpitUrl}/api/v1/info`);
  } catch {
    problems.push(`The mail server does not answer at ${mailpitUrl}. Forward port ${basePort + 5} in the editor's Ports view.`);
  }
  return problems;
}

// ---------------------------------------------------------------------------------------------------------------------
// The network at the host: a TCP proxy on the gateway port that the runner opens and closes
// ---------------------------------------------------------------------------------------------------------------------

// While online, every connection to the gateway port on the Mac's loopback is piped to the editor's forward. Going offline
// closes the listener and destroys every open connection, so the next connection from the browser or its service worker is
// refused at once, which is what a device without a network sees. Nothing listens beyond the loopback interface.
export class HostNetwork {
  constructor(listenPort, upstreamPort) {
    this.listenPort = listenPort;
    this.upstreamPort = upstreamPort;
    this.servers = [];
    this.sockets = new Set();
  }

  async online() {
    if (this.servers.length > 0) return;
    for (const host of ["127.0.0.1", "::1"]) {
      const server = net.createServer((client) => {
        const upstream = net.connect({ port: this.upstreamPort, host: "127.0.0.1" });
        for (const socket of [client, upstream]) {
          this.sockets.add(socket);
          socket.on("close", () => this.sockets.delete(socket));
          socket.on("error", () => socket.destroy());
        }
        client.pipe(upstream);
        upstream.pipe(client);
        client.on("close", () => upstream.destroy());
        upstream.on("close", () => client.destroy());
      });
      const listening = await new Promise((resolve) => {
        server.once("error", () => resolve(false));
        server.listen(this.listenPort, host, () => resolve(true));
      });
      if (listening) this.servers.push(server);
    }
    if (this.servers.length === 0) throw new Error(`Could not listen on port ${this.listenPort}.`);
  }

  async offline() {
    const closing = this.servers.map((server) => new Promise((resolve) => server.close(resolve)));
    for (const socket of this.sockets) socket.destroy();
    this.sockets.clear();
    this.servers = [];
    await Promise.all(closing);
    if (await canConnect(this.listenPort)) throw new Error(`Port ${this.listenPort} still accepts connections after going offline.`);
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// WebDriver
// ---------------------------------------------------------------------------------------------------------------------

const elementKey = "element-6066-11e4-a52f-4a5d2de2d2d0";

export async function startSafariDriver(port) {
  const driver = spawn("safaridriver", ["--port", String(port)], { stdio: ["ignore", "pipe", "pipe"] });
  let log = "";
  driver.stdout.on("data", (chunk) => (log += chunk));
  driver.stderr.on("data", (chunk) => (log += chunk));
  const deadline = Date.now() + 15_000;
  while (Date.now() < deadline) {
    try {
      const status = await (await fetch(`http://127.0.0.1:${port}/status`)).json();
      if (status.value?.ready !== undefined) return { process: driver, url: `http://127.0.0.1:${port}` };
    } catch {
      // not listening yet
    }
    await sleep(200);
  }
  driver.kill();
  throw new Error(`safaridriver did not start: ${log}`);
}

export class WebDriverSession {
  static async create(driverUrl, capabilities) {
    const response = await fetch(`${driverUrl}/session`, { method: "POST", body: JSON.stringify({ capabilities: { alwaysMatch: capabilities } }) });
    const body = await response.json();
    if (!response.ok) throw new Error(`New session refused: ${JSON.stringify(body.value)}`);
    return new WebDriverSession(driverUrl, body.value.sessionId, body.value.capabilities);
  }

  constructor(driverUrl, sessionId, capabilities) {
    this.base = `${driverUrl}/session/${sessionId}`;
    this.capabilities = capabilities;
  }

  async command(method, route, body) {
    // A browser sheet a driver cannot answer, such as a permission prompt, can block a command; no command waits forever
    const response = await fetch(`${this.base}${route}`, { method, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(90_000) });
    const payload = await response.json();
    if (!response.ok) {
      const error = new Error(`${method} ${route}: ${payload.value?.error ?? response.status} ${payload.value?.message ?? ""}`.trim());
      error.webDriverError = payload.value?.error;
      throw error;
    }
    return payload.value;
  }

  navigate(url) {
    return this.command("POST", "/url", { url });
  }

  currentUrl() {
    return this.command("GET", "/url");
  }

  title() {
    return this.command("GET", "/title");
  }

  execute(script, args = []) {
    return this.command("POST", "/execute/sync", { script, args });
  }

  // The script receives its arguments and must call the last one, done, with its result
  executeAsync(script, args = []) {
    return this.command("POST", "/execute/async", { script, args });
  }

  async screenshot(fileName) {
    const base64 = await this.command("GET", "/screenshot");
    return writeArtifact(fileName, Buffer.from(base64, "base64"));
  }

  async find(selector) {
    const found = await this.command("POST", "/elements", { using: "css selector", value: selector });
    if (found.length === 0) return null;
    // The W3C key first; a driver that names the reference differently still returns it as the object's only value
    const reference = found[0][elementKey] ?? found[0].ELEMENT ?? Object.values(found[0])[0];
    if (typeof reference !== "string") throw new Error(`Unreadable element reference: ${JSON.stringify(found[0])}`);
    return reference;
  }

  async waitFor(selector, timeoutMs = 30_000) {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      const element = await this.find(selector).catch(() => null);
      if (element !== null) return element;
      await sleep(250);
    }
    throw new Error(`${selector} did not appear within ${timeoutMs} ms on ${await this.currentUrl().catch(() => "an unreadable page")}.`);
  }

  async waitForUrl(pattern, timeoutMs = 30_000) {
    const deadline = Date.now() + timeoutMs;
    let url = "";
    while (Date.now() < deadline) {
      url = await this.currentUrl().catch(() => url);
      if (pattern.test(url)) return url;
      await sleep(250);
    }
    throw new Error(`The address never matched ${pattern} within ${timeoutMs} ms; it is ${url}.`);
  }

  // Safari's driver was seen to drop a keystroke into a field that was still settling ("deice-push-…" for "device-push-…"),
  // so the value is read back and typed again until it is exactly what was asked for
  async type(selector, text, verify = true, attempts = 3) {
    let value = null;
    for (let attempt = 0; attempt < attempts; attempt++) {
      const element = await this.waitFor(selector);
      await this.command("POST", `/element/${element}/clear`, {}).catch(() => undefined);
      await this.command("POST", `/element/${element}/value`, { text });
      if (!verify) return;
      value = await this.command("GET", `/element/${element}/property/value`).catch(() => null);
      if (value === text) return;
      await sleep(500);
    }
    throw new Error(`${selector} holds ${JSON.stringify(value)} after ${attempts} attempts to type it.`);
  }

  async click(selector) {
    const element = await this.waitFor(selector);
    await this.command("POST", `/element/${element}/click`, {});
  }

  async close() {
    await fetch(this.base, { method: "DELETE" }).catch(() => undefined);
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// The Blazor public pages, driven the way a user would: the code is read from the mail server, never typed by a person
// ---------------------------------------------------------------------------------------------------------------------

const testId = (id) => `[data-testid="${id}"]`;

export async function signUp(session, email) {
  await session.navigate(`${baseUrl}${pathBase}/signup`);
  await session.type(testId("email"), email);
  const sentAfter = Date.now();
  await session.click(testId("submit"));
  await session.waitForUrl(/\/blazor\/signup\/verify\?/);
  const oneTimePassword = await readOneTimePassword(email, sentAfter);
  // The code field submits itself at six characters, so the page may be gone before a read-back
  await session.type(testId("code"), oneTimePassword, false);
  await session.waitForUrl(/\/blazor\/welcome\?/);
  await session.type(testId("account-name"), "Device pass account");
  await session.click(testId("continue"));
  await session.type(testId("first-name"), "Device");
  await session.type(testId("last-name"), "Pass");
  await session.click(testId("continue"));
  await session.waitForUrl(new RegExp(`${baseUrl.replace(/[.]/g, "\\.")}${pathBase}/app$`));
}

export async function signIn(session, email) {
  await session.navigate(`${baseUrl}${pathBase}/login`);
  await session.type(testId("email"), email);
  const sentAfter = Date.now();
  await session.click(testId("submit"));
  await session.waitForUrl(/\/blazor\/login\/verify\?/);
  const oneTimePassword = await readOneTimePassword(email, sentAfter);
  // The code field submits itself at six characters, so the page may be gone before a read-back
  await session.type(testId("code"), oneTimePassword, false);
  await session.waitForUrl(new RegExp(`${baseUrl.replace(/[.]/g, "\\.")}${pathBase}/app$`));
}

// The user menu's last item is the logout, as in the container harness
export async function logOut(session) {
  // A press that lands while the page is still settling may not open the menu, so it is pressed again, at most three times
  for (let attempt = 0; attempt < 3; attempt++) {
    await session.click("#user-menu-trigger");
    const opened = await session.waitFor('[role="menu"] [role="menuitem"]', 5_000).then(() => true, () => false);
    if (opened) break;
    if (attempt === 2) throw new Error("The user menu did not open after three presses.");
  }
  const items = await session.command("POST", "/elements", { using: "css selector", value: '[role="menu"] [role="menuitem"]' });
  const last = Object.values(items[items.length - 1])[0];
  await session.command("POST", `/element/${last}/click`, {});
  await session.waitForUrl(/\/blazor\/login/);
}
