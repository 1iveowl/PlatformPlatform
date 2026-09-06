import { faker } from "@faker-js/faker";
import { expect, type Page } from "@playwright/test";
import { test } from "@shared/e2e/fixtures/page-auth";
import { getBackOfficeBaseUrl, getBaseUrl } from "@shared/e2e/utils/constants";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { completeSignupFlow, logInAsAdmin } from "@shared/e2e/utils/test-data";
import { step } from "@shared/e2e/utils/test-step-wrapper";

const MOCK_PROVIDER_COOKIE = "__Test_Use_Mock_Provider";

// The button is gated on a system feature flag, which the SPA reads from the runtime environment embedded in the
// page, so the same source decides whether these tests apply. Both purposes are needed: signing in with MitID is
// only reachable after verifying with it.
test.beforeEach(async ({ page }) => {
  await page.goto("/login");
  const isMitIdLoginEnabled = await page.evaluate(() => {
    const meta = document.head.querySelector('meta[name="runtimeEnv"]');
    const runtimeEnv = JSON.parse(meta?.getAttribute("content") ?? "{}");
    return runtimeEnv.PUBLIC_MITID_LOGIN_ENABLED === "true" && runtimeEnv.PUBLIC_MITID_VERIFICATION_ENABLED === "true";
  });
  test.skip(!isMitIdLoginEnabled, "MitID login is not enabled");
});

async function readUserInfo(page: Page): Promise<{ id: string; tenantId: string }> {
  return page.evaluate(() => {
    const metaTag = document.head.getElementsByTagName("meta").namedItem("userInfoEnv");
    const userInfo = metaTag ? JSON.parse(metaTag.content) : {};
    return { id: userInfo.id ?? "", tenantId: userInfo.tenantId ?? "" };
  });
}

async function setMockProviderCookie(page: Page, value: string): Promise<void> {
  await page.context().addCookies([
    {
      name: MOCK_PROVIDER_COOKIE,
      value: value,
      url: getBaseUrl()
    }
  ]);
}

test.describe("@smoke", () => {
  /**
   * Tests MitID login end to end, which is only reachable after a verification, including:
   * 1. An account is created with email, because MitID can never be used to sign up
   * 2. The person verifies with MitID, which is what grants the identity the right to sign in
   * 3. They log out, and the login page offers MitID under one of the five phrases MitID approves. "Log in with
   *    MitID" is not one of them, so the accessible name is asserted exactly rather than loosely
   * 4. Signing in with the same MitID identifier returns them to the same account
   * 5. The session records MitID as the login method, which is what makes the sign-in visible to an administrator
   */
  test("should log in with a verified MitID identity and record the session as MitID", async ({ page }) => {
    const context = createTestContext(page);
    const uniqueSuffix = faker.string.alphanumeric(10).toLowerCase();
    const user = {
      email: `e2e-mitid-login-${uniqueSuffix}@${uniqueSuffix}.local`,
      firstName: "Test",
      lastName: "User"
    };
    // Stable across the verification and the login, so the second flow presents the identity the first one bound
    const mitIdIdentity = `identity:${faker.string.alphanumeric(10)}`;

    // === SIGNUP: MitID cannot create an account, so the account starts with email ===

    await step("Sign up with email & complete the welcome flow")(async () => {
      await completeSignupFlow(page, expect, user, context);
    })();
    const originalAccount = await readUserInfo(page);
    expect(originalAccount.id).toBeTruthy();
    expect(originalAccount.tenantId).toBeTruthy();

    // === VERIFICATION: the step that turns the identity into a way in ===

    await step("Verify with MitID from the profile")(async () => {
      await page.goto("/user/profile");

      await setMockProviderCookie(page, mitIdIdentity);
      await page.getByRole("button", { name: "Confirm with MitID" }).click();

      await expect(page.getByText("Verified with")).toBeVisible();
    })();

    await step("Open account menu & log out")(async () => {
      context.monitoring.expectedStatusCodes.push(401);
      await page.getByRole("button", { name: "User menu" }).dispatchEvent("click");
      const menu = page.getByRole("menu");
      await expect(menu).toBeVisible();
      const logoutMenuItem = page.getByRole("menuitem", { name: "Log out" });
      await expect(logoutMenuItem).toBeVisible();
      await logoutMenuItem.dispatchEvent("click");

      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();
    })();

    // === LOGIN: the same identifier now signs the person back in ===

    await step("Read the MitID button & confirm it carries an approved phrase and the wordmark")(async () => {
      // Logging out from the profile leaves a return path behind, so the login page is opened fresh to prove the
      // plain sign-in path rather than a redirect back to where the person happened to be
      await page.goto("/login");

      const mitIdButton = page.getByRole("button", { name: "Log on with MitID" });
      await expect(mitIdButton).toBeVisible();
      await expect(mitIdButton.getByRole("img", { name: "MitID" })).toBeVisible();

      // The phrase Google and Entra use is not approved for MitID, so its absence is part of the contract
      await expect(page.getByRole("button", { name: "Log in with MitID" })).not.toBeVisible();

      await page.evaluate(() => localStorage.setItem("preferred-locale", "da-DK"));
      await page.goto("/login");
      await expect(page.getByRole("button", { name: "Log ind med MitID", exact: true })).toBeVisible();
      await page.evaluate(() => localStorage.setItem("preferred-locale", "en-US"));
      await page.route("**/login", async (route) => {
        const response = await route.fetch();
        const body = (await response.text()).replace(
          /(PUBLIC_(?:GOOGLE|ENTRA)_OAUTH_ENABLED&quot;:&quot;)true/g,
          "$1false"
        );
        await route.fulfill({ response, body });
      });
      await page.goto("/login");
      await expect(page.getByRole("button", { name: "Log in with Google" })).toHaveCount(0);
      await expect(page.getByRole("button", { name: "Log in with Microsoft" })).toHaveCount(0);
      await expect(page.getByText("or", { exact: true })).toBeVisible();
      await expect(mitIdButton).toBeVisible();
      await expect(mitIdButton).toHaveCSS("height", "48px");
      await expect(mitIdButton).toHaveCSS("border-radius", "4px");
      await expect(mitIdButton).toHaveCSS("background-color", "rgb(0, 96, 230)");
      await page.unroute("**/login");
    })();

    await step("Log in with MitID & land back on the dashboard as the same person")(async () => {
      await setMockProviderCookie(page, mitIdIdentity);
      await page.getByRole("button", { name: "Log on with MitID" }).click();

      await expect(page).toHaveURL("/dashboard");
      await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();
      expect(await readUserInfo(page)).toMatchObject(originalAccount);
    })();

    await step("Open the sessions page & confirm the sign-in was recorded as MitID")(async () => {
      await page.goto("/user/sessions");

      await expect(page.getByText("MitID").first()).toBeVisible();
    })();
  });
});

test.describe("@comprehensive", () => {
  /**
   * Tests the refusal and withdrawal paths of MitID login, including:
   * 1. A MitID identity that was never verified cannot sign in, and is told to verify rather than that no account
   *    exists, which is a different and misleading answer
   * 2. The start URL carries the return path, so a person sent to log in returns where they were going
   * 3. An administrator revoking the verification ends MitID login for that identity, because the right to sign in
   *    came from the verification and goes with it
   * 4. The identity_not_verified page renders its own copy, action and reference id
   *
   * Note: the assurance and freshness rules are deliberately not exercised here. The mock derives the provider user
   * id from the whole cookie value, so "lowassurance" and "staleauthentication" name identities of their own and
   * cannot be presented for an identity bound with an "identity:" value. Those rules are proved by the API tests,
   * which seed the identity row to match.
   */
  test("should refuse an unverified identity, carry the return path, and stop working once revoked", async ({
    page,
    ownerPage,
    browser
  }) => {
    // The refusals are read on a signed-out browser, because the login page is only reachable to someone who is not
    // already signed in; the verification and revoke steps need the signed-in owner.
    const context = createTestContext(page);
    const ownerContext = createTestContext(ownerPage);

    // === REFUSAL: an identity nobody has verified resolves no account ===

    await step("Log in with a MitID identity that was never verified & read the refusal")(async () => {
      await page.goto("/login");

      await setMockProviderCookie(page, `identity:${faker.string.alphanumeric(10)}`);
      await page.getByRole("button", { name: "Log on with MitID" }).click();

      await expect(page.getByRole("heading", { name: "Identity not verified" })).toBeVisible();
      await expect(page.getByText("This MitID has not been used to verify an account.")).toBeVisible();
    })();

    await step("Start a MitID login with a return path & confirm the start URL carries it")(async () => {
      let capturedUrl = "";
      await page.route("**/authentication/MitId/login/start**", async (route) => {
        capturedUrl = route.request().url();
        await route.continue();
      });

      await page.goto("/login?returnPath=%2Fadmin");
      await setMockProviderCookie(page, `identity:${faker.string.alphanumeric(10)}`);
      await page.getByRole("button", { name: "Log on with MitID" }).click();

      await expect(page.getByRole("heading", { name: "Identity not verified" })).toBeVisible();
      expect(capturedUrl).toContain("ReturnPath=%2Fadmin");
      await page.unroute("**/authentication/MitId/login/start**");
    })();

    // === WITHDRAWAL: revoking the verification ends the sign-in it granted ===

    const mitIdIdentity = `identity:${faker.string.alphanumeric(10)}`;

    await step("Verify with MitID as the signed-in owner")(async () => {
      await ownerPage.goto("/user/profile");

      await setMockProviderCookie(ownerPage, mitIdIdentity);
      await ownerPage.getByRole("button", { name: "Confirm with MitID" }).click();

      await expect(ownerPage.getByText("Verified with")).toBeVisible();
    })();

    const userId = (await readUserInfo(ownerPage)).id;
    const backOfficeBaseUrl = getBackOfficeBaseUrl();
    const backOfficeContext = await browser.newContext({ baseURL: backOfficeBaseUrl, ignoreHTTPSErrors: true });
    const backOfficePage = await backOfficeContext.newPage();
    createTestContext(backOfficePage);

    await step("Revoke the verification from the back office")(async () => {
      await backOfficePage.goto(`${backOfficeBaseUrl}/`);
      await logInAsAdmin(backOfficePage, `${backOfficeBaseUrl}/`);

      await backOfficePage.goto(`${backOfficeBaseUrl}/users/${userId}`);
      await backOfficePage.getByRole("tab", { name: "Identity" }).click();
      await backOfficePage.getByRole("button", { name: "Revoke verification" }).click();

      const revokeDialog = backOfficePage.getByRole("alertdialog", { name: "Revoke identity verification" });
      await expect(revokeDialog).toBeVisible();
      await revokeDialog.getByRole("button", { name: "Revoke verification" }).click();

      await expect(backOfficePage.getByText("Not verified")).toBeVisible();
    })();

    await backOfficeContext.close();

    await step("Log out & confirm the revoked identity can no longer sign in")(async () => {
      ownerContext.monitoring.expectedStatusCodes.push(401);
      await ownerPage.getByRole("button", { name: "User menu" }).dispatchEvent("click");
      const logoutMenuItem = ownerPage.getByRole("menuitem", { name: "Log out" });
      await expect(logoutMenuItem).toBeVisible();
      await logoutMenuItem.dispatchEvent("click");

      await expect(ownerPage.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();

      await setMockProviderCookie(ownerPage, mitIdIdentity);
      await ownerPage.getByRole("button", { name: "Log on with MitID" }).click();

      await expect(ownerPage.getByRole("heading", { name: "Identity not verified" })).toBeVisible();
    })();

    // === DIRECT ERROR PAGE RENDERING ===

    await step("Navigate to the identity_not_verified page & read content, action and reference id")(async () => {
      await ownerPage.goto("/error?error=identity_not_verified&id=test-ref-101");

      await expect(ownerPage.getByRole("heading", { name: "Identity not verified" })).toBeVisible();
      await expect(
        ownerPage.getByText("Log in another way, then verify your identity from your profile to sign in with MitID.")
      ).toBeVisible();
      await expect(ownerPage.getByText("Reference ID: test-ref-101")).toBeVisible();
    })();
  });
});
