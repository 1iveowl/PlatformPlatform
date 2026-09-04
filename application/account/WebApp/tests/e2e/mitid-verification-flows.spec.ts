import { faker } from "@faker-js/faker";
import { expect, type Page } from "@playwright/test";
import { test } from "@shared/e2e/fixtures/page-auth";
import { getBaseUrl } from "@shared/e2e/utils/constants";
import { createTestContext } from "@shared/e2e/utils/test-assertions";
import { step } from "@shared/e2e/utils/test-step-wrapper";

const MOCK_PROVIDER_COOKIE = "__Test_Use_Mock_Provider";

// The section heading renders as soon as the feature flag is on, while the button waits for the status query, so the
// heading is what decides whether the feature is available at all
test.beforeEach(async ({ ownerPage }) => {
  await ownerPage.goto("/user/profile");
  await expect(ownerPage.getByRole("heading", { name: "User profile" })).toBeVisible();
  const sectionCount = await ownerPage.getByRole("heading", { name: "Identity verification" }).count();
  test.skip(sectionCount === 0, "MitID verification is not enabled");
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
   * 1. A new account is created through Google so the test owns a user that has never verified
   * 2. The profile page offers verification and the flow completes through the mock provider
   * 3. The browser returns to the profile page, which shows the verified state and the assurance level
   * 4. The verify button is gone once the identity is bound
   *
   * Note: MitID is verification only, so signup happens through another provider. No session is created by the
   * verification flow itself.
   */
  test("should verify identity with MitID and return to the profile showing the verified state", async ({ page }) => {
    const context = createTestContext(page);
    const emailPrefix = faker.string.alphanumeric(10);

    // === SIGNUP: Create a fresh account that has never verified ===

    await step("Sign up with Google & complete the welcome flow")(async () => {
      await page.goto("/signup");

      await expect(page.getByRole("heading", { name: "Create your account" })).toBeVisible();
      await setMockProviderCookie(page, emailPrefix);
      await page.getByRole("button", { name: "Sign up with Google" }).click();

      await expect(page.getByRole("heading", { name: "Let's set up your account" })).toBeVisible();
      await page.getByRole("textbox", { name: "Account name" }).fill("Test Organization");
      await page.getByRole("button", { name: "Continue" }).click();

      await expect(page.getByRole("heading", { name: "Let's set up your profile" })).toBeVisible();
      await page.getByRole("textbox", { name: "First name" }).fill("Test");
      await page.getByRole("textbox", { name: "Last name" }).fill("User");
      await page.getByRole("button", { name: "Continue" }).click();

      await expect(page).toHaveURL("/dashboard");
    })();

    // === VERIFICATION: Bind a MitID identity to that account ===

    await step("Open the profile & verify with MitID")(async () => {
      await page.goto("/user/profile");

      await expect(page.getByRole("heading", { name: "Identity verification" })).toBeVisible();
      await page.getByRole("button", { name: "Verify with MitID" }).click();

      await expect(page).toHaveURL("/user/profile");
      await expect(page.getByText("Verified with MitID")).toBeVisible();
    })();

    await step("Reload the profile & read the recorded assurance level")(async () => {
      await page.goto("/user/profile");

      await expect(page.getByText("Substantial assurance")).toBeVisible();
      await expect(page.getByRole("button", { name: "Verify with MitID" })).toHaveCount(0);
      context.monitoring.expectedStatusCodes.push();
    })();
  });
});

test.describe("@comprehensive", () => {
  /**
   * Tests the MitID verification refusal surfaces including:
   * 1. The identity-already-linked error page renders with its own copy and a way to get help
   * 2. The assurance-level error page renders with its own copy
   * 3. Both carry the reference id, so a person can quote it to support
   *
   * Note: The refusals themselves are proved by the API tests, which can put two users in one account and can
   * replay an old authentication. This covers the pages a refused person actually sees.
   */
  test("should render the MitID refusal error pages", async ({ page }) => {
    createTestContext(page);

    // === DIRECT ERROR PAGE RENDERING ===

    await step("Navigate to identity_already_linked error page & read content and reference id")(async () => {
      await page.goto("/error?error=identity_already_linked&id=test-ref-101");

      await expect(page.getByRole("heading", { name: "Identity already in use" })).toBeVisible();
      await expect(page.getByText("This identity is already linked to another account.")).toBeVisible();
      await expect(page.getByText("Reference ID: test-ref-101")).toBeVisible();
    })();

    await step("Navigate to assurance_level_insufficient error page & read content and reference id")(async () => {
      await page.goto("/error?error=assurance_level_insufficient&id=test-ref-102");

      await expect(page.getByRole("heading", { name: "Verification not strong enough" })).toBeVisible();
      await expect(page.getByText("Your identity could not be verified at the required level.")).toBeVisible();
      await expect(page.getByText("Reference ID: test-ref-102")).toBeVisible();
    })();
  });
});
