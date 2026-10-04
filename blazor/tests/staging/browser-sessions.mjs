// The owner's stored sessions and the browser contexts and requests the staging acceptance makes with them: every identity
// signs in before any check runs, a context stores the cookies it ended with when it closes, and a request is recorded with
// its answer.
//
// The functions passed in, each defaulting to the real one:
// - requireSessions: verifyStoredSession, captureSession and ask of support/deployed.mjs, and log
// - createBrowserSessions: newContext of support/stack.mjs, settleAndClosePages of support/surfaces.mjs,
//   reconcileAppSession and recordResponse of support/deployed.mjs, and requestContext(storageState) for a request context

import { ask as askOnTerminal, captureSession as captureOnDesktop, identities, reconcileAppSession as reconcile, recordResponse as record, verifyStoredSession as verifyStored } from "../support/deployed.mjs";
import { newContext as createContext, playwright } from "../support/stack.mjs";
import { settleAndClosePages as settleAndClose } from "../support/surfaces.mjs";

// Every identity's session, verified in order. A missing or expired session is offered an interactive sign-in and then
// verified again; one that is still unusable stops before any check, naming the identity, and is never a failed check.
// Returns { sessions } or { missing: identity }.
export async function requireSessions({ store, target, desktopPort, verifyStoredSession = verifyStored, captureSession = captureOnDesktop, ask = askOnTerminal, log = console.log }) {
  const sessions = {};
  for (const identity of identities) {
    let verdict = await verifyStoredSession(store, identity, target);
    if (!verdict.valid) {
      log(`The ${identity} session cannot be used: ${verdict.reason}.`);
      const answer = await ask(`Sign in as ${identity} now? [y/N] `);
      if (answer?.toLowerCase() === "y") {
        await captureSession(store, identity, { ...target, desktopPort });
        verdict = await verifyStoredSession(store, identity, target);
      }
    }
    if (!verdict.valid) return { missing: identity };
    sessions[identity] = verdict;
    log(`Session ${identity}: ${verdict.me.email}${verdict.appUser ? `, app edition ${verdict.appUser.email}` : ""}.`);
  }
  return { sessions };
}

export function createBrowserSessions({
  browser,
  browserName,
  store,
  appUrl,
  newContext = createContext,
  settleAndClosePages = settleAndClose,
  reconcileAppSession = reconcile,
  recordResponse = record,
  requestContext = (storageState) => playwright.request.newContext({ storageState })
}) {
  const stateOf = (identity) => store.load(identity);
  const keep = (identity) => async (state) => store.save(identity, state);

  // A context of the identity, or of nobody; closing it stores the cookies it ended with, since the account API rotates the
  // refresh token and revokes a session whose previous token comes back
  //
  // Only a context that visited the app host may reconcile or store app-edition cookies. Check 8 keeps one admin context
  // open on the back office while other contexts visit the app host and rotate the refresh token; reconciling the first
  // one's copy, two rotations old and past the 30 s grace, made the account API revoke the session (observed on staging,
  // 2026-10-01 07:41 UTC). A context that never visited the app host stores its back-office cookies beside the stored
  // app-edition ones.
  const appHost = new URL(appUrl).hostname;
  const isAppCookie = (cookie) => cookie.domain.replace(/^\./, "") === appHost;

  async function openContext(identity, contextOptions = {}) {
    const context = await newContext(browser, browserName, identity ? stateOf(identity) : undefined, "en-US", { ignoreHTTPSErrors: false, ...contextOptions });
    let visitedApp = false;
    context.on("request", (request) => {
      if (new URL(request.url()).hostname === appHost) visitedApp = true;
    });
    return {
      context,
      close: async () => {
        await settleAndClosePages(context);
        if (identity === "admin" && visitedApp) await reconcileAppSession(context, appUrl);
        if (identity) {
          const state = await context.storageState();
          const stored = stateOf(identity);
          store.save(identity, visitedApp || stored === undefined ? state : { ...state, cookies: [...stored.cookies.filter(isAppCookie), ...state.cookies.filter((cookie) => !isAppCookie(cookie))] });
        }
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

  // A request as the identity, or with no session at all, recorded with its answer
  async function send(identity, method, url, { headers = {}, data } = {}) {
    const request = await requestContext(identity ? stateOf(identity) : undefined);
    try {
      const response = await request.fetch(url, { method, headers, data, maxRedirects: 0, failOnStatusCode: false });
      const recorded = await recordResponse(method, url, response, headers);
      return { status: response.status(), headers: response.headers(), body: await response.text(), recorded };
    } finally {
      if (identity) store.save(identity, await request.storageState());
      await request.dispose();
    }
  }

  return { stateOf, keep, openContext, withPage, send };
}
