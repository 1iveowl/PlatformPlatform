import { expect } from "@playwright/test";
import { test } from "@blazor/e2e/authentication";
import { gotoBlazor } from "@blazor/e2e/routes";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

test.describe("@smoke", () => {
  /**
   * Opens the Blazor root through the gateway and asserts content only the Blazor host renders, so the test cannot pass
   * against the React landing page
   */
  test("should render the server-rendered Blazor landing page under the path base", async ({ page }) => {
    createTestContext(page);

    await step("Navigate to the Blazor root & verify Blazor landing content")(async () => {
      await gotoBlazor(page);

      await expect(page.getByRole("heading", { name: "Welcome" })).toBeVisible();
      await expect(page.getByTestId("landing-text")).toHaveText(
        "A server-rendered public page. No WebAssembly runtime is downloaded here."
      );
      // The FluentUI label's shadow root adds a slot for a required marker, so its text is matched by containment
      await expect(page.getByTestId("fluent-label")).toContainText("Rendered by the host with a FluentUI component");
    })();
  });
});
