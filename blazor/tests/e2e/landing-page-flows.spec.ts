import { expect, type Page } from "@playwright/test";
import { signUpThroughBlazor, test, trackWebAssemblyRequests } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, blazorUrl, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { expectAppliedTheme } from "@blazor/e2e/shell";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts, otherBlazorTexts, type BlazorCulture } from "@blazor/e2e/texts";
import { createTestContext, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

declare global {
  interface Window {
    __markdownProbe?: string;
  }
}

/**
 * The language of the published legal documents, which are English inside chrome of any culture
 */
const legalDocumentLanguage = "en-US";

/**
 * The text of the terms document's link to the privacy document, English in every culture as the document is
 */
const privacyLinkInTerms = "Privacy Policy";

/**
 * Choose a language from the public navigation's language menu, which writes the preferred-locale cookie and loads the
 * page again in that language
 * @param page Playwright page instance on a public Blazor page rendered in the culture of `current`
 * @param current The text map of the culture the page renders in
 * @param chosen The text map of the language to choose
 */
async function chooseLanguage(page: Page, current: BlazorCulture, chosen: BlazorCulture): Promise<void> {
  await page.getByRole("button", { name: current.changeLanguage, exact: true }).click();
  const menu = page.getByRole("menu", { name: current.changeLanguage, exact: true });
  await expect(menu).toBeVisible();
  await menu.getByRole("menuitemradio", { name: chosen.languageName, exact: true }).dispatchEvent("click");

  await expect(page.locator("html")).toHaveAttribute("lang", chosen.locale);
}

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

test.describe("@comprehensive", () => {
  /**
   * The rest of the public surface for an anonymous visitor, and what an authenticated one sees instead:
   * - The legal index and the three documents on a direct load: the level-one heading, the table or list each document
   *   renders, and the English text marked with its own language inside chrome of the running culture
   * - Browser Back and Forward across the legal routes
   * - A path naming no published document, the two internal documents included, is not found
   * - The renderer's policy in a browser, on the Development fixture rather than on a published document: the markup and
   *   the executable destinations of a hostile document are text, and none of its scripts run
   * - The theme and the language control of the public navigation
   * - No WebAssembly runtime request and no policy violation on any of them
   * - An authenticated visitor is sent to the workspace and never sees the landing content
   */
  test("should render the legal pages, refuse unpublished documents and offer the public controls", async ({ page }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    const other = otherBlazorTexts();
    const runtimeRequests = trackWebAssemblyRequests(page);
    await trackPolicyViolations(page);
    const legalDocument = () => page.getByRole("article");

    // === THE LEGAL INDEX AND THE THREE DOCUMENTS ===
    await step("Load the legal index directly & verify its heading, sections and document links")(async () => {
      await gotoBlazor(page, "legal");

      await expect(page.getByRole("heading", { name: texts.legalAndCompliance, level: 1 })).toBeVisible();
      // The index names each document in the page's own culture, and a card link is named by its whole content, the title
      // and its description, so the title is a substring of that name
      await expect(page.getByRole("link", { name: texts.termsTitle })).toHaveAttribute("href", blazorPath("legal/terms"));
      await expect(page.getByRole("link", { name: texts.privacyTitle })).toHaveAttribute("href", blazorPath("legal/privacy"));
      await expect(page.getByRole("link", { name: texts.dpaTitle })).toHaveAttribute("href", blazorPath("legal/dpa"));
      await expectNoPolicyViolations(page);
    })();

    await step("Load the terms document directly & verify its heading, its list and the English text inside the page's chrome")(async () => {
      await gotoBlazor(page, "legal/terms");

      await expect(page.getByRole("heading", { name: texts.termsOfService, level: 1 })).toBeVisible();
      await expect(legalDocument()).toHaveAttribute("lang", legalDocumentLanguage);
      await expect(legalDocument().getByRole("list").first()).toBeVisible();
      await expect(legalDocument().getByRole("link", { name: privacyLinkInTerms, exact: true }).first()).toHaveAttribute("href", blazorPath("legal/privacy"));
      await expect(page.locator("html")).toHaveAttribute("lang", texts.locale);
      await expect(page.getByRole("contentinfo").getByRole("link", { name: texts.dpa, exact: true })).toBeVisible();
      await expectNoPolicyViolations(page);
    })();

    await step("Load the privacy document directly & verify its heading and the table it renders")(async () => {
      await gotoBlazor(page, "legal/privacy");

      await expect(page.getByRole("heading", { name: texts.privacyPolicy, level: 1 })).toBeVisible();
      await expect(legalDocument()).toHaveAttribute("lang", legalDocumentLanguage);
      await expect(legalDocument().getByRole("table").first()).toBeVisible();
      await expectNoPolicyViolations(page);
    })();

    await step("Load the data processing agreement directly & verify its heading and its list")(async () => {
      await gotoBlazor(page, "legal/dpa");

      await expect(page.getByRole("heading", { name: texts.dataProcessingAgreement, level: 1 })).toBeVisible();
      await expect(legalDocument()).toHaveAttribute("lang", legalDocumentLanguage);
      await expect(legalDocument().getByRole("list").first()).toBeVisible();
      await expectNoPolicyViolations(page);
    })();

    // === BROWSER HISTORY ACROSS THE LEGAL ROUTES ===
    await step("Go back through the browser history & verify the privacy document renders again")(async () => {
      await page.goBack();

      await expectBlazorUrl(page, "legal/privacy");
      await expect(page.getByRole("heading", { name: texts.privacyPolicy, level: 1 })).toBeVisible();
      await expectNoPolicyViolations(page);
    })();

    await step("Go forward through the browser history & verify the data processing agreement renders again")(async () => {
      await page.goForward();

      await expectBlazorUrl(page, "legal/dpa");
      await expect(page.getByRole("heading", { name: texts.dataProcessingAgreement, level: 1 })).toBeVisible();
      await expectNoPolicyViolations(page);
    })();

    // === PATHS THAT NAME NO PUBLISHED DOCUMENT ===
    await step("Load an unknown key and the two internal documents & verify each is not found and shows no document")(async () => {
      const unknownKey = await page.goto(blazorPath("legal/unknown"));
      const crossReferences = await page.goto(blazorPath("legal/cross-references.internal"));
      const legitimateInterest = await page.goto(blazorPath("legal/legitimate-interest-assessment.internal.en-US"));

      expect([unknownKey!.status(), crossReferences!.status(), legitimateInterest!.status()]).toEqual([404, 404, 404]);
      await expect(page.getByRole("heading", { name: texts.pageNotFound, level: 1 })).toBeVisible();
      await expect(page.getByRole("article")).toHaveCount(0);
      await expectNetworkErrors(context, [404]);
      await expectNoPolicyViolations(page);
    })();

    // === THE RENDERER'S POLICY IN A BROWSER ===
    await step("Load the Development renderer fixture & verify the hostile markup is text and none of its scripts ran")(async () => {
      await gotoBlazor(page, "development/markdown-probe");

      await expect(legalDocument()).toContainText("<script>window.__markdownProbe");
      await expect(legalDocument()).toContainText("<iframe src=");
      // The absence of elements the renderer must never emit; none of them has a role or a name to select it by
      await expect(legalDocument().locator("script, style, svg, img, iframe, form, a")).toHaveCount(0);
      expect(await page.evaluate(() => window.__markdownProbe)).toBeUndefined();
      await expectNoPolicyViolations(page);
    })();

    // === THE PUBLIC CONTROLS ===
    await step("Choose the dark theme from the public navigation & verify it applies without a style attribute")(async () => {
      await gotoBlazor(page);
      await page.getByRole("button", { name: texts.changeTheme, exact: true }).click();
      const themeMenu = page.getByRole("menu", { name: texts.changeTheme, exact: true });
      await expect(themeMenu).toBeVisible();

      await themeMenu.getByRole("menuitemradio", { name: texts.themeDark, exact: true }).dispatchEvent("click");

      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);
    })();

    await step("Choose the other language from the public navigation and choose back & verify the landing page in each")(async () => {
      await chooseLanguage(page, texts, other);

      await expect(page.getByRole("heading", { name: other.welcomeToProduct, exact: true })).toBeVisible();
      await expect(page.getByText(other.landingNote, { exact: true })).toBeVisible();
      await chooseLanguage(page, other, texts);
      await expect(page.getByRole("heading", { name: texts.welcomeToProduct, exact: true })).toBeVisible();
      await expectAppliedTheme(page, "dark");
      await expectNoPolicyViolations(page);
      expect(runtimeRequests).toEqual([]);
    })();

    // === THE AUTHENTICATED VISITOR ===
    await step("Sign up and open the landing page again & verify the workspace replaces the landing content")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail(), "Landing account");

      await page.goto(blazorPath());

      await expectBlazorUrl(page, "app");
      await expect(page.getByRole("heading", { name: texts.yourWorkspace, exact: true, level: 1 })).toBeVisible();
      await expect(page.getByRole("heading", { name: texts.welcomeToProduct, exact: true })).toHaveCount(0);
      await expect(page.getByRole("link", { name: texts.getStarted, exact: true })).toHaveCount(0);
    })();
  });
});
