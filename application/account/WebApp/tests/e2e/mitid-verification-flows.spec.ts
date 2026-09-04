import { faker } from "@faker-js/faker";
import { expect, type Page } from "@playwright/test";
import { test } from "@shared/e2e/fixtures/page-auth";
import { getBaseUrl } from "@shared/e2e/utils/constants";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { completeSignupFlow } from "@shared/e2e/utils/test-data";
import { step } from "@shared/e2e/utils/test-step-wrapper";

const MOCK_PROVIDER_COOKIE = "__Test_Use_Mock_Provider";

// The section is gated on a system feature flag, which the SPA reads from the runtime environment embedded in the
// page, so the same source decides whether these tests apply
test.beforeEach(async ({ page }) => {
  await page.goto("/login");
  const isMitIdVerificationEnabled = await page.evaluate(() => {
    const meta = document.head.querySelector('meta[name="runtimeEnv"]');
    const runtimeEnv = JSON.parse(meta?.getAttribute("content") ?? "{}");
    return runtimeEnv.PUBLIC_MITID_VERIFICATION_ENABLED === "true";
  });
  test.skip(!isMitIdVerificationEnabled, "MitID verification is not enabled");
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

test.describe("@smoke", () => {
  /**
   * Tests MitID identity verification including:
   * 1. A new account is created with email so the test owns a user that has never verified
   * 2. The profile page offers verification and the flow completes through the mock provider
   * 3. The browser returns to the profile page, which shows the verified state, the assurance level and the date
   * 4. The verified state persists across a reload and the verify button is gone once the identity is bound
   *
   * Note: MitID is verification only, so signup happens with email. No session is created by the verification
   * flow itself.
   */
  test("should verify identity with MitID and return to the profile showing the verified state", async ({ page }) => {
    const context = createTestContext(page);
    const uniqueSuffix = faker.string.alphanumeric(10).toLowerCase();
    const user = {
      email: `e2e-mitid-${uniqueSuffix}@${uniqueSuffix}.local`,
      firstName: "Test",
      lastName: "User"
    };

    // === SIGNUP: Create a fresh account that has never verified ===

    await step("Sign up with email & complete the welcome flow")(async () => {
      await completeSignupFlow(page, expect, user, context);
    })();

    // === VERIFICATION: Bind a MitID identity to that account ===

    await step("Open the profile & verify with MitID through the mock provider")(async () => {
      await page.goto("/user/profile");

      await expect(page.getByRole("heading", { name: "Identity verification" })).toBeVisible();
      await setMockProviderCookie(page, `identity:${faker.string.alphanumeric(10)}`);
      await page.getByRole("button", { name: "Verify with MitID" }).click();

      await expect(page.getByText("Verified with MitID")).toBeVisible();
      await expect(page).toHaveURL("/user/profile");
      await expect(page.getByText("Substantial assurance")).toBeVisible();
      await expect(page.getByText("Verified on")).toBeVisible();
    })();

    await step("Reload the profile & read the persisted verified state")(async () => {
      await page.goto("/user/profile");

      await expect(page.getByText("Verified with MitID")).toBeVisible();
      await expect(page.getByRole("button", { name: "Verify with MitID" })).not.toBeVisible();
    })();
  });
});

test.describe("@comprehensive", () => {
  /**
   * Tests the MitID verification refusal surfaces as a signed-in user, which is the only kind of user who can be
   * refused, including:
   * 1. A verification whose authentication predates the flow is refused and lands on the authentication failed page
   * 2. The identity-already-linked error page renders with its own copy and its action
   * 3. The assurance-level error page renders with its own copy and its action
   * 4. Every page carries the reference id, so a person can quote it to support
   *
   * Note: The mock provider always reports the requested assurance level, and a linked identity needs two users
   * in one account, so those two refusals are proved by the API tests and their pages are rendered directly here.
   */
  test("should refuse a stale authentication and render the MitID refusal error pages", async ({ ownerPage }) => {
    createTestContext(ownerPage);

    // === STALE AUTHENTICATION THROUGH THE PROFILE ===

    await step("Verify with MitID using a stale authentication & land on the authentication failed page")(async () => {
      await ownerPage.goto("/user/profile");

      await expect(ownerPage.getByRole("heading", { name: "Identity verification" })).toBeVisible();
      await setMockProviderCookie(ownerPage, "staleauthentication");
      await ownerPage.getByRole("button", { name: "Verify with MitID" }).click();

      await expect(ownerPage.getByRole("heading", { name: "Authentication failed" })).toBeVisible();
      await expect(ownerPage.getByText("We detected a security issue with your login attempt.")).toBeVisible();
      await expect(ownerPage.getByRole("button", { name: "Log out" })).toBeVisible();
      await expect(ownerPage.getByText("Reference ID:")).toBeVisible();
    })();

    // === DIRECT ERROR PAGE RENDERING ===

    await step("Navigate to identity_already_linked error page & read content, action and reference id")(async () => {
      await ownerPage.goto("/error?error=identity_already_linked&id=test-ref-101");

      await expect(ownerPage.getByRole("heading", { name: "Identity already in use" })).toBeVisible();
      await expect(ownerPage.getByText("This identity is already linked to another account.")).toBeVisible();
      await expect(ownerPage.getByRole("button", { name: "Back to profile" })).toBeVisible();
      await expect(ownerPage.getByText("Reference ID: test-ref-101")).toBeVisible();
    })();

    await step("Navigate to assurance_level_insufficient error page & read content, action and reference id")(
      async () => {
        await ownerPage.goto("/error?error=assurance_level_insufficient&id=test-ref-102");

        await expect(ownerPage.getByRole("heading", { name: "Verification not strong enough" })).toBeVisible();
        await expect(ownerPage.getByText("Your identity could not be verified at the required level.")).toBeVisible();
        await expect(ownerPage.getByRole("button", { name: "Back to profile" })).toBeVisible();
        await expect(ownerPage.getByText("Reference ID: test-ref-102")).toBeVisible();
      }
    )();
  });
});
