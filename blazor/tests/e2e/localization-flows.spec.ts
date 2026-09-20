import { expect, type Page } from "@playwright/test";
import { openUserMenu, signUpThroughBlazor, startEmailFlowThroughBlazor, test } from "@blazor/e2e/authentication";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { newBlazorContext } from "@blazor/e2e/sessions";
import { expectAppliedTheme, expectZoomLevel } from "@blazor/e2e/shell";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts, otherBlazorTexts, type BlazorCulture } from "@blazor/e2e/texts";
import { blazorToast } from "@blazor/e2e/toast";
import { getBaseUrl } from "@shared/e2e/utils/constants";
import { assertNoUnexpectedErrors, createTestContext, expectNetworkErrors } from "@shared/e2e/utils/test-assertions";
import { getVerificationCode } from "@shared/e2e/utils/test-data";
import { step } from "@shared/e2e/utils/test-step-wrapper";

/**
 * The cookie the public language menu and a confirmed language change write, read by the host before Accept-Language
 */
const preferredLocaleCookieName = "preferred-locale";

/**
 * The account API's language change, as a route pattern for failure injection
 */
const changeLocaleRoute = "**/api/account/users/me/change-locale";

async function getPreferredLocaleCookie(page: Page): Promise<string | undefined> {
  return (await page.context().cookies(getBaseUrl())).find((cookie) => cookie.name === preferredLocaleCookieName)?.value;
}

/**
 * Choose a language from the public navigation's language menu, which writes the cookie and loads the page again
 * @param page Playwright page instance on a public Blazor page rendered in the culture of `current`
 * @param current The text map of the culture the page renders in
 * @param chosen The text map of the language to choose
 */
async function chooseLanguageFromPublicMenu(page: Page, current: BlazorCulture, chosen: BlazorCulture): Promise<void> {
  await page.getByRole("button", { name: current.changeLanguage, exact: true }).click();
  const menu = page.getByRole("menu", { name: current.changeLanguage, exact: true });
  await expect(menu).toBeVisible();
  await menu.getByRole("menuitemradio", { name: chosen.languageName, exact: true }).dispatchEvent("click");

  await expect(page.locator("html")).toHaveAttribute("lang", chosen.locale);
}

/**
 * The radio of one choice on the preferences page, inside the group named by its section heading
 * @param page Playwright page instance on the preferences page
 * @param group The localized name of the group
 * @param choice The localized name of the choice
 */
function preferenceChoice(page: Page, group: string, choice: string) {
  return page.getByRole("radiogroup", { name: group, exact: true }).getByRole("radio", { name: choice, exact: true });
}

/**
 * Expect the document to render in a culture: the lang attribute and the user menu's localized name
 * @param page Playwright page instance on an interactive authenticated Blazor page
 * @param culture The text map of the expected culture
 */
async function expectWorkspaceCulture(page: Page, culture: BlazorCulture): Promise<void> {
  await expect(page.locator("html")).toHaveAttribute("lang", culture.locale);
  await expect(page.getByRole("button", { name: culture.userMenu, exact: true })).toBeVisible();
}

test.describe("@smoke", () => {
  /**
   * The preferences page, in the culture of the running project:
   * - Preferences opens from the user menu with the theme, language and zoom groups and the notifications section
   * - Dark applies at once with its toast; Larger applies at once with its toast and scales the root font size to 20px
   * - The Arrow keys move the zoom choice to Large, which applies at once
   * - Choosing the other language saves it and loads the page again in that language, notifications section included, with the cookie written
   * - A reload keeps the theme, the zoom level and the language
   * - Zoom is applied through a data attribute and external CSS: no policy violation and no style attribute
   */
  test("should change theme, zoom and language on the preferences page and keep them across a reload", async ({ page }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const other = otherBlazorTexts();
    await trackPolicyViolations(page);

    await step("Sign up and open Preferences from the user menu & verify the three preference groups")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail());

      await openUserMenu(page);
      await page.getByRole("menuitem", { name: texts.preferences, exact: true }).dispatchEvent("click");

      await expectBlazorUrl(page, "user/preferences");
      await expect(page.getByRole("heading", { name: texts.userPreferences, exact: true, level: 1 })).toBeVisible();
      await expect(preferenceChoice(page, texts.theme, texts.themeSystem)).toBeChecked();
      await expect(preferenceChoice(page, texts.language, texts.languageName)).toBeChecked();
      await expect(preferenceChoice(page, texts.zoom, texts.zoomDefault)).toBeChecked();
      await expect(page.getByRole("heading", { name: texts.notifications, exact: true, level: 2 })).toBeVisible();
      await expect(page.getByText(texts.notificationsSectionDescription, { exact: true })).toBeVisible();
      await expect(page.getByRole("switch", { name: texts.notificationsOnThisDevice, exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: texts.sendTestNotification, exact: true })).toBeVisible();
    })();

    await step("Choose the dark theme and the larger zoom & verify both apply at once with their toasts")(async () => {
      await preferenceChoice(page, texts.theme, texts.themeDark).click();
      await expect(blazorToast(page, texts.themeUpdated)).toBeVisible();

      await preferenceChoice(page, texts.zoom, texts.zoomLarger).click();

      await expect(blazorToast(page, texts.zoomLevelUpdated)).toBeVisible();
      await expectAppliedTheme(page, "dark");
      await expectZoomLevel(page, "1.25");
      await expect(page.locator("html")).toHaveCSS("font-size", "20px");
      await expectNoPolicyViolations(page);
    })();

    await step("Move the zoom choice with the Arrow Left key & verify the large zoom applies")(async () => {
      await preferenceChoice(page, texts.zoom, texts.zoomLarger).focus();

      await page.keyboard.press("ArrowLeft");

      await expect(preferenceChoice(page, texts.zoom, texts.zoomLarge)).toBeChecked();
      await expectZoomLevel(page, "1.125");
      await expect(page.locator("html")).toHaveCSS("font-size", "18px");
    })();

    await step("Choose the other language & verify the page loads again in that language with the cookie written")(async () => {
      await preferenceChoice(page, texts.language, other.languageName).click();

      await expect(page.getByRole("heading", { name: other.userPreferences, exact: true, level: 1 })).toBeVisible();
      await expect(page.locator("html")).toHaveAttribute("lang", other.locale);
      await expect(preferenceChoice(page, other.language, other.languageName)).toBeChecked();
      await expect(page.getByRole("heading", { name: other.notifications, exact: true, level: 2 })).toBeVisible();
      await expect(page.getByRole("switch", { name: other.notificationsOnThisDevice, exact: true })).toBeVisible();
      expect(await getPreferredLocaleCookie(page)).toBe(other.locale);
    })();

    await step("Reload the preferences page & verify theme, zoom and language are kept without policy violations")(async () => {
      await page.reload();

      await expect(page.getByRole("heading", { name: other.userPreferences, exact: true, level: 1 })).toBeVisible();
      await expect(preferenceChoice(page, other.theme, other.themeDark)).toBeChecked();
      await expect(preferenceChoice(page, other.zoom, other.zoomLarge)).toBeChecked();
      await expectAppliedTheme(page, "dark");
      await expectZoomLevel(page, "1.125");
      await expect(page.locator("html")).toHaveAttribute("lang", other.locale);
      await expectNoPolicyViolations(page);
    })();
  });
});

test.describe("@comprehensive", () => {
  /**
   * Language across the anonymous and authenticated surfaces and across users, in the culture of the running project
   * (the project's language) and the other supported culture:
   * - The public language menu on the signup page switches to the other language and writes the preferred-locale cookie
   * - Signup and the welcome setup complete in the other language, which the new user keeps as their language
   * - Logout keeps the other language on the login page; the login page's menu switches back to the project's language
   * - Logging in on that page renders the workspace in the user's saved language, not the cookie's
   * - A second user signed up in the project's language keeps it, and the first user logging in from a fresh browser
   *   gets their own language, so the language belongs to the user
   * - A refused language change keeps the saved language selected, the document language and the cookie unchanged, and
   *   the server's language survives a reload
   * - While a language change is pending the language choices are disabled, so changes cannot overlap; once it is
   *   confirmed the cookie, the document language and the saved language agree after the reload
   * - A malformed preferred-locale cookie and a browser whose storage throws fall back to the browser language and the
   *   default zoom
   */
  test("should keep the language across signup, logout, login, users and refused or pending changes", async ({ page, browser }) => {
    const context = createTestContext(page);
    const texts = blazorTexts();
    const other = otherBlazorTexts();
    const firstEmail = uniqueBlazorEmail();
    const secondEmail = uniqueBlazorEmail();

    // === ANONYMOUS LANGUAGE MENU ===
    await step("Open the signup page and choose the other language from the language menu & verify the page and the cookie")(async () => {
      await gotoBlazor(page, "signup");
      await expect(page.getByRole("heading", { name: texts.createYourAccount })).toBeVisible();

      await chooseLanguageFromPublicMenu(page, texts, other);

      await expect(page.getByRole("heading", { name: other.createYourAccount })).toBeVisible();
      expect(await getPreferredLocaleCookie(page)).toBe(other.locale);
    })();

    await step("Sign up and complete the welcome setup in the other language & verify the workspace in that language")(async () => {
      await page.getByLabel(other.email, { exact: true }).fill(firstEmail);
      await page.getByRole("button", { name: other.signUpWithEmail, exact: true }).click();
      await expectBlazorUrl(page, "signup/verify");
      await expect(page.getByRole("heading", { name: other.enterYourVerificationCode })).toBeVisible();

      await page.getByLabel(other.signupVerificationCode, { exact: true }).fill(getVerificationCode());
      await expectBlazorUrl(page, "welcome");
      await page.getByLabel(other.accountName, { exact: true }).fill("Localization account");
      await page.getByRole("button", { name: other.continue, exact: true }).click();
      await page.getByLabel(other.firstName, { exact: true }).fill("Blazor");
      await page.getByLabel(other.lastName, { exact: true }).fill("User");
      await page.getByRole("button", { name: other.continue, exact: true }).click();

      await expectBlazorUrl(page, "app");
      await expectWorkspaceCulture(page, other);
    })();

    await step("Log out from the workspace in the other language & verify the login page keeps that language")(async () => {
      await page.getByRole("button", { name: other.userMenu, exact: true }).click();
      await expect(page.getByRole("menu", { name: other.userMenu, exact: true })).toBeVisible();

      await page.getByRole("menuitem", { name: other.logOut, exact: true }).click();

      await expectBlazorUrl(page, "login");
      await expect(page.getByRole("heading", { name: other.hiWelcomeBack })).toBeVisible();
    })();

    await step("Switch the login page to the project's language and log in & verify the workspace uses the user's saved language")(async () => {
      await chooseLanguageFromPublicMenu(page, other, texts);
      await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
      expect(await getPreferredLocaleCookie(page)).toBe(texts.locale);

      await startEmailFlowThroughBlazor(page, "login", firstEmail);
      await page.getByLabel(texts.loginVerificationCode, { exact: true }).fill(getVerificationCode());

      await expectBlazorUrl(page, "app");
      await expectWorkspaceCulture(page, other);
    })();

    // === PER-USER LANGUAGE ===
    await step("Sign up a second user in the project's language and log the first user in from a fresh browser & verify each keeps their own language")(async () => {
      const secondContext = await newBlazorContext(browser);
      const secondPage = await secondContext.newPage();
      const secondTestContext = createTestContext(secondPage);
      const thirdContext = await newBlazorContext(browser);
      const thirdPage = await thirdContext.newPage();
      const thirdTestContext = createTestContext(thirdPage);

      await signUpThroughBlazor(secondPage, secondEmail);
      await gotoBlazor(thirdPage, "login");
      await startEmailFlowThroughBlazor(thirdPage, "login", firstEmail);
      await thirdPage.getByLabel(texts.loginVerificationCode, { exact: true }).fill(getVerificationCode());

      await expectWorkspaceCulture(secondPage, texts);
      await expectBlazorUrl(thirdPage, "app");
      await expectWorkspaceCulture(thirdPage, other);
      await assertNoUnexpectedErrors(secondTestContext);
      await assertNoUnexpectedErrors(thirdTestContext);
      await secondContext.close();
      await thirdContext.close();
    })();

    // === REFUSED AND PENDING CHANGES ===
    await step("Choose the project's language while the account API refuses the change & verify nothing changes")(async () => {
      await gotoBlazor(page, "user/preferences");
      await expect(page.getByRole("heading", { name: other.userPreferences, exact: true, level: 1 })).toBeVisible();
      await page.route(changeLocaleRoute, (route) => route.fulfill({ status: 500, contentType: "application/problem+json", body: "{}" }));

      await preferenceChoice(page, other.language, texts.languageName).click();

      await expectNetworkErrors(context, [500]);
      await expect(page.getByRole("region", { name: other.notifications, exact: true }).getByRole("alert")).toBeVisible();
      await expect(preferenceChoice(page, other.language, other.languageName)).toBeChecked();
      await expect(preferenceChoice(page, other.language, texts.languageName)).toBeEnabled();
      await expect(page.locator("html")).toHaveAttribute("lang", other.locale);
      expect(await getPreferredLocaleCookie(page)).toBe(texts.locale);
      await page.unroute(changeLocaleRoute);
      await page.reload();
      await expect(page.getByRole("heading", { name: other.userPreferences, exact: true, level: 1 })).toBeVisible();
    })();

    await step("Choose the project's language while the change is held & verify the choices are disabled until it is confirmed")(async () => {
      const changeRequests: string[] = [];
      let releaseChange: () => void = () => {};
      const changeReleased = new Promise<void>((resolve) => {
        releaseChange = resolve;
      });
      await page.route(changeLocaleRoute, async (route) => {
        changeRequests.push(route.request().method());
        await changeReleased;
        await route.continue();
      });

      await preferenceChoice(page, other.language, texts.languageName).click();

      await expect(preferenceChoice(page, other.language, other.languageName)).toBeDisabled();
      await expect(preferenceChoice(page, other.language, texts.languageName)).toBeDisabled();
      // Polled: the choices are disabled the moment the change starts, which is before the request leaves the runtime
      await expect.poll(() => changeRequests).toEqual(["PUT"]);
      releaseChange();
      await expect(page.getByRole("heading", { name: texts.userPreferences, exact: true, level: 1 })).toBeVisible();
      await expect(page.locator("html")).toHaveAttribute("lang", texts.locale);
      await expect(preferenceChoice(page, texts.language, texts.languageName)).toBeChecked();
      expect(await getPreferredLocaleCookie(page)).toBe(texts.locale);
      expect(changeRequests).toEqual(["PUT"]);
      await page.unroute(changeLocaleRoute);
    })();

    // === FALLBACKS ===
    await step("Open the login page with a malformed language cookie and a throwing storage & verify the browser language and default zoom")(async () => {
      const fallbackContext = await newBlazorContext(browser);
      await fallbackContext.addCookies([{ name: preferredLocaleCookieName, value: "xx-XX", url: getBaseUrl() }]);
      await fallbackContext.addInitScript(() => {
        Object.defineProperty(window, "localStorage", {
          configurable: true,
          get: () => {
            throw new DOMException("Storage is denied.", "SecurityError");
          }
        });
      });
      const fallbackPage = await fallbackContext.newPage();
      const fallbackTestContext = createTestContext(fallbackPage);
      await trackPolicyViolations(fallbackPage);

      await gotoBlazor(fallbackPage, "login");

      await expect(fallbackPage.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
      await expect(fallbackPage.locator("html")).toHaveAttribute("lang", texts.locale);
      await expect(fallbackPage.locator("html")).toHaveCSS("font-size", "16px");
      await expectNoPolicyViolations(fallbackPage);
      await assertNoUnexpectedErrors(fallbackTestContext);
      await fallbackContext.close();
    })();
  });
});
