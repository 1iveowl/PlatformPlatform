import { expect, type Page } from "@playwright/test";
import { getBaseUrl } from "@shared/e2e/utils/constants";

/**
 * The path base the gateway serves the Blazor host under. Every Blazor URL in the tests is built from this one value,
 * so a root-absolute React route such as "/signup" can never be mistaken for a Blazor page.
 */
export const blazorPathBase = "/blazor";

/**
 * Build the root-absolute path of a Blazor route, for example "signup/verify" becomes "/blazor/signup/verify"
 * @param route Route below the path base, with or without a leading slash; empty for the Blazor root
 */
export function blazorPath(route = ""): string {
  return `${blazorPathBase}/${route.replace(/^\/+/, "")}`;
}

/**
 * Build the absolute URL of a Blazor route through the gateway
 * @param route Route below the path base; empty for the Blazor root
 */
export function blazorUrl(route = ""): string {
  return `${getBaseUrl()}${blazorPath(route)}`;
}

/**
 * Navigate to a Blazor route and assert the final URL is that route under the path base
 * @param page Playwright page instance
 * @param route Route below the path base; empty for the Blazor root
 */
export async function gotoBlazor(page: Page, route = ""): Promise<void> {
  await page.goto(blazorPath(route));

  await expectBlazorUrl(page, route);
}

/**
 * Assert the page is on a Blazor route under the path base, ignoring any query string
 * @param page Playwright page instance
 * @param route Route below the path base; empty for the Blazor root
 */
export async function expectBlazorUrl(page: Page, route = ""): Promise<void> {
  const expectedUrl = blazorUrl(route);

  await expect(page).toHaveURL((url) => `${url.origin}${url.pathname}` === expectedUrl);
}
