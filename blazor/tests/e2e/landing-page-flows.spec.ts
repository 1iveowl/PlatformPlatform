import { expect } from "@playwright/test";
import { test, trackWebAssemblyRequests } from "@blazor/e2e/authentication";
import { expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { blazorTexts } from "@blazor/e2e/texts";
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
    const texts = blazorTexts();
    const runtimeRequests = trackWebAssemblyRequests(page);
    const navigationLink = (name: string) => page.getByRole("navigation").getByRole("link", { name, exact: true });

    await step("Navigate to the Blazor root & verify landing content and navigation")(async () => {
      await gotoBlazor(page);

      await expect(page.getByRole("heading", { name: texts.welcome, exact: true })).toBeVisible();
      await expect(page.getByText(texts.landingText, { exact: true })).toBeVisible();
      // The FluentUI label's shadow root adds a slot for a required marker, so its text is matched by containment
      await expect(page.getByTestId("fluent-label")).toContainText(texts.landingComponentText);
      await expect(page.getByRole("navigation").getByRole("link")).toHaveText([texts.home, texts.logIn, texts.signUp, texts.terms, texts.openTheApp]);
    })();

    await step("Follow the public navigation links & verify each page renders")(async () => {
      await navigationLink(texts.logIn).click();
      await expectBlazorUrl(page, "login");
      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();

      await navigationLink(texts.signUp).click();
      await expectBlazorUrl(page, "signup");
      await expect(page.getByRole("heading", { name: texts.createYourAccount })).toBeVisible();

      await navigationLink(texts.terms).click();
      await expectBlazorUrl(page, "legal/terms");

      await navigationLink(texts.home).click();
      await expectBlazorUrl(page);
      await expect(page.getByText(texts.landingText, { exact: true })).toBeVisible();
    })();

    await step("Load the public pages directly & verify no WebAssembly runtime request")(async () => {
      await gotoBlazor(page, "login");
      await gotoBlazor(page, "signup");
      await gotoBlazor(page, "legal/terms");
      await gotoBlazor(page);

      expect(runtimeRequests).toEqual([]);
    })();

    await step("Open the app while anonymous & verify redirect to login")(async () => {
      await navigationLink(texts.openTheApp).click();

      await expectBlazorUrl(page, "login");
      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
    })();
  });
});
