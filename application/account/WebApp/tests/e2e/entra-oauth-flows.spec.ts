import { faker } from "@faker-js/faker";
import { expect, type Page } from "@playwright/test";
import { test } from "@shared/e2e/fixtures/page-auth";
import { getBaseUrl } from "@shared/e2e/utils/constants";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

const MOCK_PROVIDER_COOKIE = "__Test_Use_Mock_Provider";

test.beforeEach(async ({ page }) => {
  await page.goto("/signup");
  await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
  const microsoftButtonCount = await page.getByRole("button", { name: "Sign up with Microsoft" }).count();
  test.skip(microsoftButtonCount === 0, "Entra OAuth is not enabled");
});

async function setMockProviderCookie(page: Page, value: string): Promise<void> {
  await page.context().addCookies([
    {
      name: MOCK_PROVIDER_COOKIE,
      value: value,
      url: getBaseUrl()
    }
  ]);
}

async function readUserInfo(page: Page): Promise<{ id: string; tenantId: string }> {
  return page.evaluate(() => {
    const metaTag = document.head.getElementsByTagName("meta").namedItem("userInfoEnv");
    return metaTag ? JSON.parse(metaTag.content) : { id: "", tenantId: "" };
  });
}

test.describe("@smoke", () => {
  /**
   * Tests Entra OAuth authentication flows including:
   * 1. Signup with Microsoft to create new tenant and user
   * 2. Logout and verify redirect to login page
   * 3. Login with Microsoft and verify authentication
   * 4. Verify user profile shows correct email
   * 5. Logout via menu and verify redirect
   * 6. Attempt signup as existing user - verify account already exists error page
   * 7. Navigate to login from error page and complete login
   * 8. Login with a changed provider email - verify the same account is resolved by provider identity alone
   *
   * Note: Uses mock OAuth provider with unique email per test run to avoid conflicts.
   */
  test("should handle Entra OAuth signup, login, existing user signup redirect, and changed provider email login flow", async ({
    page
  }) => {
    const context = createTestContext(page);
    const emailPrefix = faker.string.alphanumeric(10);
    const mockUserEmail = `${emailPrefix}@mock.localhost`;

    // === SIGNUP: Create mock user via Entra OAuth ===

    let userInfo: { id: string; tenantId: string };

    await step("Navigate to signup page & sign up with Microsoft & complete welcome flow")(async () => {
      await page.goto("/signup");

      await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
      await setMockProviderCookie(page, emailPrefix);
      await page.getByRole("button", { name: "Sign up with Microsoft" }).click();

      await expect(page).toHaveURL(/\/welcome/);
      await expect(page.getByRole("heading", { name: "Let's set up your account" })).toBeVisible();
      await page.getByRole("textbox", { name: "Account name" }).fill("Test Organization");
      await page.getByRole("button", { name: "Continue" }).click();

      await expect(page.getByRole("heading", { name: "Let's set up your profile" })).toBeVisible();
      await page.getByRole("textbox", { name: "First name" }).fill("Test");
      await page.getByRole("textbox", { name: "Last name" }).fill("User");
      await page.getByRole("button", { name: "Continue" }).click();

      await expect(page).toHaveURL("/dashboard");
      await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();

      userInfo = await readUserInfo(page);
      expect(userInfo.id).toBeTruthy();
      expect(userInfo.tenantId).toBeTruthy();
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

    // === LOGIN: Verify Entra OAuth login works ===

    await step("Click Microsoft login button & verify successful authentication")(async () => {
      await setMockProviderCookie(page, emailPrefix);
      await page.getByRole("button", { name: "Log in with Microsoft" }).click();

      await expect(page).toHaveURL("/dashboard");
      await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();
    })();

    await step("Open account menu & verify mock user email displays")(async () => {
      await page.getByRole("button", { name: "User menu" }).dispatchEvent("click");
      const menu = page.getByRole("menu");
      await expect(menu).toBeVisible();

      await expect(menu).toContainText(mockUserEmail.toLowerCase());
    })();

    await step("Log out via menu & verify redirect to login page")(async () => {
      context.monitoring.expectedStatusCodes.push(401);
      const menu = page.getByRole("menu");
      await expect(menu).toBeVisible();
      const logoutMenuItem = page.getByRole("menuitem", { name: "Log out" });
      await expect(logoutMenuItem).toBeVisible();
      await logoutMenuItem.dispatchEvent("click");

      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();
    })();

    // === EXISTING USER SIGNUP: Verify error page with login redirect ===

    await step("Navigate to signup page & attempt Microsoft signup as existing user")(async () => {
      await page.goto("/signup");

      await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
      await setMockProviderCookie(page, emailPrefix);
      await page.getByRole("button", { name: "Sign up with Microsoft" }).click();

      await expect(page.getByRole("heading", { name: "Account already exists" })).toBeVisible();
      await expect(page.getByText("An account with this email already exists.")).toBeVisible();
      await expect(page.getByRole("button", { name: "Log in" })).toBeVisible();
    })();

    await step("Click log in button from error page & login with Microsoft")(async () => {
      await page.getByRole("button", { name: "Log in" }).click();

      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();
      await setMockProviderCookie(page, emailPrefix);
      await page.getByRole("button", { name: "Log in with Microsoft" }).click();

      await expect(page).toHaveURL("/dashboard");
      await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();
    })();

    // === CHANGED PROVIDER EMAIL: Login by provider identity alone ===

    await step("Log out to prepare for the changed-email login")(async () => {
      context.monitoring.expectedStatusCodes.push(401);
      await page.getByRole("button", { name: "User menu" }).dispatchEvent("click");
      const menu = page.getByRole("menu");
      await expect(menu).toBeVisible();
      const logoutMenuItem = page.getByRole("menuitem", { name: "Log out" });
      await expect(logoutMenuItem).toBeVisible();
      await logoutMenuItem.dispatchEvent("click");

      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();
    })();

    await step("Log in with Microsoft using a changed provider email & verify the same account is resolved")(
      async () => {
        const changedEmailPrefix = faker.string.alphanumeric(10);
        await setMockProviderCookie(page, `identity:${emailPrefix}:${changedEmailPrefix}`);
        await page.getByRole("button", { name: "Log in with Microsoft" }).click();

        await expect(page).toHaveURL("/dashboard");
        await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();
        const userInfoAfterLogin = await readUserInfo(page);
        expect(userInfoAfterLogin.id).toBe(userInfo.id);
        expect(userInfoAfterLogin.tenantId).toBe(userInfo.tenantId);

        await page.getByRole("button", { name: "User menu" }).dispatchEvent("click");
        const menu = page.getByRole("menu");
        await expect(menu).toBeVisible();

        await expect(menu).toContainText(mockUserEmail.toLowerCase());
      }
    )();
  });
});

test.describe("@comprehensive", () => {
  /**
   * Tests Entra OAuth error paths, preferred tenant selection, and error page rendering including:
   * 1. Preferred tenant - signup, extract tenant ID, set localStorage, re-login and verify PreferredTenantId passed
   * 2. Access denied - user cancels OAuth consent, verify error page
   * 3. Token exchange failure - mock provider returns null tokens, verify error page
   * 4. Email not verified - mock provider returns unverified email, verify error page
   * 5. User not found on login - login with unknown identity, verify error page with signup action
   * 6. Email not provided on signup - mock provider returns a profile without an email, verify error page
   * 7. Direct error page rendering for email not provided and identity mismatch with reference ID display
   */
  test("should handle preferred tenant selection and Entra OAuth error paths with error page rendering", async ({
    page
  }) => {
    const context = createTestContext(page);
    const emailPrefix = faker.string.alphanumeric(10);
    const mockUserEmail = `${emailPrefix}@mock.localhost`;

    // === PREFERRED TENANT: Verify PreferredTenantId is passed during Entra login ===

    await step("Sign up with Microsoft & complete welcome flow")(async () => {
      await page.goto("/signup");

      await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
      await setMockProviderCookie(page, emailPrefix);
      await page.getByRole("button", { name: "Sign up with Microsoft" }).click();

      await expect(page).toHaveURL(/\/welcome/);
      await expect(page.getByRole("heading", { name: "Let's set up your account" })).toBeVisible();
      await page.getByRole("textbox", { name: "Account name" }).fill("Test Organization");
      await page.getByRole("button", { name: "Continue" }).click();

      await expect(page.getByRole("heading", { name: "Let's set up your profile" })).toBeVisible();
      await page.getByRole("textbox", { name: "First name" }).fill("Test");
      await page.getByRole("textbox", { name: "Last name" }).fill("User");
      await page.getByRole("button", { name: "Continue" }).click();

      await expect(page).toHaveURL("/dashboard");
      await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();
    })();

    let tenantId: string;

    await step("Extract tenant ID from user info & log out")(async () => {
      tenantId = (await readUserInfo(page)).tenantId;
      expect(tenantId).toBeTruthy();

      context.monitoring.expectedStatusCodes.push(401);
      await page.getByRole("button", { name: "User menu" }).dispatchEvent("click");
      const menu = page.getByRole("menu");
      await expect(menu).toBeVisible();
      const logoutMenuItem = page.getByRole("menuitem", { name: "Log out" });
      await expect(logoutMenuItem).toBeVisible();
      await logoutMenuItem.dispatchEvent("click");

      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();
    })();

    await step("Set preferred tenant, enter email & login with Microsoft verifying query parameter")(async () => {
      await page.evaluate((tid) => localStorage.setItem("preferred-tenant", tid), tenantId);

      await page.getByLabel("Email").fill(mockUserEmail);

      let capturedUrl = "";
      await page.route("**/authentication/Entra/login/start**", async (route) => {
        capturedUrl = route.request().url();
        await route.continue();
      });

      await setMockProviderCookie(page, emailPrefix);
      await page.getByRole("button", { name: "Log in with Microsoft" }).click();

      await expect(page).toHaveURL("/dashboard");
      await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();

      expect(capturedUrl).toContain(`PreferredTenantId=${tenantId}`);
    })();

    await step("Log out to prepare for error path tests")(async () => {
      context.monitoring.expectedStatusCodes.push(401);
      await page.getByRole("button", { name: "User menu" }).dispatchEvent("click");
      const menu = page.getByRole("menu");
      await expect(menu).toBeVisible();
      const logoutMenuItem = page.getByRole("menuitem", { name: "Log out" });
      await expect(logoutMenuItem).toBeVisible();
      await logoutMenuItem.dispatchEvent("click");

      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();
    })();

    // === MOCK PROVIDER ERROR PATHS ===

    await step("Navigate to login & trigger access denied error via mock provider")(async () => {
      await page.goto("/login");

      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();
      await setMockProviderCookie(page, "fail:access_denied");
      await page.getByRole("button", { name: "Log in with Microsoft" }).click();

      await expect(page.getByRole("heading", { name: "Access denied" })).toBeVisible();
      await expect(page.getByText("Authentication was cancelled or denied.")).toBeVisible();
      await expect(page.getByRole("button", { name: "Log in" })).toBeVisible();
    })();

    await step("Navigate to signup & trigger token exchange failure via mock provider")(async () => {
      await page.goto("/signup");

      await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
      await setMockProviderCookie(page, "fail:token_exchange");
      await page.getByRole("button", { name: "Sign up with Microsoft" }).click();

      await expect(page.getByRole("heading", { name: "Authentication failed" })).toBeVisible();
      await expect(page.getByText("We detected a security issue with your login attempt.")).toBeVisible();
      await expect(page.getByRole("button", { name: "Log in" })).toBeVisible();
    })();

    await step("Navigate to signup & trigger email not verified error via mock provider")(async () => {
      await page.goto("/signup");

      await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
      await setMockProviderCookie(page, "fail:email_not_verified");
      await page.getByRole("button", { name: "Sign up with Microsoft" }).click();

      await expect(page.getByRole("heading", { name: "Authentication failed" })).toBeVisible();
      await expect(page.getByText("We detected a security issue with your login attempt.")).toBeVisible();
    })();

    await step("Navigate to login & trigger user not found error with an unknown identity")(async () => {
      const unknownPrefix = faker.string.alphanumeric(10);
      await page.goto("/login");

      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();
      await setMockProviderCookie(page, unknownPrefix);
      await page.getByRole("button", { name: "Log in with Microsoft" }).click();

      await expect(page.getByRole("heading", { name: "Account not found" })).toBeVisible();
      await expect(page.getByText("No account found for this email address.")).toBeVisible();
      await expect(page.getByRole("button", { name: "Sign up" })).toBeVisible();
    })();

    await step("Sign up with Microsoft without a shared email & verify the email address required page")(async () => {
      await page.goto("/signup");

      await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
      await setMockProviderCookie(page, "noemail");
      await page.getByRole("button", { name: "Sign up with Microsoft" }).click();

      await expect(page.getByRole("heading", { name: "Email address required" })).toBeVisible();
      await expect(
        page.getByText(
          "The identity provider did not share a verified email address, which is needed to create an account."
        )
      ).toBeVisible();
      await expect(page.getByRole("button", { name: "Sign up" })).toBeVisible();
    })();

    // === DIRECT ERROR PAGE RENDERING ===

    await step("Navigate to email_not_provided error page & verify content and reference ID")(async () => {
      await page.goto("/error?error=email_not_provided&id=test-ref-101");

      await expect(page.getByRole("heading", { name: "Email address required" })).toBeVisible();
      await expect(
        page.getByText(
          "The identity provider did not share a verified email address, which is needed to create an account."
        )
      ).toBeVisible();
      await expect(page.getByRole("button", { name: "Sign up" })).toBeVisible();
      await expect(page.getByText("Reference ID: test-ref-101")).toBeVisible();
    })();

    await step("Navigate to identity_mismatch error page & verify provider neutral content")(async () => {
      await page.goto("/error?error=identity_mismatch&id=test-ref-102");

      await expect(page.getByRole("heading", { name: "Identity mismatch" })).toBeVisible();
      await expect(page.getByText("This account is linked to a different sign-in identity.")).toBeVisible();
      await expect(page.getByRole("button", { name: "Log in" })).toBeVisible();
      await expect(page.getByText("Reference ID: test-ref-102")).toBeVisible();
    })();
  });
});
