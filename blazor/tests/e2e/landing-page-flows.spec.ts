import { expect } from "@playwright/test";
import { test, trackWebAssemblyRequests } from "@blazor/e2e/authentication";
import { blazorUrl, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { blazorTexts } from "@blazor/e2e/texts";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

test.describe("@smoke", () => {
  /**
   * The server-rendered landing page, its calls to action and the public chrome for an anonymous visitor:
   * - Landing content only the Blazor host renders, so the test cannot pass against the React landing page
   * - Enhanced navigation to login, signup and the legal documents and back to the landing page
   * - Direct loads of the public pages
   * - An anonymous visit to the authenticated surface ends at login
   * - No WebAssembly runtime request on any of them
   */
  test("should display landing page with navigation for unauthenticated users", async ({ page }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const runtimeRequests = trackWebAssemblyRequests(page);
    const navigationLink = (name: string) => page.getByRole("navigation").getByRole("link", { name, exact: true });
    const footerLink = (name: string) => page.getByRole("contentinfo").getByRole("link", { name, exact: true });

    await step("Navigate to the Blazor root & verify landing content, calls to action and the legal links")(async () => {
      await gotoBlazor(page);

      await expect(page.getByRole("heading", { name: texts.welcomeToProduct, exact: true })).toBeVisible();
      await expect(page.getByText(texts.landingText, { exact: true })).toBeVisible();
      await expect(page.getByText(texts.landingNote, { exact: true })).toBeVisible();
      await expect(page.getByRole("link", { name: texts.getStarted, exact: true })).toBeVisible();
      // "Log in" is both a navigation link and a call to action
      await expect(page.getByRole("link", { name: texts.logIn, exact: true })).toHaveCount(2);
      await expect(page.getByRole("navigation").getByRole("link")).toHaveText([texts.productName, texts.logIn, texts.signUp]);
      await expect(page.getByRole("contentinfo").getByRole("link")).toHaveText([texts.compliance, texts.terms, texts.privacy, texts.dpa]);
    })();

    await step("Follow the calls to action and the public links & verify each page renders")(async () => {
      await page.getByRole("link", { name: texts.getStarted, exact: true }).click();
      await expectBlazorUrl(page, "signup");
      await expect(page.getByRole("heading", { name: texts.createYourAccount })).toBeVisible();

      // The footer carries the legal links, and the login and signup forms have the navigation only
      await navigationLink(texts.productName).click();
      await expectBlazorUrl(page);
      await footerLink(texts.terms).click();
      await expectBlazorUrl(page, "legal/terms");
      await expect(page.getByRole("heading", { name: texts.termsOfService, level: 1 })).toBeVisible();

      await footerLink(texts.compliance).click();
      await expectBlazorUrl(page, "legal");
      await expect(page.getByRole("heading", { name: texts.legalAndCompliance, level: 1 })).toBeVisible();

      await navigationLink(texts.logIn).click();
      await expectBlazorUrl(page, "login");
      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();

      await navigationLink(texts.productName).click();
      await expectBlazorUrl(page);
      await expect(page.getByText(texts.landingText, { exact: true })).toBeVisible();
    })();

    await step("Load the public pages directly & verify no WebAssembly runtime request")(async () => {
      await gotoBlazor(page, "login");
      await gotoBlazor(page, "signup");
      await gotoBlazor(page, "legal");
      await gotoBlazor(page, "legal/terms");
      await gotoBlazor(page, "legal/privacy");
      await gotoBlazor(page, "legal/dpa");
      await gotoBlazor(page);

      expect(runtimeRequests).toEqual([]);
    })();

    await step("Open the authenticated surface while anonymous & verify redirect to login")(async () => {
      await page.goto(blazorUrl("app"));

      await expectBlazorUrl(page, "login");
      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
    })();
  });
});
