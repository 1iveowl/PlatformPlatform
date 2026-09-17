import { expect, test, type Page } from "@playwright/test";
import { getBaseUrl } from "@shared/e2e/utils/constants";
import { createTestContext, type TestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";
import { sendAccountApiRequest } from "./account-api";
import { logOutThroughBlazor, userMenuButton } from "./authentication";
import { blazorPath, expectBlazorUrl, gotoBlazor } from "./routes";
import { blazorTexts } from "./texts";

/**
 * The cookie that makes the account API use its mock provider for a flow, when the deployment allows the mock provider
 */
export const mockProviderCookieName = "__Test_Use_Mock_Provider";

/**
 * The cookie a tenant switch writes and the Blazor login page sends as the preferred tenant of every login it starts
 */
export const preferredTenantCookieName = "preferred-tenant";

/**
 * The annotation that labels a test's evidence as mock provider evidence, recorded in the external login result files
 */
export const mockProviderEvidenceAnnotation = "mock-provider-evidence";

/**
 * The annotation a provider specification carries when it was skipped because this deployment disabled its provider,
 * after the unavailable behaviour was asserted; such a run belongs to the provider-disabled lane
 */
export const providerDisabledAnnotation = "provider-disabled";

const providerErrorDescription = "The user denied access";

export type Provider = "Google" | "Entra" | "MitId";

/**
 * The system feature flags a provider needs before its specification applies: Google and Entra need their login flag,
 * MitID login needs both MitID flags, because a MitID identity can only log in after verifying with it
 */
const providerFlags: Record<Provider, string[]> = {
  Google: ["google-oauth"],
  Entra: ["entra-oauth"],
  MitId: ["mitid-login", "mitid-verification"]
};

/**
 * A random identifier for a mock provider identity or email prefix, unique per call
 */
export function uniqueIdentifier(): string {
  return crypto.randomUUID().replaceAll("-", "").slice(0, 16);
}

/**
 * Select the mock provider's behaviour for the next flow: an email prefix, "identity:<id>", "identity:<id>:<email prefix>",
 * "noemail", "lowassurance" or "fail:<mode>"
 */
export async function setMockProviderCookie(page: Page, value: string): Promise<void> {
  await page.context().addCookies([{ name: mockProviderCookieName, value, url: getBaseUrl() }]);
}

/**
 * Read the system feature flags of the deployment through the page, from the bootstrap the Blazor pages are rendered with
 * @param page Playwright page instance on a page of the gateway's origin
 */
export function readSystemFeatureFlags(page: Page): Promise<Record<string, boolean>> {
  return page.evaluate(async () => {
    const response = await fetch("/api/account/bootstrap", { credentials: "same-origin" });
    return ((await response.json()) as { systemFeatureFlags: Record<string, boolean> }).systemFeatureFlags;
  });
}

/**
 * The signed-in user as the bootstrap reports it, or null for an anonymous page
 * @param page Playwright page instance on a page of the gateway's origin
 */
export function readBootstrapUser(page: Page): Promise<{ id: string; tenantId: string; email: string } | null> {
  return page.evaluate(async () => {
    const response = await fetch("/api/account/bootstrap", { credentials: "same-origin" });
    return ((await response.json()) as { user: { id: string; tenantId: string; email: string } | null }).user;
  });
}

/**
 * Decide the lane of a provider specification before each test. With every flag the provider needs switched on, the test
 * runs and is labelled as mock provider evidence. Otherwise the provider's unavailable behaviour is asserted (no button on
 * the Blazor login or signup page, and the account API refuses to start the flow) and the test is skipped with the
 * provider-disabled annotation; such a skip can never satisfy the provider-enabled result, which requires every test to
 * pass (see blazor/tests/e2e/support/external-login-reporter.ts).
 * @param page Playwright page instance in a fresh browser context
 * @param provider The provider the specification covers
 */
export async function requireProviderOrExpectUnavailable(page: Page, provider: Provider): Promise<void> {
  const context = createTestContext(page);
  await page.goto(blazorPath("login"));
  const flags = await readSystemFeatureFlags(page);
  const missingFlags = providerFlags[provider].filter((flag) => flags[flag] !== true);
  test.info().annotations.push({
    type: mockProviderEvidenceAnnotation,
    description: "Flows run against the account API's mock provider selected by the __Test_Use_Mock_Provider cookie; this is not a real provider integration result"
  });
  if (missingFlags.length === 0) return;

  const loginFlagOff = flags[providerFlags[provider][0]] !== true;
  if (loginFlagOff) await expectProviderUnavailable(page, context, provider);
  test.info().annotations.push({ type: providerDisabledAnnotation, description: `${provider}: ${missingFlags.join(", ")} disabled` });
  test.skip(true, `${provider} is disabled in this deployment (${missingFlags.join(", ")}); its unavailable behaviour was asserted`);
}

/**
 * How the account API refuses the start of a disabled provider's flow. MitID is switched off per flow, so its start is
 * refused as not enabled. Entra is switched off by leaving its credentials out, so its start is refused as not configured.
 * Google has no start refusal: OAuthProviderFactory leaves Google out of its configuration guard on purpose, so a disabled
 * Google is unavailable only by its missing buttons.
 */
const disabledStartRefusals: Record<Provider, ((flow: "Login" | "Signup") => string) | null> = {
  Google: null,
  Entra: () => "Provider 'Entra' is not configured.",
  MitId: (flow) => `Provider 'MitId' is not enabled for the '${flow}' flow.`
};

/**
 * Expect a disabled provider to be unavailable: no button on the Blazor login page, none on the signup page, and, where the
 * account API guards the provider, the start of each of its flows refused with the problem for a disabled provider
 */
async function expectProviderUnavailable(page: Page, context: TestContext, provider: Provider): Promise<void> {
  await expect(page.getByRole("heading", { name: blazorTexts().hiWelcomeBack })).toBeVisible();
  await expect(page.getByRole("button", { name: providerButtonName(provider, "login"), exact: true })).toHaveCount(0);
  const refusal = disabledStartRefusals[provider];
  const flows: ("login" | "signup")[] = refusal === null ? [] : provider === "MitId" ? ["login"] : ["login", "signup"];
  for (const flow of flows) {
    context.monitoring.expectedStatusCodes.push(400);
    const response = await page.evaluate(async (path) => {
      const result = await fetch(path, { credentials: "same-origin", redirect: "manual" });
      return { status: result.status, body: await result.text() };
    }, `/api/account/authentication/${provider}/${flow}/start?Edition=Blazor`);
    expect(response.status).toBe(400);
    expect((JSON.parse(response.body) as { detail?: string }).detail).toBe(refusal!(flow === "login" ? "Login" : "Signup"));
  }
  await page.goto(blazorPath("signup"));
  await expect(page.getByRole("heading", { name: blazorTexts().createYourAccount })).toBeVisible();
  await expect(page.getByRole("button", { name: providerButtonName(provider, "signup"), exact: true })).toHaveCount(0);
}

/**
 * The accessible name of a provider's button on the Blazor login or signup page in the running project's culture
 */
export function providerButtonName(provider: Provider, flow: "login" | "signup"): string {
  const texts = blazorTexts();
  if (provider === "MitId") return texts.logOnWithMitId;
  if (flow === "login") return provider === "Google" ? texts.logInWithGoogle : texts.logInWithMicrosoft;
  return provider === "Google" ? texts.signUpWithGoogle : texts.signUpWithMicrosoft;
}

/**
 * Start an external authentication flow from the provider's button on the Blazor login or signup page, opened with the
 * return path the page passes on to the start endpoint
 */
export async function startExternalFlow(page: Page, provider: Provider, flow: "login" | "signup", returnPath?: string): Promise<void> {
  const query = returnPath === undefined ? "" : `?returnPath=${encodeURIComponent(returnPath)}`;
  await page.goto(`${blazorPath(flow)}${query}`);

  await page.getByRole("button", { name: providerButtonName(provider, flow), exact: true }).click();
}

/**
 * The absolute start URL the provider's button on the current Blazor login or signup page submits: its form's action with
 * the hidden fields as the query, read from the rendered page
 */
export function readStartUrl(page: Page, provider: Provider, flow: "login" | "signup"): Promise<string> {
  return page.getByRole("button", { name: providerButtonName(provider, flow), exact: true }).evaluate((button) => {
    const form = (button as HTMLButtonElement).form!;
    const url = new URL(form.action);
    for (const [name, value] of new FormData(form)) url.searchParams.append(name, String(value));
    return url.toString();
  });
}

/**
 * Start an external authentication flow for the Blazor edition by navigating to the start endpoint directly, the way a
 * crafted link would, so the account API's own return path rule is exercised without the page's sanitising
 */
export async function startExternalFlowByUrl(page: Page, provider: Provider, flow: "login" | "signup", returnPath: string): Promise<void> {
  const query = new URLSearchParams({ Edition: "Blazor", Locale: blazorTexts().locale, ReturnPath: returnPath });

  await page.goto(`${getBaseUrl()}/api/account/authentication/${provider}/${flow}/start?${query}`);
}

/**
 * Start the flow of the provider's button from the current Blazor page in the background, without following the redirect
 * to the provider: the browser stores the flow cookie the start sets, and the flow waits for a callback
 */
export async function startFlowWithoutFollowing(page: Page, provider: Provider, flow: "login" | "signup"): Promise<void> {
  const startUrl = await readStartUrl(page, provider, flow);
  const responseType = await page.evaluate(async (url) => (await fetch(url, { credentials: "same-origin", redirect: "manual" })).type, startUrl);
  expect(responseType).toBe("opaqueredirect");
}

/**
 * Record the Location of every redirect a document navigation follows from now on
 * @param page Playwright page instance
 * @returns The live list of absolute redirect destinations
 */
export function trackRedirects(page: Page): string[] {
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
export function expectRedirectsInsideBlazor(locations: string[]): void {
  expect(locations.length).toBeGreaterThan(0);
  for (const location of locations) {
    const url = new URL(location);
    expect(url.pathname.startsWith("/api/account/authentication/") || url.pathname.startsWith(blazorPath()), location).toBe(true);
  }
  const blazorRedirects = locations.filter((location) => new URL(location).pathname.startsWith(blazorPath()));
  expect(blazorRedirects.length).toBeGreaterThan(0);
  expect(blazorRedirects.filter((location) => location.includes("error_description"))).toEqual([]);
}

/**
 * Expect the Blazor error page for a refused external authentication, localized, with the reference id of the attempt and
 * no provider error description
 */
export async function expectBlazorErrorPage(page: Page, errorCode: string, heading: string): Promise<void> {
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
 * Start a MitID verification for the Blazor edition as the signed-in user through the account API, the request the Blazor
 * profile's verification button will send, and follow the returned authorization URL. The identity verification and
 * profile specifications (T018) will replace this API step with the profile's verification section (T017).
 */
export async function verifyWithMitIdForBlazor(page: Page): Promise<void> {
  const response = await sendAccountApiRequest(page, "POST", "/api/account/authentication/MitId/verification/start?Edition=Blazor", {
    returnPath: blazorPath("user/profile")
  });
  expect(response.status, response.body).toBe(200);

  await page.goto((JSON.parse(response.body) as { authorizationUrl: string }).authorizationUrl);
}

/**
 * Start a MitID verification from the identity verification section of the Blazor profile as the signed-in user: open the
 * profile, click the localized "Confirm with MitID" button and let the document leave for the identity provider. The caller
 * sets the mock provider cookie first and asserts where the flow lands.
 */
export async function verifyWithMitIdFromBlazorProfile(page: Page): Promise<void> {
  await gotoBlazor(page, "user/profile");
  const confirmButton = page.getByRole("button", { name: blazorTexts().confirmWithMitId, exact: true });
  await expect(confirmButton).toBeVisible();

  await confirmButton.click();
}

/**
 * The flow cookie the account API sets when an external authentication starts and reads at the callback
 */
const flowCookieName = "__Host-external-login";

/**
 * Sign up with an OAuth provider, log in again, be refused a second signup for the same identity, log in from that refusal,
 * and log in once the provider reports a changed email: the same account is resolved by the provider identity alone
 * @param page Playwright page instance in a fresh browser context
 * @param provider Google or Entra
 */
export async function runOAuthSignupAndLoginJourney(page: Page, provider: "Google" | "Entra"): Promise<void> {
  const texts = blazorTexts();
  const emailPrefix = uniqueIdentifier();
  const email = `${emailPrefix}@mock.localhost`;
  const redirects = trackRedirects(page);
  let account = { id: "", tenantId: "", email: "" };

  // === SIGNUP ===

  await step(`Sign up with ${provider} & name the account & verify the workspace of the new account`)(async () => {
    await setMockProviderCookie(page, emailPrefix);
    await startExternalFlow(page, provider, "signup");

    await expectBlazorUrl(page, "welcome");
    await expect(page.getByRole("heading", { name: texts.setUpYourAccount })).toBeVisible();
    await page.getByLabel(texts.accountName, { exact: true }).fill("External account");
    await page.getByRole("button", { name: texts.continue, exact: true }).click();

    await expectBlazorUrl(page, "app");
    await expect(userMenuButton(page)).toBeVisible();
    account = (await readBootstrapUser(page))!;
    expect(account.email).toBe(email);
    expectRedirectsInsideBlazor(redirects);
  })();

  // === LOGIN ===

  await step(`Log out & log in with ${provider} & verify the same account`)(async () => {
    await logOutThroughBlazor(page);
    redirects.length = 0;

    await setMockProviderCookie(page, emailPrefix);
    await page.getByRole("button", { name: providerButtonName(provider, "login"), exact: true }).click();

    await expectBlazorUrl(page, "app");
    await expect(userMenuButton(page)).toBeVisible();
    expect(await readBootstrapUser(page)).toMatchObject(account);
    expectRedirectsInsideBlazor(redirects);
  })();

  // === EXISTING USER SIGNUP ===

  await step(`Log out & sign up with ${provider} as the existing user & verify the account already exists page`)(async () => {
    await logOutThroughBlazor(page);
    redirects.length = 0;

    await setMockProviderCookie(page, emailPrefix);
    await startExternalFlow(page, provider, "signup");

    await expectBlazorErrorPage(page, "account_already_exists", texts.accountAlreadyExists);
    await expect(page.getByText(texts.accountAlreadyExistsMessage)).toBeVisible();
    expectRedirectsInsideBlazor(redirects);
  })();

  await step(`Open login from the error page & log in with ${provider} & verify the workspace`)(async () => {
    await page.getByTestId("error-login").click();
    await expectBlazorUrl(page, "login");
    await expect(page.getByRole("heading", { name: texts.hiWelcomeBack })).toBeVisible();
    redirects.length = 0;

    await setMockProviderCookie(page, emailPrefix);
    await page.getByRole("button", { name: providerButtonName(provider, "login"), exact: true }).click();

    await expectBlazorUrl(page, "app");
    await expect(userMenuButton(page)).toBeVisible();
    expectRedirectsInsideBlazor(redirects);
  })();

  // === CHANGED PROVIDER EMAIL ===

  await step(`Log out & log in with ${provider} reporting a changed email & verify the same account by provider identity`)(async () => {
    await logOutThroughBlazor(page);
    redirects.length = 0;

    await setMockProviderCookie(page, `identity:${emailPrefix}:${uniqueIdentifier()}`);
    await page.getByRole("button", { name: providerButtonName(provider, "login"), exact: true }).click();

    await expectBlazorUrl(page, "app");
    await expect(userMenuButton(page)).toBeVisible();
    expect(await readBootstrapUser(page)).toMatchObject(account);
    expectRedirectsInsideBlazor(redirects);
  })();
}

/**
 * Carry the preferred tenant cookie to the provider's login start, then drive every refusal of the provider's flows through
 * the gateway: a denial at the provider, a failed token exchange, an unverified email, an unknown identity, a tampered
 * state and a lost flow cookie, and render the error page directly for each refusal code
 * @param page Playwright page instance in a fresh browser context
 * @param provider Google or Entra
 */
export async function runOAuthPreferredTenantAndRefusalJourney(page: Page, provider: "Google" | "Entra"): Promise<void> {
  const texts = blazorTexts();
  const emailPrefix = uniqueIdentifier();
  const redirects = trackRedirects(page);
  let tenantId = "";

  // === PREFERRED TENANT ===

  await step(`Sign up with ${provider} & name the account & verify the workspace`)(async () => {
    await setMockProviderCookie(page, emailPrefix);
    await startExternalFlow(page, provider, "signup");
    await expectBlazorUrl(page, "welcome");
    await page.getByLabel(texts.accountName, { exact: true }).fill("Preferred account");
    await page.getByRole("button", { name: texts.continue, exact: true }).click();

    await expectBlazorUrl(page, "app");
    tenantId = (await readBootstrapUser(page))!.tenantId;
    expect(tenantId).toBeTruthy();
  })();

  await step(`Log out & open login with a preferred tenant cookie & verify the ${provider} start carries the tenant`)(async () => {
    await logOutThroughBlazor(page);
    await page.context().addCookies([{ name: preferredTenantCookieName, value: tenantId, url: getBaseUrl() }]);

    await page.goto(blazorPath("login"));

    const startUrl = new URL(await readStartUrl(page, provider, "login"));
    expect(startUrl.searchParams.get("PreferredTenantId")).toBe(tenantId);
    expect(startUrl.searchParams.get("Edition")).toBe("Blazor");
  })();

  await step(`Log in with ${provider} & verify the preferred tenant is the signed-in tenant`)(async () => {
    redirects.length = 0;
    await setMockProviderCookie(page, emailPrefix);

    await page.getByRole("button", { name: providerButtonName(provider, "login"), exact: true }).click();

    await expectBlazorUrl(page, "app");
    await expect(userMenuButton(page)).toBeVisible();
    expect((await readBootstrapUser(page))!.tenantId).toBe(tenantId);
    expectRedirectsInsideBlazor(redirects);
  })();

  await step(`Log out & open login with a malformed preferred tenant cookie & verify the ${provider} start carries no tenant`)(async () => {
    await logOutThroughBlazor(page);
    await page.context().addCookies([{ name: preferredTenantCookieName, value: "not-a-tenant", url: getBaseUrl() }]);

    await page.goto(blazorPath("login"));

    const startUrl = new URL(await readStartUrl(page, provider, "login"));
    expect(startUrl.searchParams.has("PreferredTenantId")).toBe(false);
    await page.context().clearCookies({ name: preferredTenantCookieName });
  })();

  // === MOCK PROVIDER REFUSALS THROUGH THE GATEWAY ===

  await step(`Cancel authentication at ${provider} & verify the access denied page leads back to login`)(async () => {
    redirects.length = 0;
    await setMockProviderCookie(page, "fail:access_denied");

    await startExternalFlow(page, provider, "login");

    await expectBlazorErrorPage(page, "access_denied", texts.accessDenied);
    await expect(page.getByText(texts.accessDeniedMessage)).toBeVisible();
    await expect(page.getByTestId("error-login")).toHaveAttribute("href", blazorPath("login"));
    await expect(page.getByTestId("error-signup")).toHaveCount(0);
    expectRedirectsInsideBlazor(redirects);
  })();

  await step(`Sign up with ${provider} when the token exchange fails & verify the authentication failed page`)(async () => {
    redirects.length = 0;
    await setMockProviderCookie(page, "fail:token_exchange");

    await startExternalFlow(page, provider, "signup");

    await expectBlazorErrorPage(page, "authentication_failed", texts.authenticationFailed);
    await expect(page.getByText(texts.authenticationFailedMessage)).toBeVisible();
    await expect(page.getByTestId("error-login")).toHaveAttribute("href", blazorPath("login"));
    expectRedirectsInsideBlazor(redirects);
  })();

  await step(`Sign up with ${provider} reporting an unverified email & verify the authentication failed page`)(async () => {
    redirects.length = 0;
    await setMockProviderCookie(page, "fail:email_not_verified");

    await startExternalFlow(page, provider, "signup");

    await expectBlazorErrorPage(page, "authentication_failed", texts.authenticationFailed);
    await expect(page.getByText(texts.authenticationFailedMessage)).toBeVisible();
    expectRedirectsInsideBlazor(redirects);
  })();

  await step(`Log in with ${provider} as an unknown identity & verify the account not found page leads to signup`)(async () => {
    redirects.length = 0;
    await setMockProviderCookie(page, uniqueIdentifier());

    await startExternalFlow(page, provider, "login");

    await expectBlazorErrorPage(page, "user_not_found", texts.accountNotFound);
    await expect(page.getByText(texts.accountNotFoundMessage)).toBeVisible();
    await expect(page.getByTestId("error-signup")).toHaveAttribute("href", blazorPath("signup"));
    expectRedirectsInsideBlazor(redirects);
  })();

  // A callback that cannot tie the flow cookie to its state cannot know the edition the flow started from, so the account
  // API sends it to the edition-independent fallback, the React error page on the same origin, never to a Blazor page
  await step(`Start a ${provider} login & return with a tampered state & verify the fallback invalid request page`)(async () => {
    await page.goto(blazorPath("login"));
    await setMockProviderCookie(page, emailPrefix);
    await startFlowWithoutFollowing(page, provider, "login");

    await page.goto(`${getBaseUrl()}/api/account/authentication/${provider}/login/callback?code=mock-authorization-code&state=tampered-state`);

    await expect(page).toHaveURL((url) => url.pathname === "/error" && url.searchParams.get("error") === "invalid_request");
    await page.context().clearCookies({ name: flowCookieName });
  })();

  await step(`Log in with ${provider} & replay its callback once the flow cookie is gone & verify the fallback authentication failed page`)(async () => {
    await page.goto(blazorPath("login"));
    redirects.length = 0;
    await setMockProviderCookie(page, emailPrefix);
    await page.getByRole("button", { name: providerButtonName(provider, "login"), exact: true }).click();
    await expectBlazorUrl(page, "app");
    await expect(userMenuButton(page)).toBeVisible();
    const callbackUrl = redirects.find((location) => new URL(location).pathname === `/api/account/authentication/${provider}/login/callback`);
    expect(callbackUrl).toBeDefined();
    await logOutThroughBlazor(page);

    await page.goto(callbackUrl!);

    await expect(page).toHaveURL((url) => url.pathname === "/error" && url.searchParams.get("error") === "authentication_failed");
    await expect(page).toHaveURL((url) => !url.pathname.startsWith(blazorPath()));
  })();

  // === DIRECT ERROR PAGE RENDERING ===

  await step("Open the Blazor error page for each refusal code & verify its localized title, message and reference id")(async () => {
    const codes = [
      { code: "user_not_found", title: texts.accountNotFound, message: texts.accountNotFoundMessage },
      { code: "authentication_failed", title: texts.authenticationFailed, message: texts.authenticationFailedMessage },
      { code: "access_denied", title: texts.accessDenied, message: texts.accessDeniedMessage },
      { code: "invalid_request", title: texts.invalidRequest, message: undefined }
    ];
    for (const [index, { code, title, message }] of codes.entries()) {
      const referenceId = `test-ref-00${index + 1}`;

      await page.goto(`${blazorPath("error")}?error=${code}&id=${referenceId}`);

      await expect(page.getByRole("heading", { name: title })).toBeVisible();
      if (message !== undefined) await expect(page.getByText(message)).toBeVisible();
      await expect(page.getByText(`${texts.referenceId}${referenceId}`)).toBeVisible();
    }

    await page.goto(`${blazorPath("error")}?error=some_unknown_error&id=test-ref-007`);
    await expect(page.getByRole("heading", { name: texts.somethingWentWrong })).toBeVisible();
  })();
}
