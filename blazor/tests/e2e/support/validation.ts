import { expect, type Page } from "@playwright/test";

/**
 * Expect a field message rendered by the Blazor ValidationMessage component (div.validation-message)
 * @param page Playwright page instance
 * @param message The expected validation message
 */
export async function expectBlazorValidationMessage(page: Page, message: string): Promise<void> {
  await expect(page.locator("div.validation-message").filter({ hasText: message })).toBeVisible();
}

/**
 * Expect a form-level message rendered by FormErrorAlert (data-testid="form-error-message")
 * @param page Playwright page instance
 * @param message The expected form error message
 */
export async function expectBlazorFormError(page: Page, message: string): Promise<void> {
  await expect(page.getByTestId("form-error").getByTestId("form-error-message").filter({ hasText: message })).toBeVisible();
}
