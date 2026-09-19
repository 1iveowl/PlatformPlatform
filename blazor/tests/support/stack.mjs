// Shared helpers for the browser harness scripts in blazor/tests. They run against the local stack through the gateway:
// the AppHost started without its blazor-host resource (start-stack --without-blazor-host) and the trimmed Release publish
// served in its place by the developer CLI (blazor-publish, then blazor-serve).

import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import os from "node:os";
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

// The component library's browser bundle, which is its JavaScript initializer. Blazor.Host.csproj keeps it out of the host's
// JS module manifest, so it arrives only with the WebAssembly runtime; a static public page must issue none of these either
export const componentLibraryRequestPattern = /\/_content\/Microsoft\.FluentUI\.AspNetCore\.Components\/[^/]*\.lib\.module\.js(\?|$)/;

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
// contextOptions: further Playwright context options, for example a phone viewport with touch
export async function newContext(browser, browserName, storageState, locale = "en-US", contextOptions = {}) {
  const context = await browser.newContext({ ignoreHTTPSErrors: browserName !== "chromium", locale, storageState, ...contextOptions });
  // Violations are also reported to Node through a binding, so a strict verdict sees those of documents the page has left
  const violations = [];
  policyViolationsByContext.set(context, violations);
  await context.exposeBinding("__reportPolicyViolation", ({ page }, violation) => violations.push({ page: page.url(), ...violation }));
  await context.addInitScript(() => {
    window.__policyViolations = [];
    document.addEventListener("securitypolicyviolation", (event) => {
      const violation = { effectiveDirective: event.effectiveDirective, blockedURI: event.blockedURI };
      window.__policyViolations.push(violation);
      window.__reportPolicyViolation?.(violation);
    });
  });
  return context;
}

const policyViolationsByContext = new WeakMap();

// Every content security policy violation of every document the context has loaded so far
export function policyViolationsOf(context) {
  return policyViolationsByContext.get(context) ?? [];
}

// The strict verdict of a browser journey: any content security policy violation, console error, page error or HTTP error
// response fails it. Nothing is allowlisted; a negative test that expects an error response observes its own page instead.
export function strictFailures(label, observations, violations) {
  const failures = [];
  if (violations.length > 0) failures.push(`${label}: ${violations.length} content security policy violations`);
  if (observations.consoleErrors.length > 0) failures.push(`${label}: ${observations.consoleErrors.length} console errors`);
  if (observations.pageErrors.length > 0) failures.push(`${label}: ${observations.pageErrors.length} page errors`);
  if (observations.errorResponses.length > 0) failures.push(`${label}: ${observations.errorResponses.length} error responses`);
  return failures;
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

// The development host list in the policy tells the environment apart; blazor-serve is the only way this stack runs the
// host in Production, and it serves the trimmed Release publish, while the AppHost runs the Debug build in Development
export function hostConfiguration(contentSecurityPolicy) {
  return isProductionPolicy(contentSecurityPolicy)
    ? { hostEnvironment: "Production", buildConfiguration: "Release trimmed publish served by blazor-serve" }
    : { hostEnvironment: "Development", buildConfiguration: "Debug build run by the AppHost" };
}

// Reads the host configuration from the policy on the login page, in a context of its own
export async function probeHostConfiguration(browser, browserName) {
  const context = await newContext(browser, browserName);
  try {
    const response = await (await context.newPage()).goto(`${baseUrl}${pathBase}/login`, { waitUntil: "load" });
    return hostConfiguration(response.headers()["content-security-policy"]);
  } finally {
    await context.close();
  }
}

// The commit the run tested, from the CI environment or the working tree, with whether the tree had uncommitted changes
export function runCommit() {
  const git = (...argumentList) => execFileSync("git", argumentList, { cwd: repositoryRoot, encoding: "utf8" }).trim();
  return { commit: process.env.GITHUB_SHA ?? git("rev-parse", "HEAD"), uncommittedChanges: git("status", "--porcelain").length > 0 };
}

// One-time passwords, tokens and cookie values never reach a result file or the console: every value read from the mail
// server is registered here, and anything shaped like a signed token is masked as well
const sensitiveValues = new Set();
const signedTokenPattern = /eyJ[\w-]+\.[\w-]+\.[\w-]*/g;

export function registerSensitiveValue(value) {
  if (typeof value === "string" && value.length > 0) sensitiveValues.add(value);
}

export function redact(text) {
  let redacted = String(text).replace(signedTokenPattern, "[redacted token]");
  for (const value of sensitiveValues) redacted = redacted.split(value).join("[redacted]");
  return redacted;
}

// Writes a harness result under .workspace/blazor-tests/ with its run metadata, redacted, and returns the file path and the
// verdict. The cases are the result's results list, cases map or checks list, each with a passed flag. A run that executed
// fewer cases than expectedCaseCount, or none, fails, so a script that skipped cases can never read as passed.
export function writeResult(fileName, result, expectedCaseCount) {
  const cases = result.results ?? result.checks ?? Object.values(result.cases ?? {});
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

// Routes of the published static web assets, to prove the gateway serves this publish and to size the Brotli files on disk
export function readPublishedEndpoints() {
  const manifest = JSON.parse(readFileSync(path.join(publishFolder, "Blazor.Host.staticwebassets.endpoints.json"), "utf8"));
  return manifest.Endpoints;
}

// Identifies the publish a measurement ran against: the content-fingerprinted Blazor.Client assembly route and a hash of the
// endpoint manifest, which changes whenever any published static asset changes
export function publishIdentity() {
  const manifestFile = path.join(publishFolder, "Blazor.Host.staticwebassets.endpoints.json");
  const clientRoutes = readPublishedEndpoints()
    .map((endpoint) => endpoint.Route)
    .filter((route) => /^_framework\/Blazor\.Client\.[a-z0-9]+\.wasm$/.test(route));
  return { clientAssembly: [...new Set(clientRoutes)].join(", ") || null, endpointManifestSha256: createHash("sha256").update(readFileSync(manifestFile)).digest("hex") };
}

// Where a measurement ran: the GitHub Actions runner when there is one, and the machine either way
export function runEnvironment() {
  const environment = process.env;
  return {
    githubActions: environment.GITHUB_ACTIONS === "true",
    runnerEnvironment: environment.RUNNER_ENVIRONMENT ?? null,
    runnerOs: environment.RUNNER_OS ?? null,
    runnerArchitecture: environment.RUNNER_ARCH ?? null,
    runnerImage: environment.ImageOS ? `${environment.ImageOS} ${environment.ImageVersion ?? ""}`.trim() : null,
    workflowRun: environment.GITHUB_RUN_ID ? `${environment.GITHUB_REPOSITORY}/actions/runs/${environment.GITHUB_RUN_ID} attempt ${environment.GITHUB_RUN_ATTEMPT}` : null,
    platform: `${os.type()} ${os.release()}`,
    architecture: os.arch(),
    cpus: os.cpus().length,
    cpuModel: os.cpus()[0]?.model ?? null,
    memoryGiB: Math.round(os.totalmem() / 2 ** 30)
  };
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
      registerSensitiveValue(match[1]);
      return match[1];
    }
    await new Promise((resolve) => setTimeout(resolve, mailPollIntervalMs));
  }
  throw new Error(`No mail to ${email} within ${mailTimeoutMs} ms.`);
}

// Records a trace of the context, with screenshots and DOM snapshots, for a script that keeps it when the run fails
export async function startTrace(context) {
  await context.tracing.start({ screenshots: true, snapshots: true });
}

// Ends the trace started by startTrace, writing it to traceFile only when it is given
export async function stopTrace(context, traceFile) {
  await context.tracing.stop(traceFile === undefined ? undefined : { path: traceFile });
}

// Signs up a new user through the Blazor public pages with the mailed code and returns the signed-in storage state. The
// browser locale sets Accept-Language, which the signup stores as the user's locale. With failureTraceFile, a failed signup
// leaves a trace at that path. With a strict verdict, the errors and policy violations of the signup and welcome pages are
// returned for the caller to judge.
export async function signUpThroughBlazor(browser, browserName, email, locale = "en-US", failureTraceFile = undefined, strict = false) {
  const context = await newContext(browser, browserName, undefined, locale);
  if (failureTraceFile !== undefined) await startTrace(context);
  try {
    const page = await context.newPage();
    const observations = strict ? observeErrors(page) : undefined;
    await page.goto(`${baseUrl}${pathBase}/signup`, { waitUntil: "load" });
    await page.locator('[data-testid="email"]').fill(email);
    const sentAfter = Date.now();
    await page.locator('[data-testid="submit"]').click();
    await page.waitForURL(/\/blazor\/signup\/verify\?/);
    const verifyUrl = page.url();
    const oneTimePassword = await readOneTimePassword(email, sentAfter);
    await submitOneTimePasswordThroughBlazor(page, oneTimePassword);
    await completeWelcomeThroughBlazor(page);
    const storageState = await context.storageState();
    if (failureTraceFile !== undefined) await stopTrace(context);
    if (!strict) return { email, verifyUrl, storageState };
    return { email, verifyUrl, storageState, observations, violations: [...policyViolationsOf(context)] };
  } catch (error) {
    if (failureTraceFile !== undefined) await stopTrace(context, failureTraceFile);
    throw error;
  } finally {
    await context.close();
  }
}

// Enters a six-character code on a Blazor verification page, which submits itself once, and waits for that post; clicking
// Verify as well would race the auto-submit
export async function submitOneTimePasswordThroughBlazor(page, oneTimePassword) {
  const posted = page.waitForRequest((request) => request.method() === "POST" && new URL(request.url()).pathname.endsWith("/verify"));
  await page.locator('[data-testid="code"]').fill(oneTimePassword);
  await posted;
}

// Completes the welcome setup a new account owner is sent to after signup: names the tenant, sets up the profile and waits
// for the authenticated home
export async function completeWelcomeThroughBlazor(page, accountName = "Harness account") {
  await page.waitForURL(/\/blazor\/welcome\?/);
  await page.locator('[data-testid="account-name"]').fill(accountName);
  await page.locator('[data-testid="continue"]').click();
  await page.locator('[data-testid="first-name"]').waitFor();
  await page.locator('[data-testid="first-name"]').fill("Harness");
  await page.locator('[data-testid="last-name"]').fill("User");
  await page.locator('[data-testid="continue"]').click();
  await page.waitForURL(`${baseUrl}${pathBase}/app`);
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
