import { type Browser, type BrowserContext, expect, type Locator, type Page } from "@playwright/test";
import { type AccountApiResponse, sendAccountApiRequest } from "./account-api";
import { blazorBackOfficeUrl, openBlazorBackOffice } from "./back-office";

/**
 * The tenant-scoped flag the Features section of the account settings page shows: a kill-switch flag a tenant owner
 * configures for the whole account
 */
export const tenantFeatureFlagKey = "account-overview";

/**
 * The user-scoped flag the Feature preferences section of the preferences page shows: a kill-switch flag each user
 * configures for themselves
 */
export const userFeatureFlagKey = "compact-view";

/**
 * A user-scoped flag the registry does not let a user configure: an A/B flag an administrator rolls out. The user override
 * endpoint refuses it, which is what proves a flag list is not an authorization boundary
 */
export const nonConfigurableUserFeatureFlagKey = "experimental-ui";

/**
 * The account API route a tenant override goes to, which is also what the feature flag header assertion waits for
 * @param flagKey The registry key of the flag
 */
export function tenantOverrideRoute(flagKey: string): string {
  return `/api/account/feature-flags/${flagKey}/tenant-override`;
}

/**
 * The account API route a user override goes to
 * @param flagKey The registry key of the flag
 */
export function userOverrideRoute(flagKey: string): string {
  return `/api/account/feature-flags/${flagKey}/user-override`;
}

/**
 * One switch of a feature flag section, named by the flag's localized name and described by its localized description
 * @param page Playwright page instance on an interactive Blazor surface
 * @param flagName The flag's name in the culture of the running project
 */
export function featureFlagSwitch(page: Page, flagName: string): Locator {
  return page.getByRole("switch", { name: flagName, exact: true });
}

/**
 * Set the signed-in owner's tenant override through the account API and return the response, which the caller asserts
 * @param page Playwright page instance of a signed-in user
 * @param flagKey The registry key of the flag
 * @param enabled Whether the flag is turned on for the tenant
 */
export function setTenantFeatureFlagThroughAccountApi(page: Page, flagKey: string, enabled: boolean): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "PUT", tenantOverrideRoute(flagKey), { enabled });
}

/**
 * Set the signed-in user's own override through the account API and return the response, which the caller asserts
 * @param page Playwright page instance of a signed-in user
 * @param flagKey The registry key of the flag
 * @param enabled Whether the flag is turned on for the user
 */
export function setUserFeatureFlagThroughAccountApi(page: Page, flagKey: string, enabled: boolean): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "PUT", userOverrideRoute(flagKey), { enabled });
}

/**
 * The tenant-configurable flags of the signed-in user's account, as the section reads them, keyed by registry key
 * @param page Playwright page instance of a signed-in user
 */
export async function getTenantConfigurableFeatureFlags(page: Page): Promise<Record<string, boolean>> {
  return readConfigurableFlags(page, "/api/account/feature-flags/tenant-configurable");
}

/**
 * The user-configurable flags of the signed-in user, as the section reads them, keyed by registry key
 * @param page Playwright page instance of a signed-in user
 */
export async function getUserConfigurableFeatureFlags(page: Page): Promise<Record<string, boolean>> {
  return readConfigurableFlags(page, "/api/account/feature-flags/user-configurable");
}

/**
 * A signed-in back-office administrator: the page the precondition and the teardown send their requests from, and the
 * context to close when the test is over
 */
export interface BackOfficeAdmin {
  context: BrowserContext;
  page: Page;
}

/**
 * Open a Blazor back-office context and sign in as an administrator on the feature flag list. The helpers below drive the
 * page by test id, so the context is pinned to en-US whichever culture project the running test belongs to.
 * @param browser The browser the test runs in
 */
export function signInToBackOfficeAsAdmin(browser: Browser): Promise<BackOfficeAdmin> {
  return openBlazorBackOffice(browser, "admin", "back-office/feature-flags");
}

/**
 * Open a flag's detail in the Blazor back office and wait until its runtime has started and it has read the flag
 * @param page Playwright page instance of a signed-in back-office administrator
 * @param flagKey The registry key of the flag
 */
export async function openFeatureFlagDetailInBackOffice(page: Page, flagKey: string): Promise<void> {
  await page.goto(blazorBackOfficeUrl(`back-office/feature-flags/${flagKey}`));
  await expect(page.getByTestId("back-office-shell")).toHaveAttribute("data-interactive", "true");
  await expect(page.getByTestId("back-office-feature-flag-detail")).toHaveAttribute("data-state", "loaded");
}

/**
 * Confirm one of the flag detail's actions through its dialog and wait until the dialog has closed, which it does once the
 * account API has answered, whatever the answer
 * @param page Playwright page instance on a flag's detail
 * @param action The action whose button and dialog to use
 */
export async function confirmFeatureFlagActionInBackOffice(page: Page, action: "activate" | "deactivate"): Promise<void> {
  await page.getByTestId(`feature-flag-${action}`).click();
  const dialog = page.getByTestId(`${action}-feature-flag-dialog`);
  await dialog.getByTestId(`${action}-feature-flag-dialog-confirm`).click();
  await expect(dialog).toBeHidden();
}

/**
 * The attempts one flag's activation is given before the run gives up on the shared fixture
 */
const activationAttempts = 5;

/**
 * Leave the given flags globally active, through the Blazor back office's flag detail. Both configurable flags are
 * kill-switch flags that the reconciler creates globally inactive, and a section hides a flag whose base row is inactive,
 * so a run activates them first, exactly as the React specification does.
 *
 * The state is read before every write and the flag is only ever turned on, never off, so the six browser and culture
 * projects converge on one end state rather than racing each other, and a stack whose flags are already active is not
 * written to at all. Two activations of the same row do collide in the database, which the back office answers with a 500,
 * so the state is read again after each attempt: a project whose own write lost the race continues on the activation the
 * winner made.
 * @param page Playwright page instance of a signed-in back-office administrator
 * @param flagKeys The registry keys of the flags to leave active
 */
export async function ensureFeatureFlagsActivatedThroughBackOffice(page: Page, flagKeys: string[]): Promise<void> {
  for (const flagKey of flagKeys) {
    for (let attempt = 0; attempt < activationAttempts; attempt++) {
      const activation = await readFeatureFlagActivationThroughBackOffice(page, [flagKey]);
      if (activation[flagKey]) break;

      await activateFeatureFlagThroughBackOffice(page, flagKey);
    }

    const activation = await readFeatureFlagActivationThroughBackOffice(page, [flagKey]);
    expect(activation[flagKey], `Activating '${flagKey}' through the back office`).toBe(true);
  }
}

/**
 * Whether each of the given flags is globally active, read through the back-office API. The snapshot the precondition
 * takes and the state the teardown asserts.
 * @param page Playwright page instance of a signed-in back-office administrator
 * @param flagKeys The registry keys to report on
 */
export async function readFeatureFlagActivationThroughBackOffice(page: Page, flagKeys: string[]): Promise<Record<string, boolean>> {
  const flags = await page.evaluate(async () => {
    const response = await fetch("/api/back-office/feature-flags", { credentials: "same-origin" });
    return { status: response.status, body: await response.text() };
  });

  expect(flags.status, flags.body).toBe(200);
  const { flags: rows } = JSON.parse(flags.body) as { flags: { key: string; isActive: boolean }[] };
  return Object.fromEntries(flagKeys.map((flagKey) => [flagKey, rows.find((row) => row.key === flagKey)?.isActive === true]));
}

/**
 * One activation through the flag detail of the Blazor back office, whose client carries the antiforgery token the write
 * needs. The outcome is not asserted: a flag another project activated after the last read offers no Activate button, and a
 * collision with another project's activation is answered with a 500; both are resolved by reading the state again.
 */
async function activateFeatureFlagThroughBackOffice(page: Page, flagKey: string): Promise<void> {
  await openFeatureFlagDetailInBackOffice(page, flagKey);
  if ((await page.getByTestId("feature-flag-activate").count()) === 0) return;

  await confirmFeatureFlagActionInBackOffice(page, "activate");
}

async function readConfigurableFlags(page: Page, path: string): Promise<Record<string, boolean>> {
  const response = await sendAccountApiRequest(page, "GET", path);
  expect(response.status, response.body).toBe(200);

  const { flags } = JSON.parse(response.body) as { flags: { flagKey: string; enabled: boolean }[] };
  return Object.fromEntries(flags.map((flag) => [flag.flagKey, flag.enabled]));
}
