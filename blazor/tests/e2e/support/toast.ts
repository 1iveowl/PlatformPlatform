import { expect, type Page } from "@playwright/test";

/**
 * Test ids ApiFailurePresenter gives the toasts it shows in the in-house ToastRegion
 */
export const blazorToastTestIds = {
  apiFailure: "api-failure-toast",
  antiforgeryRecovery: "antiforgery-recovery-toast"
} as const;

interface BlazorToast {
  testId: string;
  title: string;
  message: string;
}

/**
 * Expect exactly one toast with the given title and message in the Blazor ToastRegion, then dismiss it and expect the
 * region to be empty again
 * @param page Playwright page instance on an interactive Blazor surface
 * @param toast The toast's test id, title and message
 */
export async function expectBlazorToast(page: Page, toast: BlazorToast): Promise<void> {
  const region = page.getByTestId("toast-region");
  const toastElement = region.getByTestId(toast.testId);

  await expect(region.getByRole("alert")).toHaveCount(1);
  await expect(toastElement.getByTestId("toast-title")).toHaveText(toast.title);
  await expect(toastElement.getByTestId("toast-message")).toHaveText(toast.message);

  await toastElement.getByTestId("toast-dismiss").click();

  await expect(region.getByRole("alert")).toHaveCount(0);
}
