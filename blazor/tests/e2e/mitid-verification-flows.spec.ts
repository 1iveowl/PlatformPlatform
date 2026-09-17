import { expect, type Locator, type Page } from "@playwright/test";
import { inviteUsersThroughAccountApi } from "@blazor/e2e/account-api";
import { logInInvitedUserThroughBlazor, logInThroughBlazor, logOutThroughBlazor, openUserMenu, signUpThroughBlazor, test, userMenuButton } from "@blazor/e2e/authentication";
import { expectBlazorErrorPage, expectRedirectsInsideBlazor, readBootstrapUser, setMockProviderCookie, trackRedirects, verifyWithMitIdFromBlazorProfile } from "@blazor/e2e/external-login";
import {
  getVerificationStatusThroughAccountApi,
  holdVerificationCallback,
  mockVerificationValues,
  requireMitIdVerificationOrExpectUnavailable,
  revokeVerificationInBackOffice,
  trackVerificationCallbacks
} from "@blazor/e2e/identity-verification";
import { expectNoPolicyViolations, trackPolicyViolations } from "@blazor/e2e/policy";
import { blazorPath, blazorUrl, expectBlazorUrl, gotoBlazor } from "@blazor/e2e/routes";
import { uniqueBlazorEmail } from "@blazor/e2e/test-data";
import { blazorLocale, blazorTexts } from "@blazor/e2e/texts";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

// Runs only when this deployment enables MitID verification, read from the bootstrap's system feature flags through the
// page. Otherwise the absence of the profile section and the refused start are asserted and the test is skipped into the
// provider-disabled lane. Every flow uses the account API's mock provider, so this is mock provider evidence, never a real
// MitID integration result.
test.beforeEach(async ({ page }) => {
  await requireMitIdVerificationOrExpectUnavailable(page);
});

/**
 * The confirm button of the profile's identity verification section, named by the approved phrase and the wordmark
 */
function confirmButton(page: Page): Locator {
  return page.getByRole("button", { name: blazorTexts().confirmWithMitId, exact: true });
}

/**
 * The switch account item of the user menu for a tenant; open the user menu first
 */
function switchTenantButton(page: Page, tenantName: string): Locator {
  const texts = blazorTexts();
  return page.getByRole("menu", { name: texts.userMenu, exact: true }).getByRole("group", { name: texts.switchAccount, exact: true }).getByRole("menuitem", { name: tenantName });
}

test.describe("@smoke", () => {
  /**
   * MitID identity verification from the Blazor profile, in the culture of the running project:
   * - A new account is created with email, so the test owns a user that has never verified
   * - The profile shows the identity verification section with the "Confirm with MitID" button and its wordmark
   * - Confirming through the mock provider returns to the profile, which shows "Verified with" and the wordmark, the
   *   substantial assurance badge and the verified date, and no longer offers the button; every redirect stays inside the
   *   Blazor edition or on the authentication endpoints
   * - The verified state persists across a reload and matches the account API's status
   * - No securitypolicyviolation event on any profile document
   */
  test("should verify identity with MitID and return to the profile showing the verified state", async ({ page }) => {
    createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const redirects = trackRedirects(page);
    const verifiedSection = page.getByRole("region", { name: texts.identityVerification, exact: true });

    // === SIGNUP ===

    await step("Sign up with email and open the profile & verify the identity verification section offers MitID")(async () => {
      await signUpThroughBlazor(page, uniqueBlazorEmail());

      await gotoBlazor(page, "user/profile");

      await expect(page.getByRole("heading", { name: texts.identityVerification, exact: true })).toBeVisible();
      await expect(confirmButton(page)).toBeVisible();
      await expect(confirmButton(page).getByRole("img", { name: "MitID" })).toBeVisible();
      expect((await getVerificationStatusThroughAccountApi(page)).isVerified).toBe(false);
    })();

    // === VERIFICATION ===

    await step("Confirm with MitID through the mock provider & verify the profile shows the verified state")(async () => {
      await setMockProviderCookie(page, mockVerificationValues.identity());
      redirects.length = 0;

      await verifyWithMitIdFromBlazorProfile(page);

      await expectBlazorUrl(page, "user/profile");
      await expect(verifiedSection.getByText(texts.verifiedWith)).toBeVisible();
      await expect(verifiedSection.getByRole("img", { name: "MitID" })).toBeVisible();
      await expect(verifiedSection.getByText(texts.substantialAssurance, { exact: true })).toBeVisible();
      await expect(verifiedSection.getByText(texts.verifiedOnPrefix)).toBeVisible();
      await expect(confirmButton(page)).toHaveCount(0);
      expectRedirectsInsideBlazor(redirects);
    })();

    await step("Reload the profile & verify the verified state persists and matches the account API")(async () => {
      await page.reload();

      await expect(verifiedSection.getByText(texts.verifiedWith)).toBeVisible();
      await expect(verifiedSection.getByRole("img", { name: "MitID" })).toBeVisible();
      await expect(verifiedSection.getByText(texts.substantialAssurance, { exact: true })).toBeVisible();
      await expect(confirmButton(page)).toHaveCount(0);
      expect(await getVerificationStatusThroughAccountApi(page)).toMatchObject({ isVerified: true, provider: "MitId", assuranceLevel: "Substantial" });
      await expectNoPolicyViolations(page);
    })();
  });
});

test.describe("@comprehensive", () => {
  /**
   * The refusal, binding, replay and withdrawal paths of MitID verification from the Blazor profile:
   * - A stale authentication lands on the authentication failed page and a low assurance authentication on the
   *   verification not strong enough page, whose Back to profile offers the button again; neither links an identity
   * - A verification left pending at the broker cannot be completed after logout (session expired), after a switch to
   *   another tenant, or by another user who logs in in the same browser (authentication failed); none of them links an
   *   identity to the user who started it or to the user who presented the callback
   * - A successful verification's callback replayed afterwards is refused on the account API's fallback error page, since
   *   the consumed flow no longer names its edition, and leaves the verification as it was
   * - An administrator revokes the verification in the back office, which stays the React edition until stage G, and the
   *   profile offers the button again
   * - The identity already in use page renders its title, message, action and reference id when opened directly
   */
  test("should refuse a stale or weak authentication, render the refusal pages, and let an administrator revoke", async ({ page, browser }) => {
    createTestContext(page);
    const texts = blazorTexts();
    await trackPolicyViolations(page);
    const redirects = trackRedirects(page);
    const suffix = Date.now().toString().slice(-6);
    const primaryTenantName = `Verify ${suffix}`;
    const secondaryTenantName = `Other ${suffix}`;
    const ownerEmail = uniqueBlazorEmail();
    const invitedEmail = `invited-${uniqueBlazorEmail()}`;
    const ownerContext = await browser.newContext({ locale: blazorLocale(), baseURL: blazorUrl(), ignoreHTTPSErrors: true });
    const secondaryOwnerPage = await ownerContext.newPage();
    createTestContext(secondaryOwnerPage);
    let invitedUserId = "";

    // === REFUSALS ===

    await step("Confirm with MitID reporting a stale authentication & verify the authentication failed page and no link")(async () => {
      await signUpThroughBlazor(page, ownerEmail, primaryTenantName);
      await setMockProviderCookie(page, mockVerificationValues.staleAuthentication);
      redirects.length = 0;

      await verifyWithMitIdFromBlazorProfile(page);

      await expectBlazorErrorPage(page, "authentication_failed", texts.authenticationFailed);
      await expect(page.getByText(texts.authenticationFailedMessage)).toBeVisible();
      expectRedirectsInsideBlazor(redirects);
      expect((await getVerificationStatusThroughAccountApi(page)).isVerified).toBe(false);
    })();

    await step("Confirm with MitID at a low assurance level & verify the verification not strong enough page and no link")(async () => {
      await setMockProviderCookie(page, mockVerificationValues.lowAssurance);
      redirects.length = 0;

      await verifyWithMitIdFromBlazorProfile(page);

      await expectBlazorErrorPage(page, "assurance_level_insufficient", texts.assuranceLevelInsufficient);
      await expect(page.getByText(texts.assuranceLevelInsufficientMessage)).toBeVisible();
      expectRedirectsInsideBlazor(redirects);
      expect((await getVerificationStatusThroughAccountApi(page)).isVerified).toBe(false);
    })();

    await step("Go back to profile from the refusal & verify the MitID button is offered again")(async () => {
      await page.getByRole("link", { name: texts.backToProfile, exact: true }).click();

      await expectBlazorUrl(page, "user/profile");
      await expect(confirmButton(page)).toBeVisible();
    })();

    // === PENDING VERIFICATION ===

    await step("Leave a verification pending and log out & verify the callback shows session expired and links nothing")(async () => {
      const heldCallback = await holdVerificationCallback(page);
      await setMockProviderCookie(page, mockVerificationValues.identity());
      await verifyWithMitIdFromBlazorProfile(page);
      const callbackUrl = await heldCallback();
      await gotoBlazor(page, "app");
      await logOutThroughBlazor(page);

      await page.goto(callbackUrl);

      await expectBlazorErrorPage(page, "session_expired", texts.sessionExpired);
      await logInThroughBlazor(page, ownerEmail);
      expect((await getVerificationStatusThroughAccountApi(page)).isVerified).toBe(false);
    })();

    await step("Leave a verification pending and switch to another tenant & verify the callback is refused and links nothing")(async () => {
      await signUpThroughBlazor(secondaryOwnerPage, `owner-${uniqueBlazorEmail()}`, secondaryTenantName);
      await inviteUsersThroughAccountApi(secondaryOwnerPage, [ownerEmail, invitedEmail]);
      const heldCallback = await holdVerificationCallback(page);
      await setMockProviderCookie(page, mockVerificationValues.identity());
      await verifyWithMitIdFromBlazorProfile(page);
      const callbackUrl = await heldCallback();
      await gotoBlazor(page, "app");
      await openUserMenu(page);
      await switchTenantButton(page, secondaryTenantName).click();
      await expect(userMenuButton(page)).toHaveAccessibleDescription(new RegExp(` ${secondaryTenantName}$`));

      await page.goto(callbackUrl);

      await expectBlazorErrorPage(page, "authentication_failed", texts.authenticationFailed);
      expect((await getVerificationStatusThroughAccountApi(page)).isVerified).toBe(false);
      await gotoBlazor(page, "app");
      await openUserMenu(page);
      await switchTenantButton(page, primaryTenantName).click();
      await expect(userMenuButton(page)).toHaveAccessibleDescription(new RegExp(` ${primaryTenantName}$`));
      expect((await getVerificationStatusThroughAccountApi(page)).isVerified).toBe(false);
    })();

    await step("Leave a verification pending and log in as another user & verify the callback is refused and links nothing")(async () => {
      const heldCallback = await holdVerificationCallback(page);
      await setMockProviderCookie(page, mockVerificationValues.identity());
      await verifyWithMitIdFromBlazorProfile(page);
      const callbackUrl = await heldCallback();
      await gotoBlazor(page, "app");
      await logOutThroughBlazor(page);
      await logInInvitedUserThroughBlazor(page, invitedEmail, { firstName: "Invited", lastName: "User" });
      invitedUserId = (await readBootstrapUser(page))!.id;

      await page.goto(callbackUrl);

      await expectBlazorErrorPage(page, "authentication_failed", texts.authenticationFailed);
      expect((await getVerificationStatusThroughAccountApi(page)).isVerified).toBe(false);
    })();

    // === REPLAY ===

    await step("Verify with MitID and replay the callback & verify the replay is refused and the verification is unchanged")(async () => {
      const callbacks = trackVerificationCallbacks(page);
      await setMockProviderCookie(page, mockVerificationValues.identity());
      await verifyWithMitIdFromBlazorProfile(page);
      await expectBlazorUrl(page, "user/profile");
      await expect(page.getByText(texts.verifiedWith)).toBeVisible();
      const verified = await getVerificationStatusThroughAccountApi(page);
      expect(verified).toMatchObject({ isVerified: true, provider: "MitId", assuranceLevel: "Substantial" });
      expect(callbacks.length).toBeGreaterThan(0);

      await page.goto(callbacks[callbacks.length - 1]);

      // The completed flow's cookie is gone, so the account API cannot tell which edition started the flow and sends the
      // refusal to its fallback error page, the React edition's, by design
      const replayUrl = new URL(page.url());
      expect(replayUrl.pathname).toBe("/error");
      expect(replayUrl.searchParams.get("error")).toBe("authentication_failed");
      await gotoBlazor(page, "user/profile");
      await expect(page.getByText(texts.verifiedWith)).toBeVisible();
      expect(await getVerificationStatusThroughAccountApi(page)).toEqual(verified);
    })();

    // === WITHDRAWAL ===

    await step("Revoke the verification in the back office & verify the profile offers the MitID button again")(async () => {
      await revokeVerificationInBackOffice(browser, invitedUserId);

      await gotoBlazor(page, "user/profile");

      await expect(confirmButton(page)).toBeVisible();
      await expect(page.getByText(texts.verifiedWith)).toHaveCount(0);
      expect((await getVerificationStatusThroughAccountApi(page)).isVerified).toBe(false);
    })();

    // === DIRECT ERROR PAGE RENDERING ===

    await step("Open the identity already in use page directly & verify its title, message, action and reference id")(async () => {
      await page.goto(`${blazorPath("error")}?error=identity_already_linked&id=test-ref-101`);

      await expect(page.getByRole("heading", { name: texts.identityAlreadyLinked })).toBeVisible();
      await expect(page.getByText(texts.identityAlreadyLinkedMessage)).toBeVisible();
      await expect(page.getByRole("link", { name: texts.backToProfile, exact: true })).toBeVisible();
      await expect(page.getByText(`${texts.referenceId}test-ref-101`)).toBeVisible();
      await expectNoPolicyViolations(page);
    })();

    await ownerContext.close();
  });
});
