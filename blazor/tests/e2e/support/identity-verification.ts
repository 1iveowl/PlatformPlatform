import { expect, test, type Browser, type Page } from "@playwright/test";
import { getBackOfficeBaseUrl } from "@shared/e2e/utils/constants";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { logInAsAdmin } from "@shared/e2e/utils/test-data";
import { sendAccountApiRequest } from "./account-api";
import { signUpThroughBlazor } from "./authentication";
import { mockProviderEvidenceAnnotation, providerDisabledAnnotation, readSystemFeatureFlags, uniqueIdentifier } from "./external-login";
import { blazorPath, gotoBlazor } from "./routes";
import { uniqueBlazorEmail } from "./test-data";
import { accountApiMessages, blazorTexts } from "./texts";

/**
 * The values of the mock provider cookie a MitID verification specification selects: a successful identity, an
 * authentication older than the flow, and an authentication below the required assurance level
 */
export const mockVerificationValues = {
  identity: (identifier: string = uniqueIdentifier()) => `identity:${identifier}`,
  staleAuthentication: "staleauthentication",
  lowAssurance: "lowassurance"
} as const;

/**
 * The path of the account API's MitID verification callback, which the mock provider redirects the document to directly
 */
const verificationCallbackPath = "/api/account/authentication/MitId/verification/callback";

/**
 * The verification status of the signed-in user as the account API reports it
 */
export interface VerificationStatus {
  isVerified: boolean;
  provider: string | null;
  assuranceLevel: string | null;
  verifiedAt: string | null;
}

/**
 * Decide the lane of the MitID verification specification before each test. With the mitid-verification flag on, the test
 * runs and is labelled as mock provider evidence. Otherwise the unavailable behaviour is asserted for a new signed-in user
 * (no identity verification section on the Blazor profile, and the account API refuses to start a verification) and the
 * test is skipped with the provider-disabled annotation, which can never satisfy the provider-enabled result.
 * @param page Playwright page instance in a fresh browser context
 */
export async function requireMitIdVerificationOrExpectUnavailable(page: Page): Promise<void> {
  const context = createTestContext(page);
  await page.goto(blazorPath("login"));
  const flags = await readSystemFeatureFlags(page);
  test.info().annotations.push({
    type: mockProviderEvidenceAnnotation,
    description: "Flows run against the account API's mock provider selected by the __Test_Use_Mock_Provider cookie; this is not a real provider integration result"
  });
  if (flags["mitid-verification"] === true) return;

  const texts = blazorTexts();
  await signUpThroughBlazor(page, uniqueBlazorEmail());
  await gotoBlazor(page, "user/profile");
  await expect(page.getByRole("heading", { name: texts.profile, exact: true })).toBeVisible();
  await expect(page.getByLabel(texts.firstName, { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: texts.identityVerification, exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: texts.confirmWithMitId, exact: true })).toHaveCount(0);
  context.monitoring.expectedStatusCodes.push(400);
  const response = await sendAccountApiRequest(page, "POST", "/api/account/authentication/MitId/verification/start?Edition=Blazor", { returnPath: blazorPath("user/profile") });
  expect(response.status).toBe(400);
  expect((JSON.parse(response.body) as { detail?: string }).detail).toBe(accountApiMessages.mitIdVerificationNotEnabled);
  test.info().annotations.push({ type: providerDisabledAnnotation, description: "MitIdVerification: mitid-verification disabled" });
  test.skip(true, "MitID verification is disabled in this deployment (mitid-verification); its unavailable behaviour was asserted");
}

/**
 * Read the signed-in user's verification status through the account API, the request the profile's section sends
 * @param page Playwright page instance of a signed-in user, on a page of the gateway's origin
 */
export async function getVerificationStatusThroughAccountApi(page: Page): Promise<VerificationStatus> {
  const response = await sendAccountApiRequest(page, "GET", "/api/account/authentication/verification");
  expect(response.status, response.body).toBe(200);
  return JSON.parse(response.body) as VerificationStatus;
}

/**
 * Hold the next MitID verification callback in the browser so the broker round trip stays pending: the callback URL the
 * mock provider redirects to is recorded and answered with 204 No Content, which leaves the document on the profile
 * without reaching the account API. The returned promise resolves with the absolute callback URL once it was held.
 * @param page Playwright page instance of a signed-in user
 */
export async function holdVerificationCallback(page: Page): Promise<() => Promise<string>> {
  const pattern = `**${verificationCallbackPath}?**`;
  let resolveUrl: (url: string) => void = () => {};
  const held = new Promise<string>((resolve) => {
    resolveUrl = resolve;
  });
  await page.route(pattern, async (route) => {
    resolveUrl(route.request().url());
    await route.fulfill({ status: 204 });
  });
  return async () => {
    const url = await held;
    await page.unroute(pattern);
    return url;
  };
}

/**
 * Record the URL of every MitID verification callback the document is sent to from now on, without changing the flow
 * @param page Playwright page instance
 * @returns The live list of absolute callback URLs
 */
export function trackVerificationCallbacks(page: Page): string[] {
  const urls: string[] = [];
  page.on("request", (request) => {
    if (request.isNavigationRequest() && new URL(request.url()).pathname === verificationCallbackPath) urls.push(request.url());
  });
  return urls;
}

/**
 * Revoke a user's identity verification the way an administrator does, in the back office in a second browser context.
 * The back office stays the React edition until stage G moves it, so this step drives the React pages in English.
 * @param browser The browser the test runs in
 * @param userId The id of the verified user
 */
export async function revokeVerificationInBackOffice(browser: Browser, userId: string): Promise<void> {
  const backOfficeBaseUrl = getBackOfficeBaseUrl();
  const backOfficeContext = await browser.newContext({ baseURL: backOfficeBaseUrl, ignoreHTTPSErrors: true, locale: "en-US" });
  const backOfficePage = await backOfficeContext.newPage();
  await backOfficePage.goto(`${backOfficeBaseUrl}/`);
  await logInAsAdmin(backOfficePage, `${backOfficeBaseUrl}/`);
  await backOfficePage.goto(`${backOfficeBaseUrl}/users/${userId}`);
  await backOfficePage.getByRole("tab", { name: "Identity" }).click();
  await expect(backOfficePage.getByText("Verified with MitID")).toBeVisible();
  await backOfficePage.getByRole("button", { name: "Revoke verification" }).click();
  const revokeDialog = backOfficePage.getByRole("alertdialog", { name: "Revoke identity verification" });
  await expect(revokeDialog).toBeVisible();
  const revokeResponse = backOfficePage.waitForResponse(
    (response) => response.request().method() === "DELETE" && new URL(response.url()).pathname === `/api/back-office/users/${userId}/identity-verification`
  );

  await revokeDialog.getByRole("button", { name: "Revoke verification" }).click();

  expect((await revokeResponse).status()).toBe(200);
  await expect(backOfficePage.getByText("Not verified")).toBeVisible();
  await backOfficeContext.close();
}
