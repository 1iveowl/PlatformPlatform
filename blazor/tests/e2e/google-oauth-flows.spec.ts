import { test } from "@blazor/e2e/authentication";
import {
  requireProviderOrExpectUnavailable,
  runOAuthPreferredTenantAndRefusalJourney,
  runOAuthSignupAndLoginJourney
} from "@blazor/e2e/external-login";
import { createTestContext } from "@shared/e2e/utils/test-assertions";

// Runs only when this deployment enables Google, read from the bootstrap's system feature flags through the page; otherwise
// the unavailable behaviour is asserted and the test is skipped into the provider-disabled lane. Every flow uses the account
// API's mock provider, so this is mock provider evidence, never a real Google integration result.
test.beforeEach(async ({ page }) => {
  await requireProviderOrExpectUnavailable(page, "Google");
});

test.describe("@smoke", () => {
  /**
   * Google signup and login through the Blazor buttons, in the culture of the running project:
   * - Signup creates a tenant and user and lands in the workspace after the account step of the welcome setup
   * - Logout and login with Google resolve the same account
   * - A second signup for the same identity lands on the localized account already exists page, whose login action leads to a working login
   * - A login reporting a changed provider email resolves the same account by provider identity alone
   * - Every redirect stays on external authentication endpoints or below the Blazor path base
   */
  test("should sign up, log in, redirect an existing user's signup to login and log in with a changed provider email", async ({ page }) => {
    createTestContext(page);

    await runOAuthSignupAndLoginJourney(page, "Google");
  });
});

test.describe("@comprehensive", () => {
  /**
   * Google preferred tenant selection and error paths with error page rendering:
   * - The preferred-tenant cookie a tenant switch writes is carried to the Google login start and selects the signed-in tenant; a malformed value is dropped
   * - Cancelled authentication, a failed token exchange, an unverified email and an unknown identity render their localized Blazor error pages with their actions through the gateway
   * - A tampered state and a lost flow cookie land on the edition-independent fallback error page, never on a Blazor page
   * - The Blazor error page renders each refusal code's title, message and reference id, and a generic page for an unknown code
   */
  test("should carry the preferred tenant and render the error paths of Google flows", async ({ page }) => {
    createTestContext(page);

    await runOAuthPreferredTenantAndRefusalJourney(page, "Google");
  });
});
