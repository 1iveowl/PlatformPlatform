import { expect } from "@playwright/test";
import { logOutThroughBlazor, signUpThroughBlazor, test, userMenuButton } from "@blazor/e2e/authentication";
import {
  expectBlazorErrorPage,
  expectRedirectsInsideBlazor,
  readBootstrapUser,
  readStartUrl,
  requireProviderOrExpectUnavailable,
  setMockProviderCookie,
  startExternalFlow,
  trackRedirects,
  uniqueIdentifier,
  verifyWithMitIdFromBlazorProfile
} from "@blazor/e2e/external-login";
import { revokeVerificationInBackOffice } from "@blazor/e2e/identity-verification";
import { blazorPath, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { currentSessionCard, expectSessionsListed } from "@blazor/e2e/sessions";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorTexts } from "@blazor/e2e/texts";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

// Runs only when this deployment enables both MitID login and MitID verification, read from the bootstrap's system feature
// flags through the page: signing in with MitID is only reachable after verifying with it. Otherwise the unavailable
// behaviour is asserted and the test is skipped into the provider-disabled lane. Every flow uses the account API's mock
// provider, so this is mock provider evidence, never a real MitID integration result.
test.beforeEach(async ({ page }) => {
  await requireProviderOrExpectUnavailable(page, "MitId");
});

test.describe("@smoke", () => {
  /**
   * MitID login for a verified identity, in the culture of the running project:
   * - An account is created with email, because MitID can never be used to sign up
   * - The identity is verified with the "Confirm with MitID" button on the Blazor profile
   * - After logout the login page offers MitID under the approved phrase "Log on with MitID" with the wordmark and the brand geometry, never "Log in with MitID"
   * - Logging in with the same MitID identity returns to the same account
   * - The sessions page lists the current session with MitID as its login method
   */
  test("should log in with a verified MitID identity and record the session as MitID", async ({ page }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const identity = `identity:${uniqueIdentifier()}`;
    const redirects = trackRedirects(page);
    let account = { id: "", tenantId: "" };

    // === SIGNUP AND VERIFICATION ===

    await step("Sign up with email & verify with MitID from the Blazor profile & verify return to the verified profile")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail());
      account = (await readBootstrapUser(page))!;
      await setMockProviderCookie(page, identity);
      redirects.length = 0;

      await verifyWithMitIdFromBlazorProfile(page);

      await expectBlazorUrl(page, "user/profile");
      await expect(page.getByText(texts.verifiedWith)).toBeVisible();
      await expect(userMenuButton(page)).toBeVisible();
      expectRedirectsInsideBlazor(redirects);
    })();

    // === LOGIN ===

    await step("Log out & read the MitID button & verify the approved phrase, the wordmark and the brand geometry")(async () => {
      await page.goto(blazorPath("app"));
      await logOutThroughBlazor(page);

      await page.goto(blazorPath("login"));

      const mitIdButton = page.getByRole("button", { name: texts.logOnWithMitId, exact: true });
      await expect(mitIdButton).toBeVisible();
      await expect(mitIdButton.getByRole("img", { name: "MitID" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Log in with MitID" })).toHaveCount(0);
      await expect(page.getByText(texts.or, { exact: true })).toBeVisible();
      await expect(mitIdButton).toHaveCSS("height", "48px");
      await expect(mitIdButton).toHaveCSS("border-radius", "4px");
      await expect(mitIdButton).toHaveCSS("background-color", "rgb(0, 96, 230)");
    })();

    await step("Log in with the verified MitID identity & verify the same account in the workspace")(async () => {
      await setMockProviderCookie(page, identity);
      redirects.length = 0;

      await page.getByRole("button", { name: texts.logOnWithMitId, exact: true }).click();

      await expectBlazorUrl(page, "app");
      await expect(userMenuButton(page)).toBeVisible();
      expect(await readBootstrapUser(page)).toMatchObject(account);
      expectRedirectsInsideBlazor(redirects);
    })();

    await step("Open the sessions page & verify the current session is recorded as MitID")(async () => {
      await gotoBlazor(page, "user/sessions");

      await expectSessionsListed(page, 1);
      await expect(currentSessionCard(page)).toContainText(texts.loginMethodMitId);
    })();
  });
});

test.describe("@comprehensive", () => {
  /**
   * The refusal and withdrawal paths of MitID login:
   * - An identity that was never verified is refused with the localized identity not verified page, its hint and reference id, rather than told no account exists
   * - The login start carries the return path of the login page
   * - Revoking the verification in the back office ends MitID login for that identity; the identity is verified from the Blazor profile, and the back office stays the React edition until stage G
   * - The identity not verified page renders its title, message, hint and reference id when opened directly
   */
  test("should refuse an unverified identity, carry the return path, and stop working once revoked", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();
    const redirects = trackRedirects(page);
    const identity = `identity:${uniqueIdentifier()}`;
    let userId = "";

    // === REFUSAL ===

    await step("Log in with a MitID identity that was never verified & verify the identity not verified page")(async () => {
      await setMockProviderCookie(page, `identity:${uniqueIdentifier()}`);
      redirects.length = 0;

      await startExternalFlow(page, "MitId", "login");

      await expectBlazorErrorPage(page, "identity_not_verified", texts.identityNotVerified);
      await expect(page.getByText(texts.identityNotVerifiedMessage)).toBeVisible();
      await expect(page.getByText(texts.identityNotVerifiedHint)).toBeVisible();
      expectRedirectsInsideBlazor(redirects);
    })();

    await step("Open login with a return path & verify the MitID start carries it")(async () => {
      await page.goto(`${blazorPath("login")}?returnPath=${encodeURIComponent(blazorPath("app/details"))}`);

      const startUrl = new URL(await readStartUrl(page, "MitId", "login"));

      expect(startUrl.pathname).toBe("/api/account/authentication/MitId/login/start");
      expect(startUrl.searchParams.get("ReturnPath")).toBe(blazorPath("app/details"));
    })();

    // === WITHDRAWAL ===

    await step("Sign up with email & verify with MitID from the Blazor profile & verify return to the verified profile")(async () => {
      await page.context().clearCookies();
      await signUpThroughBlazor(page, uniqueBlazorEmail());
      userId = (await readBootstrapUser(page))!.id;
      await setMockProviderCookie(page, identity);

      await verifyWithMitIdFromBlazorProfile(page);

      await expectBlazorUrl(page, "user/profile");
      await expect(page.getByText(texts.verifiedWith)).toBeVisible();
    })();

    await step("Revoke the verification in the back office & verify the identity is no longer verified")(async () => {
      await revokeVerificationInBackOffice(browser, userId);

      await gotoBlazor(page, "user/profile");

      await expect(page.getByRole("button", { name: texts.confirmWithMitId, exact: true })).toBeVisible();
    })();

    await step("Log out & log in with the revoked MitID identity & verify the identity not verified page")(async () => {
      await page.goto(blazorPath("app"));
      await logOutThroughBlazor(page);
      await setMockProviderCookie(page, identity);
      redirects.length = 0;

      await page.getByRole("button", { name: texts.logOnWithMitId, exact: true }).click();

      await expectBlazorErrorPage(page, "identity_not_verified", texts.identityNotVerified);
      expectRedirectsInsideBlazor(redirects);
    })();

    // === DIRECT ERROR PAGE RENDERING ===

    await step("Open the identity not verified error page directly & verify its title, message, hint and reference id")(async () => {
      await page.goto(`${blazorPath("error")}?error=identity_not_verified&id=test-ref-101`);

      await expect(page.getByRole("heading", { name: texts.identityNotVerified })).toBeVisible();
      await expect(page.getByText(texts.identityNotVerifiedMessage)).toBeVisible();
      await expect(page.getByText(texts.identityNotVerifiedHint)).toBeVisible();
      await expect(page.getByText(`${texts.referenceId}test-ref-101`)).toBeVisible();
    })();
  });
});
