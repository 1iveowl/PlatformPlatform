import { expect } from "@playwright/test";
import { test, trackWebAssemblyRequests } from "@blazor/e2e/authentication";
import { expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

test.describe("@smoke", () => {
  /**
   * The server-rendered landing page and the public navigation for an anonymous visitor:
   * - Landing content only the Blazor host renders, so the test cannot pass against the React landing page
   * - Enhanced navigation to login, signup and terms and back to the landing page
   * - Direct loads of the public pages
   * - The link into the authenticated surface sends an anonymous visitor to login
   * - No WebAssembly runtime request on any of them
   */
  test("should display landing page with navigation for unauthenticated users", async ({ page }) => {
    createTestContext(page);
    const runtimeRequests = trackWebAssemblyRequests(page);

    await step("Navigate to the Blazor root & verify landing content and navigation")(async () => {
      await gotoBlazor(page);

      await expect(page.getByRole("heading", { name: "Welcome" })).toBeVisible();
      await expect(page.getByTestId("landing-text")).toHaveText(
        "A server-rendered public page. No WebAssembly runtime is downloaded here."
      );
      // The FluentUI label's shadow root adds a slot for a required marker, so its text is matched by containment
      await expect(page.getByTestId("fluent-label")).toContainText("Rendered by the host with a FluentUI component");
      await expect(page.getByTestId("public-nav").getByRole("link")).toHaveText([
        "Home",
        "Log in",
        "Sign up",
        "Terms",
        "Open the app"
      ]);
    })();

    await step("Follow the public navigation links & verify each page renders")(async () => {
      await page.getByTestId("nav-login").click();
      await expectBlazorUrl(page, "login");
      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();

      await page.getByTestId("nav-signup").click();
      await expectBlazorUrl(page, "signup");
      await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();

      await page.getByTestId("nav-terms").click();
      await expectBlazorUrl(page, "legal/terms");

      await page.getByTestId("nav-landing").click();
      await expectBlazorUrl(page);
      await expect(page.getByTestId("landing-text")).toBeVisible();
    })();

    await step("Load the public pages directly & verify no WebAssembly runtime request")(async () => {
      await gotoBlazor(page, "login");
      await gotoBlazor(page, "signup");
      await gotoBlazor(page, "legal/terms");
      await gotoBlazor(page);

      expect(runtimeRequests).toEqual([]);
    })();

    await step("Open the app while anonymous & verify redirect to login")(async () => {
      await page.getByTestId("nav-app").click();

      await expectBlazorUrl(page, "login");
      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();
    })();
  });
});
