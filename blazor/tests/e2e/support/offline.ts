import { expect, test, type Page } from "@playwright/test";
import { blazorPath } from "./routes";

/**
 * The path of the one document the offline shell's service worker stores
 */
export const offlineShellPath = blazorPath("app/offline");

/**
 * The annotation that records a project whose offline navigation this suite deliberately does not exercise, so a report
 * shows which projects proved the offline shell itself and which proved only what surrounds it
 */
export const offlineEmulationAnnotation = "offline-emulation-does-not-reach-the-worker";

/**
 * The annotation that records a project which cannot see the request a service worker issues
 */
export const workerRequestAnnotation = "service-worker-requests-are-not-reported";

/**
 * Whether taking the network away from a browser context also takes it away from the service worker that answers the
 * navigation, which is what the offline shell needs in order to be observable from a specification at all.
 *
 * Measured at b5a22f2d7 on 2026-09-21 in all three browsers, with probe code inside the worker writing one entry per
 * decision into a cache of its own:
 * - Chromium: the fetch handler receives the navigation, its own fetch throws, the cache lookup hits and the stored
 *   shell is returned. Observable, so the offline leg of the case runs here.
 * - Firefox: the fetch handler receives the navigation and its own fetch is answered 200 by the network, because the
 *   context's offline emulation reaches the document but not the worker. In the same run navigator.onLine is false in
 *   the document and true inside the worker, and a fetch the document issues fails. The real page is therefore returned
 *   and the shell can never appear, whatever the worker does.
 * - WebKit: the navigation fails before the fetch handler is dispatched at all, with the same "WebKit encountered an
 *   internal error" this browser gives for a public route offline, which the worker never intercepts. Taking the network
 *   away below the browser instead does reach the handler, so the interception itself works; that technique cannot be
 *   used from here because a relaunched WebKit profile returns with its Cache Storage empty.
 *
 * Neither is a defect of this edition and neither can be worked around from a specification. Firefox's offline shell is
 * proved by blazor/tests/offline-shell-relaunch.mjs, which takes the network away below the browser; WebKit's is carried
 * by the device pass on real Safari. docs/blazor-recovery-runbook.md records both.
 * @param browserName The browser of the running project
 */
export function offlineNavigationReachesTheWorker(browserName: string): boolean {
  return browserName === "chromium";
}

/**
 * Whether the automation library reports a request that a service worker, rather than a document, issued. Only Chromium
 * does, in this library at version 1.61.0, so a step that needs to tell the two apart asserts it there and proves what
 * it can through the Cache API everywhere else.
 * @param browserName The browser of the running project
 */
export function workerRequestsAreReported(browserName: string): boolean {
  return browserName === "chromium";
}

/**
 * The registration that governs the documents under the path base, as its scope and the state of its active worker.
 *
 * This, and not a controlled document, is what makes a case about what the worker does or does not answer mean something,
 * because it means the same in all three browsers. Measured at b5a22f2d7 on 2026-09-21: Firefox sets
 * navigator.serviceWorker.controller only for a document the worker answered, so /blazor/legal is uncontrolled there
 * while signed in and /blazor/app is controlled, before and after a logout alike, while Chromium and WebKit report both
 * as controlled. This worker deliberately answers no public document, so a controlled public document is unreachable in
 * Firefox by design.
 * @param page Playwright page instance on a document under the path base
 */
export function readWorkerRegistration(page: Page): Promise<{ scope: string; active: string | null }> {
  return page.evaluate(async () => {
    const registration = await navigator.serviceWorker.getRegistration();
    return { scope: registration === undefined ? "none" : new URL(registration.scope).pathname, active: registration.active?.state ?? null };
  });
}

/**
 * Open a route of the authenticated surface with the network gone and assert the stored shell answers at that address.
 *
 * In a browser whose offline emulation does not reach the worker the network is deliberately left alone and the same
 * route is opened with it, so signup, worker control, what is stored and the real page at the same address all still run
 * there; only the offline navigation is left out, annotated with the reason. See offlineNavigationReachesTheWorker for
 * the measurements and for where those browsers are covered instead.
 * @param page Playwright page instance on the authenticated surface, with the worker in control
 * @param browserName The browser of the running project
 * @param route Route below the path base to open
 * @param offlineHeading The shell's heading in the culture of the running project
 */
export async function openAuthenticatedRouteWithoutNetwork(page: Page, browserName: string, route: string, offlineHeading: string): Promise<void> {
  if (!offlineNavigationReachesTheWorker(browserName)) {
    test.info().annotations.push({
      type: offlineEmulationAnnotation,
      description: `${browserName}: taking the context offline does not take the network away from the service worker, so the offline navigation is opened with the network instead`
    });
    await page.goto(blazorPath(route));
    return;
  }

  await page.context().setOffline(true);
  await page.goto(blazorPath(route));

  await expect(page.getByTestId("offline-page")).toBeVisible();
  await expect(page.getByRole("heading", { name: offlineHeading })).toBeVisible();
}

/**
 * Watch, from inside the browser, for the stored offline shell to leave the caches. Start it before the action that
 * drops the shell and await it afterwards; it resolves to true the moment no cache holds the shell any more.
 *
 * It reads the Cache API from a document of the same origin, which every browser allows, rather than observing the
 * request with which the worker fetches the shell again, which only Chromium reports. The window it has to catch is
 * short, because the navigation that follows the drop stores a shell again, and the watching document is in the
 * background, where every browser throttles timers to about a second. The loop therefore yields through a message
 * channel, which is not throttled, instead of through setTimeout.
 * @param page Playwright page instance on a same-origin document that is not navigating while the watch runs
 * @param timeoutMs How long to keep sampling before giving up
 */
export function watchForTheStoredShellToBeDropped(page: Page, timeoutMs = 20_000): Promise<boolean> {
  return page.evaluate(
    async ({ shellPath, timeout }) => {
      const shellIsStored = async () => {
        const perCache = await Promise.all((await caches.keys()).map(async (name) => (await (await caches.open(name)).keys()).map((request) => new URL(request.url).pathname)));
        return perCache.flat().includes(shellPath);
      };
      const yieldToTheEventLoop = () =>
        new Promise<void>((resolve) => {
          const channel = new MessageChannel();
          channel.port1.onmessage = () => resolve();
          channel.port2.postMessage(null);
        });
      const deadline = Date.now() + timeout;
      while (Date.now() < deadline) {
        if (!(await shellIsStored())) return true;
        await yieldToTheEventLoop();
      }
      return false;
    },
    { shellPath: offlineShellPath, timeout: timeoutMs }
  );
}

/**
 * Assert that the service worker, and not the document, is what fetches the offline shell again after the stored one was
 * dropped. Start it before the action that drops the shell and await it afterwards.
 *
 * In a browser that does not report a request a service worker issued this asserts nothing and is annotated with the
 * reason; there the drop is proved by watchForTheStoredShellToBeDropped and the restore by the stored shell coming back,
 * both of which every browser can do.
 * @param page Playwright page instance in the context that holds the worker
 * @param browserName The browser of the running project
 */
export async function expectTheWorkerToFetchTheShellAgain(page: Page, browserName: string): Promise<void> {
  if (!workerRequestsAreReported(browserName)) {
    test.info().annotations.push({
      type: workerRequestAnnotation,
      description: `${browserName}: a request issued by a service worker is not reported, so the drop and the restore are asserted through the Cache API instead`
    });
    return;
  }

  const shellFetchedAgain = await page.context().waitForEvent("request", (request) => new URL(request.url()).pathname === offlineShellPath);

  expect(shellFetchedAgain.serviceWorker()).not.toBeNull();
}
