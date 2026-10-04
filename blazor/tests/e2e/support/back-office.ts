import { expect, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { getBackOfficeBaseUrl } from "@shared/e2e/utils/constants";
import { blazorPath } from "./routes";

/**
 * The mock identities of the local back-office login, which stands in for the platform authentication in front of the
 * back-office host
 */
export type BackOfficeIdentity = "admin" | "user";

/**
 * A signed-in back-office page of the Blazor edition, and the context to close when the test is over
 */
export interface BlazorBackOffice {
  context: BrowserContext;
  page: Page;
}

/**
 * Build the absolute URL of a Blazor route on the back-office host, where the account API's back-office listener forwards
 * the path base to the Blazor host
 * @param route Route below the path base
 */
export function blazorBackOfficeUrl(route: string): string {
  return `${getBackOfficeBaseUrl()}${blazorPath(route)}`;
}

/**
 * The back-office identity check below the dashboard
 */
export const backOfficeIdentityRoute = "back-office/identity";

/**
 * Open a Blazor back-office page in a context of its own, which lands on the mock login first, sign in as the given
 * identity and wait for the page's WebAssembly island to start. The mock login is the React edition's picker and is
 * English only, whatever the context's locale; the page itself renders in that locale.
 * @param browser The browser the test runs in
 * @param identity The mock identity to sign in as
 * @param route Route below the path base; the identity check unless given
 * @param locale The browser locale the page is requested in
 */
export async function openBlazorBackOffice(browser: Browser, identity: BackOfficeIdentity, route = backOfficeIdentityRoute, locale = "en-US"): Promise<BlazorBackOffice> {
  const context = await browser.newContext({ baseURL: getBackOfficeBaseUrl(), ignoreHTTPSErrors: true, locale });
  const page = await context.newPage();
  const backOfficeUrl = blazorBackOfficeUrl(route);

  await page.goto(backOfficeUrl);
  await expect(page).toHaveURL(/\/login\?returnPath=/);
  const radio = identity === "admin" ? page.getByRole("radio", { name: "Admin Log in with admin rights" }) : page.getByRole("radio", { name: /^User/ });
  await radio.click();
  await page.getByRole("button", { name: "Log in" }).click();

  await expect(page).toHaveURL(backOfficeUrl);
  if (route === backOfficeIdentityRoute) {
    await expect(page.getByTestId("render-mode")).toHaveText("Interactive: True");
  } else {
    await expect(page.getByTestId("back-office-shell")).toHaveAttribute("data-identity-state", "loaded");
  }
  return { context, page };
}

/**
 * Read an account's A/B inclusion pin through the back-office API, from the signed-in back-office document
 * @param page Playwright page instance of a signed-in back-office administrator
 * @param tenantId The id of the account
 */
export function readTenantAbInclusionPin(page: Page, tenantId: string): Promise<string | null> {
  return page.evaluate(async (id) => {
    const response = await fetch(`/api/back-office/tenants/${id}`, { credentials: "same-origin" });
    return ((await response.json()) as { abInclusionPin: string | null }).abInclusionPin;
  }, tenantId);
}

/**
 * Read a user's A/B inclusion pin as the account API stores it, through the back-office session of the page
 * @param page Playwright page instance signed in to the back office
 * @param userId The id of the user whose pin to read
 */
export function readUserAbInclusionPin(page: Page, userId: string): Promise<string | null> {
  return page.evaluate(async (id) => {
    const response = await fetch(`/api/back-office/users/${id}`, { credentials: "same-origin" });
    return ((await response.json()) as { abInclusionPin: string | null }).abInclusionPin;
  }, userId);
}
