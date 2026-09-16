import { expect, type Page } from "@playwright/test";

/**
 * Expect a field message rendered by the Blazor ValidationMessage component
 * @param page Playwright page instance
 * @param message The expected validation message
 */
export async function expectBlazorValidationMessage(page: Page, message: string): Promise<void> {
  await expect(page.getByText(message, { exact: true })).toBeVisible();
}

/**
 * Expect a form-level message rendered by FormErrorAlert, an alert holding one paragraph per message
 * @param page Playwright page instance
 * @param message The expected form error message
 */
export async function expectBlazorFormError(page: Page, message: string): Promise<void> {
  await expect(page.getByRole("alert").getByText(message, { exact: true })).toBeVisible();
}
