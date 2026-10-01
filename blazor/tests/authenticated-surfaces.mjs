// Every authenticated surface of the Blazor edition as a user sees it, against the running stack, at desktop and phone width:
// the back office (dashboard with its chart cards, accounts list and detail, users list and detail, feature flag list and
// detail) signed in as an admin through the local mock of the platform authentication, and the app edition's authenticated
// pages signed in as a new account owner. Each surface renders its content, scrolls with the mouse wheel when it is taller
// than the window, and reports no policy violation, console error, page error or HTTP error response. The cases and their
// assertions live in support/surfaces.mjs, which the staging acceptance command runs against the deployed hosts.
//
// Prerequisites: the stack started through the developer CLI, with the trimmed Release publish served behind the gateway
// (start-stack --without-blazor-host, blazor-publish, blazor-serve) or with the AppHost's own Blazor host.
// Run: dotnet run --project developer-cli -- blazor-harness authenticated-surfaces --browser all

import { basePort, launchBrowser, newContext, parseArguments, pathBase, probeHostConfiguration, signUpThroughBlazor, writeResult, baseUrl } from "./support/stack.mjs";
import { appSurfaces, backOfficeSurfaces, checkSurfaces, readBackOfficeIds, surfaceViewports } from "./support/surfaces.mjs";

const options = parseArguments(process.argv.slice(2), { browser: "chromium" });
// The account API's back-office listener, which forwards the path base to the Blazor host (PortAllocation: base port + 1)
const backOfficeUrl = `https://back-office.dev.localhost:${basePort + 1}`;
// The local listeners serve the development certificate, which only the gateway's fingerprint is pinned for in Chromium
const contextOptions = { ignoreHTTPSErrors: true };

const browser = await launchBrowser(options.browser);
const hostConfiguration = await probeHostConfiguration(browser, options.browser);
const stamp = `${options.browser}-${Date.now()}`;

// A new account owner, so the app edition's lists and the back office's accounts and users lists have at least one row
const owner = await signUpThroughBlazor(browser, options.browser, `authenticated-surfaces-${stamp}@example.com`);

// The local mock of the platform authentication signs in the admin identity on the back-office host
const backOfficeContext = await newContext(browser, options.browser, undefined, "en-US", contextOptions);
const signIn = await backOfficeContext.newPage();
await signIn.goto(`${backOfficeUrl}/.auth/login/aad/callback?identity=admin&post_login_redirect_uri=${encodeURIComponent(`${pathBase}/back-office/identity`)}`, { waitUntil: "load" });
const ids = await readBackOfficeIds(signIn);
const backOfficeState = await backOfficeContext.storageState();
await backOfficeContext.close();

const surfaces = [...backOfficeSurfaces(ids), ...appSurfaces()];
const results = [
  ...(await checkSurfaces({ browser, browserName: options.browser, origin: backOfficeUrl, storageState: backOfficeState, contextOptions }, backOfficeSurfaces(ids))),
  ...(await checkSurfaces({ browser, browserName: options.browser, origin: baseUrl, storageState: owner.storageState, contextOptions }, appSurfaces()))
];
const browserVersion = browser.version();
await browser.close();

const verdict = writeResult(
  `authenticated-surfaces-${options.browser}.json`,
  { browser: options.browser, browserVersion, culture: "en-US", ...hostConfiguration, viewports: surfaceViewports, backOfficeIds: ids, results },
  surfaces.length * surfaceViewports.length
);
console.log(`Result file: ${verdict.resultFile}`);
if (!verdict.passed) process.exit(1);
