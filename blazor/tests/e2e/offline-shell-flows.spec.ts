import { expect, type Page } from "@playwright/test";
import { inviteUsersThroughAccountApi } from "@blazor/e2e/account-api";
import { logInThroughBlazor, logOutThroughBlazor, openUserMenu, signUpThroughBlazor, test } from "@blazor/e2e/authentication";
import { expectTheWorkerToFetchTheShellAgain, offlineShellPath, openAuthenticatedRouteWithoutNetwork, readWorkerRegistration, watchForTheStoredShellToBeDropped } from "@blazor/e2e/offline";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, blazorUrl, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts } from "@blazor/e2e/texts";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * Wait until the worker registered by the authenticated surface controls the document
 * @param page Playwright page instance on an interactive authenticated Blazor page
 */
async function expectWorkerInControl(page: Page): Promise<void> {
  await expect
    .poll(
      async () =>
        page.evaluate(async () => {
          await navigator.serviceWorker.ready;
          return navigator.serviceWorker.controller !== null;
        }),
      { message: "The offline shell's service worker never took control of the document" }
    )
    .toBe(true);
}

/**
 * Every same-origin path the worker has stored, across all of its caches
 * @param page Playwright page instance on a Blazor document
 */
function readStoredPaths(page: Page): Promise<string[]> {
  return page.evaluate(async () => {
    const paths: string[] = [];
    for (const name of await caches.keys()) {
      const cache = await caches.open(name);
      paths.push(...(await cache.keys()).map((request) => new URL(request.url).pathname));
    }
    return paths;
  });
}

test.describe("@smoke", () => {
  /**
   * An installed application that loses the network:
   * - The authenticated surface registers the worker, which stores the anonymous offline shell and nothing else that is a
   *   document
   * - Without a network, a navigation inside the authenticated surface answers with the shell at the address that was
   *   asked for, so a reload retries that page. This leg runs on Chromium only: in Firefox and WebKit the automation
   *   library cannot take the network away from the service worker, which offlineNavigationReachesTheWorker records with
   *   the measurements, and those browsers are covered by blazor/tests/offline-shell-relaunch.mjs and by the device pass
   * - Back online, the same address shows the real page again
   * - No policy violation on any document, the shell included
   */
  test("should show the offline shell for an authenticated route without a network and the real page once it is back", async ({ page, browserName }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const email = uniqueBlazorEmail();
    await trackPolicyViolations(page);

    await step("Sign up & verify the worker takes control and stores the offline shell")(async () => {
      await signUpThroughBlazor(page, email);
      await expectWorkerInControl(page);

      await expect.poll(async () => (await readStoredPaths(page)).includes(offlineShellPath)).toBe(true);
      const stored = await readStoredPaths(page);
      expect(stored.filter((path) => !path.startsWith(`${blazorPath()}`))).toEqual([]);
      expect(stored.filter((path) => path.startsWith("/api/"))).toEqual([]);
      await expectNoPolicyViolations(page);
    })();

    await step("Open an authenticated route without a network & verify the stored shell answers at that address")(async () => {
      await openAuthenticatedRouteWithoutNetwork(page, browserName, "app/details", texts.youAreOffline);

      await expectBlazorUrl(page, "app/details");
      await expectNoPolicyViolations(page);
    })();

    await step("Come back online and reload & verify the real page replaces the shell")(async () => {
      await page.context().setOffline(false);

      await page.reload();

      await expect(page.getByTestId("app-shell")).toBeVisible();
      await expect(page.getByTestId("offline-page")).toHaveCount(0);
      await expectBlazorUrl(page, "app/details");
      await expectNoPolicyViolations(page);
    })();
  });
});

test.describe("@comprehensive", () => {
  /**
   * What the shell must never carry from one identity to the next, and what it must never intercept:
   * - A tenant switch drops the stored shell, so the next launch of the installed application shows no shell of the
   *   previous tenant
   * - A logout drops it as well and keeps the immutable assets
   * - The public documents are served by the network although a worker is installed and in scope for them
   */
  test("should drop the stored shell on a tenant switch and a logout and never answer a public document", async ({ page, browserName }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const userEmail = uniqueBlazorEmail();
    const secondaryOwnerEmail = uniqueBlazorEmail();
    const secondaryTenantName = "Offline shell second account";
    const ownerPage = await page.context().newPage();
    createTestContext(ownerPage);
    await trackPolicyViolations(page);

    await step("Create a second account that invites the user & verify the invitation")(async () => {
      await signUpThroughBlazor(ownerPage, secondaryOwnerEmail, secondaryTenantName);
      await inviteUsersThroughAccountApi(ownerPage, [userEmail]);
      await logOutThroughBlazor(ownerPage);
    })();

    await step("Sign up the user in their own account & verify the worker stores the shell")(async () => {
      await signUpThroughBlazor(page, userEmail);
      await expectWorkerInControl(page);

      // Every cache reading of this test is taken from the owner's page, a same-origin document that is not navigating.
      // The caches belong to the origin, not to a document, and the page under test navigates throughout, which destroys
      // the execution context a reading of its own would run in
      await expect.poll(async () => (await readStoredPaths(ownerPage)).includes(offlineShellPath)).toBe(true);
    })();

    await step("Switch account & verify the stored shell is dropped and fetched again for the new tenant")(async () => {
      // Two observations of the same drop. The shell leaving the caches is watched from the owner's page, a same-origin
      // document that is not navigating while this one does, and every browser can see it. The worker fetches the
      // anonymous shell only when it has none, so the request for it is the drop observed from the outside, which only
      // Chromium reports. The new tenant's first navigation is what puts one back, asserted by the next step, and the
      // assets are untouched throughout
      const shellDropped = watchForTheStoredShellToBeDropped(ownerPage);
      const workerFetchedTheShellAgain = expectTheWorkerToFetchTheShellAgain(page, browserName);

      await openUserMenu(page);
      await page.getByRole("menuitem", { name: secondaryTenantName }).click();

      expect(await shellDropped).toBe(true);
      await workerFetchedTheShellAgain;
    })();

    await step("Land in the new tenant & verify the worker stored a shell again and kept the assets")(async () => {
      await page.waitForLoadState("load");
      await expect(page.getByTestId("app-shell")).toBeVisible();

      await expect.poll(async () => (await readStoredPaths(ownerPage)).includes(offlineShellPath)).toBe(true);
      expect((await readStoredPaths(ownerPage)).length).toBeGreaterThan(0);
    })();

    await step("Log out & verify the shell stored for the new tenant is dropped and stays dropped")(async () => {

      await logOutThroughBlazor(page);

      await expect.poll(async () => (await readStoredPaths(ownerPage)).includes(offlineShellPath)).toBe(false);
      expect((await readStoredPaths(ownerPage)).length).toBeGreaterThan(0);
    })();

    await step("Open the public documents & verify a worker is in scope but answers none of them")(async () => {
      const documentsFromTheWorker: string[] = [];
      page.on("response", (response) => {
        if (response.request().resourceType() === "document" && response.fromServiceWorker()) documentsFromTheWorker.push(new URL(response.url()).pathname);
      });

      for (const route of ["", "login", "signup", "legal"]) {
        await gotoBlazor(page, route);
        await expectNoPolicyViolations(page);
      }

      // The precondition is that a worker is installed and in scope for these documents, not that it controls them:
      // Firefox sets navigator.serviceWorker.controller only for a document the worker answered, and this worker answers
      // no public document by design, so a controlled public document is unreachable there. See readWorkerRegistration
      expect(await readWorkerRegistration(page)).toEqual({ scope: blazorPath(), active: "activated" });
      expect(documentsFromTheWorker).toEqual([]);
      await expect(page).toHaveURL(blazorUrl("legal"));
    })();

    await step("Log in again & verify the worker stores a shell of the current identity only")(async () => {
      await logInThroughBlazor(page, userEmail);

      await expect.poll(async () => (await readStoredPaths(ownerPage)).includes(offlineShellPath)).toBe(true);
      await expect(page.getByRole("heading", { name: texts.yourWorkspace })).toBeVisible();
    })();
  });
});
