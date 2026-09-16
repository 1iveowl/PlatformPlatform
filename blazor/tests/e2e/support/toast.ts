import { expect, type Locator, type Page } from "@playwright/test";
import { blazorTexts } from "./texts";

/**
 * The toast with the given title in the Blazor ToastRegion
 * @param page Playwright page instance on an interactive Blazor surface
 * @param title The toast's title
 */
export function blazorToast(page: Page, title: string): Locator {
  return page
    .getByRole("region", { name: blazorTexts().notifications, exact: true })
    .getByRole("alert")
    .filter({ has: page.getByText(title, { exact: true }) });
}

/**
 * Dismiss a toast and expect it to be gone
 * @param page Playwright page instance on an interactive Blazor surface
 * @param toast The toast, as returned by blazorToast
 */
export async function dismissBlazorToast(page: Page, toast: Locator): Promise<void> {
  await toast.getByRole("button", { name: blazorTexts().dismissNotification, exact: true }).click();

  await expect(toast).toHaveCount(0);
}

/**
 * Expect exactly one toast with the given title and message in the Blazor ToastRegion, then dismiss it and expect the
 * region to be empty again
 * @param page Playwright page instance on an interactive Blazor surface
 * @param toast The toast's title and message
 */
export async function expectBlazorToast(page: Page, toast: { title: string; message: string }): Promise<void> {
  const region = page.getByRole("region", { name: blazorTexts().notifications, exact: true });
  const toastElement = blazorToast(page, toast.title);

  await expect(region.getByRole("alert")).toHaveCount(1);
  await expect(toastElement.getByText(toast.message, { exact: true })).toBeVisible();

  await dismissBlazorToast(page, toastElement);

  await expect(region.getByRole("alert")).toHaveCount(0);
}
