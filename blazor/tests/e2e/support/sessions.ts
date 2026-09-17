import { expect, type Browser, type BrowserContext, type Locator, type Page, type Response } from "@playwright/test";
import { blazorUrl } from "./routes";
import { blazorLocale, blazorTexts } from "./texts";
import { getBaseUrl } from "@shared/e2e/utils/constants";

/**
 * The authentication cookies the gateway sets, as named in SharedKernel's AuthenticationTokenHttpKeys
 */
export const accessTokenCookieName = "__Host-access-token";
export const refreshTokenCookieName = "__Host-refresh-token";
export const antiforgeryCookieName = "__Host-xsrf-token";

/**
 * The account API's bootstrap read, which the workspace's Reload bootstrap button sends again
 */
export const bootstrapPath = "/api/account/bootstrap";

/**
 * Open a second, independent browser in the culture of the running project
 * @param browser Playwright browser instance
 */
export function newBlazorContext(browser: Browser): Promise<BrowserContext> {
  return browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
}

export async function getCookieNames(page: Page): Promise<string[]> {
  return (await page.context().cookies(getBaseUrl())).map((cookie) => cookie.name);
}

/**
 * Remove the access token cookie while keeping the refresh token cookie, so the next request through the gateway has to
 * refresh the session before it reaches the API or the host. This simulates an expired access token; a real expiry is
 * covered by the browser harness, which waits for the token's lifetime
 * @param page Playwright page instance of a signed-in user
 */
export async function deleteAccessTokenCookie(page: Page): Promise<void> {
  await page.context().clearCookies({ name: accessTokenCookieName });

  expect(await getCookieNames(page)).not.toContain(accessTokenCookieName);
  expect(await getCookieNames(page)).toContain(refreshTokenCookieName);
}

/**
 * Copy the refresh token cookie of one browser into another, as a stolen token would be used
 * @param from The browser context of the signed-in user
 * @param to A browser context with no session
 */
export async function copyRefreshTokenCookie(from: BrowserContext, to: BrowserContext): Promise<void> {
  const refreshToken = (await from.cookies(getBaseUrl())).find((cookie) => cookie.name === refreshTokenCookieName);
  expect(refreshToken).toBeDefined();

  await to.addCookies([{ name: refreshTokenCookieName, value: refreshToken!.value, url: getBaseUrl(), secure: true, httpOnly: true, sameSite: refreshToken!.sameSite }]);
}

/**
 * Click the workspace's Reload bootstrap button and wait for the account API's answer
 * @param page Playwright page instance on the interactive authenticated home
 * @returns The bootstrap response
 */
export async function reloadBootstrap(page: Page): Promise<Response> {
  const response = page.waitForResponse((candidate) => new URL(candidate.url()).pathname === bootstrapPath);
  await page.getByRole("button", { name: blazorTexts().reloadBootstrap, exact: true }).click();
  return response;
}

/**
 * Record every state-changing account API request the page sends from now on
 * @param page Playwright page instance
 * @returns The live list of "METHOD path" entries
 */
export function trackWrites(page: Page): string[] {
  const writes: string[] = [];
  page.on("request", (request) => {
    const path = new URL(request.url()).pathname;
    if (!["GET", "HEAD", "OPTIONS"].includes(request.method()) && path.startsWith("/api/")) writes.push(`${request.method()} ${path}`);
  });
  return writes;
}

/**
 * The session cards of the sessions page, each an article named by its browser
 * @param page Playwright page instance on the sessions page
 */
export function sessionCards(page: Page): Locator {
  return page.getByRole("main").getByRole("article");
}

/**
 * The card of the session the page's own cookies belong to, marked "This device"
 * @param page Playwright page instance on the sessions page
 */
export function currentSessionCard(page: Page): Locator {
  return sessionCards(page).filter({ has: page.getByText(blazorTexts().thisDevice, { exact: true }) });
}

/**
 * The cards of the sessions of other devices, each with a Revoke button
 * @param page Playwright page instance on the sessions page
 */
export function otherSessionCards(page: Page): Locator {
  return sessionCards(page).filter({ hasNot: page.getByText(blazorTexts().thisDevice, { exact: true }) });
}

/**
 * The revoke session confirmation dialog
 * @param page Playwright page instance on the sessions page
 */
export function revokeSessionDialog(page: Page): Locator {
  return page.getByRole("alertdialog", { name: blazorTexts().revokeSession, exact: true });
}

/**
 * Expect the sessions page to have read the sessions: the heading and the given number of cards, the current one first
 * @param page Playwright page instance on the sessions page
 * @param count The number of sessions listed
 */
export async function expectSessionsListed(page: Page, count: number): Promise<void> {
  await expect(page.getByRole("heading", { name: blazorTexts().userSessions, exact: true, level: 1 })).toBeVisible();
  await expect(sessionCards(page)).toHaveCount(count);
  await expect(sessionCards(page).first()).toContainText(blazorTexts().thisDevice);
}

/**
 * Revoke the only other device's session through the sessions page: Revoke on its card, then Revoke in the confirmation
 * @param page Playwright page instance on the sessions page with exactly one other session
 */
export async function revokeOtherSessionThroughSessionsPage(page: Page): Promise<void> {
  const texts = blazorTexts();
  await otherSessionCards(page).getByRole("button", { name: texts.revoke, exact: true }).click();
  await expect(revokeSessionDialog(page)).toBeVisible();

  await revokeSessionDialog(page).getByRole("button", { name: texts.revoke, exact: true }).click();

  await expect(revokeSessionDialog(page)).toHaveCount(0);
}

/**
 * The non-dismissable dialog another tab's logout, switch or login raises, named by its title
 * @param page Playwright page instance on an interactive authenticated Blazor page
 * @param title The localized title of the outcome
 */
export function authSyncDialog(page: Page, title: string): Locator {
  return page.getByRole("alertdialog", { name: title, exact: true });
}

/**
 * Simulate the page becoming hidden or visible, raising visibilitychange as the browser does when a tab is switched
 * @param page Playwright page instance
 * @param state The visibility state to report
 */
export async function setVisibilityState(page: Page, state: "hidden" | "visible"): Promise<void> {
  await page.evaluate((visibilityState) => {
    Object.defineProperty(document, "visibilityState", { configurable: true, get: () => visibilityState });
    document.dispatchEvent(new Event("visibilitychange"));
  }, state);
}

declare global {
  interface Window {
    __authSyncMessages?: Record<string, unknown>[];
  }
}

/**
 * The BroadcastChannel both editions announce logout, tenant switch and login on
 */
const authSyncChannelName = "auth-sync";

/**
 * Every field a synchronization message may carry; none of them is a credential
 */
export const authSyncMessageKeys = ["type", "userId", "tenantId", "email", "timestamp", "newTenantId", "previousTenantId", "tenantName"];

/**
 * Record every synchronization message the browser's tabs send from now on, in a tab that only listens
 * @param page Playwright page instance on a page of the gateway's origin
 */
export async function listenToAuthSync(page: Page): Promise<void> {
  await page.evaluate((channelName) => {
    window.__authSyncMessages = [];
    new BroadcastChannel(channelName).onmessage = (event: MessageEvent) => window.__authSyncMessages!.push(event.data as Record<string, unknown>);
  }, authSyncChannelName);
}

/**
 * The synchronization messages the listening tab has received so far
 * @param page The page listenToAuthSync was called on
 */
export function readAuthSyncMessages(page: Page): Promise<Record<string, unknown>[]> {
  return page.evaluate(() => window.__authSyncMessages ?? []);
}

/**
 * Post synchronization messages from the listening tab, as another tab of the browser would send them, and wait until the
 * listener has received them, so every other tab has been sent them in order
 * @param page The page listenToAuthSync was called on
 * @param messages The messages to post in order
 */
export async function postAuthSyncMessages(page: Page, messages: Record<string, unknown>[]): Promise<void> {
  const receivedBefore = (await readAuthSyncMessages(page)).length;
  await page.evaluate(
    ({ channelName, items }) => {
      const channel = new BroadcastChannel(channelName);
      for (const item of items) channel.postMessage(item);
      channel.close();
    },
    { channelName: authSyncChannelName, items: messages }
  );

  await expect.poll(async () => (await readAuthSyncMessages(page)).length).toBe(receivedBefore + messages.length);
}
