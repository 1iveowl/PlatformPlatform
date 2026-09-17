import { expect, type Page } from "@playwright/test";
import { sendAccountApiRequest } from "@blazor/e2e/account-api";
import {
  logOutThroughBlazor,
  signUpThroughBlazor,
  startEmailFlowThroughBlazor,
  test,
  userMenuButton
} from "@blazor/e2e/authentication";
import { submitOneTimePassword } from "@blazor/e2e/one-time-password";
import { blazorPath, blazorUrl, expectBlazorUrl } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts } from "@blazor/e2e/texts";
import { getBaseUrl } from "@shared/e2e/utils/constants";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

const mockProviderCookie = "__Test_Use_Mock_Provider";
const providerErrorDescription = "The user denied access";

/**
 * A random identifier for a mock provider identity or email prefix, unique per call
 */
function uniqueIdentifier(): string {
  return crypto.randomUUID().replaceAll("-", "").slice(0, 16);
}

async function setMockProviderCookie(page: Page, value: string): Promise<void> {
  await page.context().addCookies([{ name: mockProviderCookie, value, url: getBaseUrl() }]);
}

/**
 * Record the Location of every redirect a document navigation follows from now on
 * @param page Playwright page instance
 * @returns The live list of absolute redirect destinations
 */
function trackRedirects(page: Page): string[] {
  const locations: string[] = [];
  page.on("response", (response) => {
    const location = response.headers().location;
    if (response.request().isNavigationRequest() && response.status() >= 300 && response.status() < 400 && location) {
      locations.push(new URL(location, response.url()).toString());
    }
  });
  return locations;
}

/**
 * Expect every recorded redirect to lead either to an external authentication endpoint or into the Blazor path base,
 * never to a React page, and at least one to lead into the Blazor path base. The provider's own redirect to the callback may carry an
 * error description; a redirect into the Blazor path base never does
 * @param locations The redirects recorded by trackRedirects
 */
function expectRedirectsInsideBlazor(locations: string[]): void {
  expect(locations.length).toBeGreaterThan(0);
  for (const location of locations) {
    const url = new URL(location);
    expect(url.pathname.startsWith("/api/account/authentication/") || url.pathname.startsWith(blazorPath()), location).toBe(true);
  }
  const blazorRedirects = locations.filter((location) => new URL(location).pathname.startsWith(blazorPath()));
  expect(blazorRedirects.length).toBeGreaterThan(0);
  expect(blazorRedirects.filter((location) => location.includes("error_description"))).toEqual([]);
}

type Provider = "Google" | "Entra" | "MitId";

/**
 * The accessible name of a provider's button on the Blazor login or signup page in the running project's culture
 */
function providerButtonName(provider: Provider, flow: "login" | "signup"): string {
  const texts = blazorTexts();
  if (provider === "MitId") return texts.logOnWithMitId;
  if (flow === "login") return provider === "Google" ? texts.logInWithGoogle : texts.logInWithMicrosoft;
  return provider === "Google" ? texts.signUpWithGoogle : texts.signUpWithMicrosoft;
}

/**
 * Start an external authentication flow from the provider's button on the Blazor login or signup page, opened with the
 * return path the page passes on to the start endpoint
 */
async function startExternalFlow(page: Page, provider: Provider, flow: "login" | "signup", returnPath?: string): Promise<void> {
  const query = returnPath === undefined ? "" : `?returnPath=${encodeURIComponent(returnPath)}`;
  await page.goto(`${blazorPath(flow)}${query}`);

  await page.getByRole("button", { name: providerButtonName(provider, flow), exact: true }).click();
}

/**
 * Start an external authentication flow for the Blazor edition by navigating to the start endpoint directly, the way a
 * crafted link would, so the account API's own return path rule is exercised without the page's sanitising
 */
async function startExternalFlowByUrl(page: Page, provider: Provider, flow: "login" | "signup", returnPath: string): Promise<void> {
  const query = new URLSearchParams({ Edition: "Blazor", Locale: blazorTexts().locale, ReturnPath: returnPath });

  await page.goto(`${getBaseUrl()}/api/account/authentication/${provider}/${flow}/start?${query}`);
}

/**
 * Expect the Blazor error page for a refused external authentication, localized, with the reference id of the attempt and
 * no provider error description
 */
async function expectBlazorErrorPage(page: Page, errorCode: string, heading: string): Promise<void> {
  await expectBlazorUrl(page, "error");
  const url = new URL(page.url());
  expect(url.searchParams.get("error")).toBe(errorCode);
  const referenceId = url.searchParams.get("id");
  expect(referenceId).not.toBeNull();
  expect(url.searchParams.has("error_description")).toBe(false);

  await expect(page.getByRole("heading", { name: heading })).toBeVisible();
  await expect(page.getByTestId("error-reference-id")).toHaveText(`${blazorTexts().referenceId}${referenceId}`);
  const html = await page.content();
  expect(html).not.toContain(providerErrorDescription);
  expect(html).not.toContain("error_description");
  expect(html).not.toContain("mock-authorization-code");
}


/**
 * Sign up and log in with a provider for the Blazor edition, then prove hostile return paths are dropped for the Blazor
 * home and a provider denial lands on the localized Blazor error page; every redirect is recorded and asserted
 */
async function runProviderFlowsForBlazor(page: Page, provider: "Google" | "Entra"): Promise<void> {
  const texts = blazorTexts();
  const emailPrefix = uniqueIdentifier();
  const redirects = trackRedirects(page);

  await step(`Sign up with ${provider} for Blazor & name the account & verify the localized workspace`)(async () => {
    await setMockProviderCookie(page, emailPrefix);
    await startExternalFlow(page, provider, "signup");

    await expectBlazorUrl(page, "welcome");
    await page.getByLabel(texts.accountName, { exact: true }).fill("External account");
    await page.getByRole("button", { name: texts.continue, exact: true }).click();

    await expectBlazorUrl(page, "app");
    await expect(page.getByRole("heading", { name: texts.yourWorkspace })).toBeVisible();
    await expect(userMenuButton(page)).toBeVisible();
    expectRedirectsInsideBlazor(redirects);
  })();

  await step(`Log in with ${provider} and a deep link & verify return to the deep link`)(async () => {
    await logOutThroughBlazor(page);
    redirects.length = 0;

    await startExternalFlow(page, provider, "login", blazorPath("app/details"));

    await expectBlazorUrl(page, "app/details");
    await expect(userMenuButton(page)).toBeVisible();
    expectRedirectsInsideBlazor(redirects);
  })();

  await step(`Open login with a hostile return path & verify the ${provider} start carries the Blazor home`)(async () => {
    await logOutThroughBlazor(page);
    redirects.length = 0;

    await startExternalFlow(page, provider, "login", "/blazor/../dashboard");

    await expect(page).toHaveURL(blazorUrl("app"));
    await expect(userMenuButton(page)).toBeVisible();
    expectRedirectsInsideBlazor(redirects);
  })();

  await step(`Log in with ${provider} and hostile return paths at the start endpoint & verify the Blazor home each time`)(async () => {
    for (const hostile of ["/blazor/../dashboard", "/blazor/%2e%2e/dashboard", "//evil.example/blazor/app", "/blazor\\..\\dashboard", "/blazorx/app"]) {
      await logOutThroughBlazor(page);
      redirects.length = 0;

      await startExternalFlowByUrl(page, provider, "login", hostile);

      await expect(page).toHaveURL(blazorUrl("app"));
      await expect(userMenuButton(page)).toBeVisible();
      expectRedirectsInsideBlazor(redirects);
    }
  })();

  await step(`Deny access at ${provider} & verify the localized Blazor error page`)(async () => {
    await logOutThroughBlazor(page);
    redirects.length = 0;
    await setMockProviderCookie(page, "fail:access_denied");

    await startExternalFlow(page, provider, "login", blazorPath("app/details"));

    await expectBlazorErrorPage(page, "access_denied", texts.accessDenied);
    expectRedirectsInsideBlazor(redirects);
  })();

  await step(`Retry from the ${provider} denial & verify the localized login page`)(async () => {
    await page.getByTestId("error-login").click();

    await expectBlazorUrl(page, "login");
    await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
    await expect(page.getByRole("button", { name: providerButtonName(provider, "login"), exact: true })).toBeVisible();
  })();
}

/**
 * Drive every login and signup refusal the mock provider can produce through the Blazor buttons and expect its localized
 * page, reference id and actions: an unknown identity, an existing account, a provider without an email and a failed token
 * exchange. The actions lead to the Blazor login and signup pages.
 */
async function runRefusalsForBlazor(page: Page): Promise<void> {
  const texts = blazorTexts();
  const emailPrefix = uniqueIdentifier();
  const redirects = trackRedirects(page);
  const expectActions = async (actions: ("login" | "signup")[]) => {
    for (const action of ["login", "signup"] as const) {
      const link = page.getByTestId(`error-${action}`);
      if (actions.includes(action)) {
        await expect(link).toHaveAttribute("href", blazorPath(action));
      } else {
        await expect(link).toHaveCount(0);
      }
    }
  };

  await step("Log in with Google as an unknown identity & verify the account not found page leads to signup")(async () => {
    await page.context().clearCookies();
    await setMockProviderCookie(page, emailPrefix);
    redirects.length = 0;

    await startExternalFlow(page, "Google", "login");

    await expectBlazorErrorPage(page, "user_not_found", texts.accountNotFound);
    await expectActions(["signup", "login"]);
    expectRedirectsInsideBlazor(redirects);
    await page.getByTestId("error-signup").click();
    await expectBlazorUrl(page, "signup");
    await expect(page.getByRole("heading", { name: texts.createYourAccount })).toBeVisible();
  })();

  await step("Sign up with Google twice for one identity & verify the account already exists page")(async () => {
    await page.getByRole("button", { name: texts.signUpWithGoogle, exact: true }).click();
    await expectBlazorUrl(page, "welcome");
    await page.context().clearCookies();
    await setMockProviderCookie(page, emailPrefix);
    redirects.length = 0;

    await startExternalFlow(page, "Google", "signup");

    await expectBlazorErrorPage(page, "account_already_exists", texts.accountAlreadyExists);
    await expectActions(["login", "signup"]);
    expectRedirectsInsideBlazor(redirects);
  })();

  await step("Sign up with Google without an email & verify the email address required page")(async () => {
    await setMockProviderCookie(page, "noemail");
    redirects.length = 0;

    await startExternalFlow(page, "Google", "signup");

    await expectBlazorErrorPage(page, "email_not_provided", texts.emailNotProvided);
    await expectActions(["signup", "login"]);
    expectRedirectsInsideBlazor(redirects);
  })();

  await step("Log in with Google when the token exchange fails & verify the authentication failed page")(async () => {
    await setMockProviderCookie(page, "fail:token_exchange");
    redirects.length = 0;

    await startExternalFlow(page, "Google", "login");

    await expectBlazorErrorPage(page, "authentication_failed", texts.authenticationFailed);
    await expectActions(["login"]);
    expectRedirectsInsideBlazor(redirects);
  })();
}

/**
 * Start a MitID verification for the Blazor edition as the signed-in user through the account API, the request a Blazor
 * profile button will send, and follow the returned authorization URL
 */
async function verifyWithMitIdForBlazor(page: Page): Promise<void> {
  const response = await sendAccountApiRequest(page, "POST", "/api/account/authentication/MitId/verification/start?Edition=Blazor", {
    returnPath: blazorPath("user/profile")
  });
  expect(response.status, response.body).toBe(200);

  await page.goto((JSON.parse(response.body) as { authorizationUrl: string }).authorizationUrl);
}

test.describe("@smoke", () => {
  /**
   * Google and MitID flows started for the Blazor edition in the culture of the running project:
   * - Google signup lands on the Blazor welcome setup, whose profile step the provider names skip, and the workspace renders in the carried culture
   * - Flows start from the buttons on the Blazor login and signup pages; the MitID button carries the approved phrase, the wordmark and the brand geometry
   * - Google login returns to a deep link; hostile return paths are dropped for the Blazor home
   * - A Google denial lands on the localized Blazor error page without the provider's error description
   * - MitID verification returns to the Blazor profile; a low assurance verification lands on the Blazor error page
   * - MitID login with the verified identity returns to a deep link; an unbound identity lands on the Blazor error page
   * - Every redirect stays on external authentication endpoints or below the Blazor path base
   */
  test("should keep Google and MitID flows, their refusals and hostile return paths inside the Blazor edition", async ({ page }) => {
    createTestContext(page);

    // === GOOGLE ===
    await runProviderFlowsForBlazor(page, "Google");

    // === MITID ===
    const identity = `identity:${uniqueIdentifier()}`;
    const redirects = trackRedirects(page);

    await step("Sign up with email and verify with MitID for Blazor & verify return to the Blazor profile")(async () => {
      await page.context().clearCookies();
      await signUpThroughBlazor(page, uniqueBlazorEmail());
      await setMockProviderCookie(page, identity);
      redirects.length = 0;

      await verifyWithMitIdForBlazor(page);

      await expectBlazorUrl(page, "user/profile");
      await expect(userMenuButton(page)).toBeVisible();
      expectRedirectsInsideBlazor(redirects);
    })();

    await step("Verify with MitID at a low assurance level & verify the Blazor error page")(async () => {
      await setMockProviderCookie(page, "lowassurance");
      redirects.length = 0;

      await verifyWithMitIdForBlazor(page);

      await expectBlazorUrl(page, "error");
      expect(new URL(page.url()).searchParams.get("error")).not.toBeNull();
      await expect(page.getByRole("heading", { name: blazorTexts().somethingWentWrong })).toBeVisible();
      expectRedirectsInsideBlazor(redirects);
    })();

    await step("Read the MitID button & confirm the approved phrase, the wordmark and the brand geometry")(async () => {
      await page.goto(blazorPath("app"));
      await logOutThroughBlazor(page);
      await page.goto(blazorPath("login"));

      const mitIdButton = page.getByRole("button", { name: blazorTexts().logOnWithMitId, exact: true });
      await expect(mitIdButton).toBeVisible();
      await expect(mitIdButton.getByRole("img", { name: "MitID" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Log in with MitID" })).not.toBeVisible();
      await expect(page.getByText(blazorTexts().or, { exact: true })).toBeVisible();
      await expect(mitIdButton).toHaveCSS("height", "48px");
      await expect(mitIdButton).toHaveCSS("border-radius", "4px");
      await expect(mitIdButton).toHaveCSS("background-color", "rgb(0, 96, 230)");

      await page.goto(blazorPath("signup"));
      await expect(page.getByRole("button", { name: blazorTexts().signUpWithGoogle, exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: blazorTexts().logOnWithMitId })).toHaveCount(0);
    })();

    await step("Log in with the verified MitID identity and a deep link & verify return to the deep link")(async () => {
      await setMockProviderCookie(page, identity);
      redirects.length = 0;

      await startExternalFlow(page, "MitId", "login", blazorPath("user/profile"));

      await expectBlazorUrl(page, "user/profile");
      await expect(userMenuButton(page)).toBeVisible();
      expectRedirectsInsideBlazor(redirects);
    })();

    await step("Log in with an unbound MitID identity & verify the localized Blazor error page")(async () => {
      await page.goto(blazorPath("app"));
      await logOutThroughBlazor(page);
      await setMockProviderCookie(page, `identity:${uniqueIdentifier()}`);
      redirects.length = 0;

      await startExternalFlow(page, "MitId", "login");

      await expectBlazorErrorPage(page, "identity_not_verified", blazorTexts().identityNotVerified);
      expectRedirectsInsideBlazor(redirects);
    })();
  });
});

test.describe("@comprehensive", () => {
  /**
   * Entra flows for the Blazor edition and hostile return paths through the Blazor callers of AppUrls:
   * - Entra signup, deep link login, hostile return paths and denial, as for Google
   * - Google refusals render their localized pages with their actions: account not found, account already exists, email address required and authentication failed
   * - Email login and the welcome gate drop hostile return paths for the Blazor home and still honour a valid deep link
   */
  test("should keep Entra flows and Google refusals inside the Blazor edition and drop hostile return paths on email login and welcome", async ({ page }) => {
    createTestContext(page);
    const email = uniqueBlazorEmail();

    // === ENTRA ===
    await runProviderFlowsForBlazor(page, "Entra");

    // === REFUSALS ===
    await runRefusalsForBlazor(page);

    // === EMAIL LOGIN AND WELCOME ===
    await step("Sign up with email & log out")(async () => {
      await page.context().clearCookies();
      await signUpThroughBlazor(page, email);
      await logOutThroughBlazor(page);

      await expectBlazorUrl(page, "login");
    })();

    await step("Log in with an encoded dot segment return path & verify the Blazor home")(async () => {
      await page.goto(`${blazorPath("login")}?returnPath=${encodeURIComponent("/blazor/%2e%2e/dashboard")}`);
      await logInFromCurrentLoginPage(page, email);

      await expect(page).toHaveURL(blazorUrl("app"));
    })();

    await step("Open welcome with hostile return paths & verify the Blazor home each time")(async () => {
      for (const hostile of ["/blazor/../dashboard", "/blazor/%5c..%5cdashboard", "https://evil.example/blazor/app"]) {
        await page.goto(`${blazorPath("welcome")}?returnPath=${encodeURIComponent(hostile)}`);

        await expect(page).toHaveURL(blazorUrl("app"));
      }
    })();

    await step("Open welcome with a valid deep link & verify return to the deep link")(async () => {
      await page.goto(`${blazorPath("welcome")}?returnPath=${encodeURIComponent(blazorPath("app/details"))}`);

      await expectBlazorUrl(page, "app/details");
    })();

    await step("Log in again from a valid deep link & verify return to the deep link")(async () => {
      await logOutThroughBlazor(page);
      await page.goto(`${blazorPath("login")}?returnPath=${encodeURIComponent(blazorPath("app/details"))}`);
      await logInFromCurrentLoginPage(page, email);

      await expectBlazorUrl(page, "app/details");
      await expect(userMenuButton(page)).toBeVisible();
    })();
  });
});

/**
 * Complete an email login from the Blazor login page the browser is on, keeping whatever return path its URL carries
 */
async function logInFromCurrentLoginPage(page: Page, email: string): Promise<void> {
  await startEmailFlowThroughBlazor(page, "login", email);
  await submitOneTimePassword(page);
}
