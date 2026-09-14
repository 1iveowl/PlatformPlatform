// Shared helpers for the browser harness scripts in blazor/tests. They run against the local stack through the gateway:
// the AppHost started by the aspire-restart skill, with the Aspire resource blazor-host stopped and the trimmed Release
// publish served in its place by the developer CLI (blazor-publish, then blazor-serve).

import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import { X509Certificate, createHash } from "node:crypto";
import tls from "node:tls";

export const repositoryRoot = path.resolve(import.meta.dirname, "../../..");
const requireFromApplication = createRequire(path.join(repositoryRoot, "application/"));
export const playwright = requireFromApplication("playwright");
export const playwrightVersion = requireFromApplication("playwright/package.json").version;

export const basePort = Number(readFileSync(path.join(repositoryRoot, ".workspace/port.txt"), "utf8").trim());
export const gatewayHostname = "app.dev.localhost";
export const baseUrl = `https://${gatewayHostname}:${basePort}`;
export const pathBase = "/blazor";
export const resultsFolder = path.join(repositoryRoot, ".workspace/blazor-tests");
export const publishFolder = path.join(repositoryRoot, ".workspace/blazor-publish");

// Offset of MailpitHttp in application/shared-kernel/SharedKernel/Configuration/PortAllocation.cs
const mailpitUrl = `http://localhost:${basePort + 5}`;
const oneTimePasswordPattern = /^([A-Z]{6})$/m;
const mailPollIntervalMs = 250;
const mailTimeoutMs = 30_000;

// Requests that belong to the WebAssembly runtime; a public page must issue none of them
export const runtimeRequestPattern = /\/_framework\/(dotnet[^/]*\.js|[^/]*\.wasm|[^/]*\.dat|blazor\.boot\.json)(\?|$)/;

export function parseArguments(argumentList, defaults) {
  const parsed = { ...defaults };
  for (let index = 0; index < argumentList.length; index++) {
    const name = argumentList[index].replace(/^--/, "");
    const next = argumentList[index + 1];
    if (next === undefined || next.startsWith("--")) {
      parsed[name] = true;
    } else {
      parsed[name] = next;
      index++;
    }
  }
  return parsed;
}

// Chromium does not cache responses for a context that ignores certificate errors, so for Chromium the development
// certificate's public key is accepted by fingerprint instead, which keeps the HTTP cache and the warm measurements real
export async function launchBrowser(browserName) {
  if (browserName !== "chromium") return playwright[browserName].launch();
  const fingerprint = await gatewayCertificateFingerprint();
  return playwright.chromium.launch({ args: [`--ignore-certificate-errors-spki-list=${fingerprint}`] });
}

function gatewayCertificateFingerprint() {
  return new Promise((resolve, reject) => {
    const socket = tls.connect({ host: "127.0.0.1", port: basePort, servername: gatewayHostname, rejectUnauthorized: false }, () => {
      const certificate = new X509Certificate(socket.getPeerCertificate().raw);
      const subjectPublicKeyInfo = certificate.publicKey.export({ type: "spki", format: "der" });
      socket.end();
      resolve(createHash("sha256").update(subjectPublicKeyInfo).digest("base64"));
    });
    socket.on("error", reject);
  });
}

// An explicit locale: headless Chromium in the container otherwise reports "en-US@posix", which the .NET runtime rejects
// as a culture name and aborts the WebAssembly start
export async function newContext(browser, browserName, storageState) {
  const context = await browser.newContext({ ignoreHTTPSErrors: browserName !== "chromium", locale: "en-US", storageState });
  await context.addInitScript(() => {
    window.__policyViolations = [];
    document.addEventListener("securitypolicyviolation", (event) => {
      window.__policyViolations.push({ effectiveDirective: event.effectiveDirective, blockedURI: event.blockedURI });
    });
  });
  return context;
}

export function observeErrors(page) {
  const observations = { consoleErrors: [], pageErrors: [], errorResponses: [] };
  page.on("response", (response) => {
    if (response.status() >= 400) observations.errorResponses.push(`${response.status()} ${response.url()}`);
  });
  page.on("console", (message) => {
    if (message.type() === "error") observations.consoleErrors.push(message.text().slice(0, 400));
  });
  page.on("pageerror", (error) => observations.pageErrors.push(String(error.message).slice(0, 400)));
  return observations;
}

// The development host list in the policy adds wildcard ports; without them the host runs outside Development
export function isProductionPolicy(contentSecurityPolicy) {
  return typeof contentSecurityPolicy === "string" && contentSecurityPolicy.length > 0 && !contentSecurityPolicy.includes(":*");
}

// Routes of the published static web assets, to prove the gateway serves this publish and to size the Brotli files on disk
export function readPublishedEndpoints() {
  const manifest = JSON.parse(readFileSync(path.join(publishFolder, "Blazor.Host.staticwebassets.endpoints.json"), "utf8"));
  return manifest.Endpoints;
}

// Reads the one-time password the account API mailed, the way a user would, instead of any debug-only shortcut
export async function readOneTimePassword(email, sentAfter) {
  const deadline = Date.now() + mailTimeoutMs;
  while (Date.now() < deadline) {
    const search = await (await fetch(`${mailpitUrl}/api/v1/search?query=${encodeURIComponent(`to:"${email}"`)}`)).json();
    const message = search.messages.find((candidate) => new Date(candidate.Created).getTime() >= sentAfter - 1_000);
    if (message !== undefined) {
      const detail = await (await fetch(`${mailpitUrl}/api/v1/message/${message.ID}`)).json();
      const match = detail.Text.match(oneTimePasswordPattern);
      if (match === null) throw new Error(`No one-time password in the mail to ${email}.`);
      return match[1];
    }
    await new Promise((resolve) => setTimeout(resolve, mailPollIntervalMs));
  }
  throw new Error(`No mail to ${email} within ${mailTimeoutMs} ms.`);
}

// Signs up a new user through the Blazor public pages with the mailed code and returns the signed-in storage state
export async function signUpThroughBlazor(browser, browserName, email) {
  const context = await newContext(browser, browserName);
  const page = await context.newPage();
  await page.goto(`${baseUrl}${pathBase}/signup`, { waitUntil: "load" });
  await page.locator('[data-testid="email"]').fill(email);
  const sentAfter = Date.now();
  await page.locator('[data-testid="submit"]').click();
  await page.waitForURL(/\/blazor\/signup\/verify\?/);
  const verifyUrl = page.url();
  const oneTimePassword = await readOneTimePassword(email, sentAfter);
  await page.locator('[data-testid="code"]').fill(oneTimePassword);
  await page.locator('[data-testid="submit"]').click();
  await page.waitForURL(`${baseUrl}${pathBase}/app`);
  const storageState = await context.storageState();
  await context.close();
  return { email, verifyUrl, storageState };
}

// Starts an email login for an existing user through the Blazor login page and returns the verification page URL it lands on
export async function startLoginThroughBlazor(browser, browserName, email) {
  const context = await newContext(browser, browserName);
  const page = await context.newPage();
  await page.goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
  await page.locator('[data-testid="email"]').fill(email);
  await page.locator('[data-testid="submit"]').click();
  await page.waitForURL(/\/blazor\/login\/verify\?/);
  const verifyUrl = page.url();
  await context.close();
  return verifyUrl;
}

// Starts an email signup for a new address and returns the verification page URL, without completing it
export async function startSignupThroughBlazor(browser, browserName, email) {
  const context = await newContext(browser, browserName);
  const page = await context.newPage();
  await page.goto(`${baseUrl}${pathBase}/signup`, { waitUntil: "load" });
  await page.locator('[data-testid="email"]').fill(email);
  await page.locator('[data-testid="submit"]').click();
  await page.waitForURL(/\/blazor\/signup\/verify\?/);
  const verifyUrl = page.url();
  await context.close();
  return verifyUrl;
}
