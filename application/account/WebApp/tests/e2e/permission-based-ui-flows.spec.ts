import { expect, type Page } from "@playwright/test";
import { test } from "@shared/e2e/fixtures/page-auth";
import {
  createTestContext,
  expectNetworkErrors,
  expectToastMessage,
  typeOneTimeCode
} from "@shared/e2e/utils/test-assertions";
import { completeSignupFlow, getVerificationCode, testUser } from "@shared/e2e/utils/test-data";
import { step } from "@shared/e2e/utils/test-step-wrapper";

const pngImageBase64 =
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
const htmlDisguisedAsImageBase64 = Buffer.from(
  "<html><body><script>alert(document.cookie)</script></body></html>"
).toString("base64");

/**
 * Posts a multipart file upload with fetch from inside the page, so the browser attaches its own cookies. The custom
 * antiforgery header is added only when requested, which proves the server requires it rather than the client adding it.
 */
async function submitUploadFromBrowser(
  page: Page,
  url: string,
  contentBase64: string,
  withAntiforgeryHeader: boolean
): Promise<{ status: number; body: string }> {
  return page.evaluate(
    async ({ url, contentBase64, withAntiforgeryHeader }) => {
      const bytes = Uint8Array.from(atob(contentBase64), (character) => character.charCodeAt(0));
      const formData = new FormData();
      formData.append("file", new Blob([bytes], { type: "image/png" }), "upload.png");
      const headers: Record<string, string> = {};
      if (withAntiforgeryHeader) {
        headers["x-xsrf-token"] =
          document.head.getElementsByTagName("meta").namedItem("antiforgeryToken")?.content ?? "";
      }
      const response = await fetch(url, { method: "POST", body: formData, headers });
      return { status: response.status, body: await response.text() };
    },
    { url, contentBase64, withAntiforgeryHeader }
  );
}

test.describe("@smoke", () => {
  /**
   * PERMISSION-BASED UI ACCESS CONTROL TESTS
   *
   * Tests the permission-based UI behavior ensuring UI elements accurately reflect
   * what actions users can perform based on backend authorization rules by creating
   * users with different roles and testing the UI behavior in the same session.
   *
   * Covers:
   * - Owner vs Member UI visibility (invite button, danger zone, bulk actions)
   * - Self-action restrictions (cannot delete or change own role)
   * - Access denied page for role-restricted routes (recycle-bin requires Owner/Admin)
   * - Logo and avatar uploads through the UI, served as images with nosniff through the gateway
   * - Browser multipart uploads without the antiforgery header, with HTML disguised as PNG, and a Member logo upload are rejected
   *
   * Note: Current test fixtures infrastructure creates only Owner users, so we test
   * by creating users with different roles and switching between them in a single session.
   * The back-office surface lives on its own subdomain (back-office.dev.localhost) post host-based
   * routing, so cross-app access-denied for non-internal users is enforced by auth-cookie-domain
   * isolation, not by an in-app AccessDeniedPage. That flow is covered by the back-office smoke test.
   */
  test("should enforce permission-based UI visibility, self-action restrictions, and access denied pages", async ({
    page
  }) => {
    const context = createTestContext(page);
    const owner = testUser();
    const member = testUser();

    // Create owner and member users
    await step("Create owner account with signup flow & verify home page")(async () => {
      await completeSignupFlow(page, expect, owner, context);
      await expect(page.getByRole("heading", { name: "Your dashboard is empty" })).toBeVisible();
    })();

    await step("Set account name & verify save confirmation")(async () => {
      await page.goto("/account/settings");
      await expect(page.getByRole("heading", { name: "Account settings" })).toBeVisible();
      await page.getByRole("textbox", { name: "Account name" }).fill("Test Organization");
      await page.getByRole("button", { name: "Save changes" }).click();
      await expectToastMessage(context, "Account settings updated successfully");
    })();

    await step("Upload account logo and profile avatar & verify both are served as PNG images")(async () => {
      const image = { name: "upload.png", mimeType: "image/png", buffer: Buffer.from(pngImageBase64, "base64") };
      await page.locator('input[type="file"]').setInputFiles(image);
      await page.getByRole("button", { name: "Save changes" }).click();
      await expectToastMessage(context, "Account settings updated successfully");

      await page.goto("/user/profile");
      await expect(page.getByRole("heading", { name: "Profile" })).toBeVisible();
      await page.locator('input[type="file"]').setInputFiles(image);
      await page.getByRole("button", { name: "Save changes" }).click();
      await expectToastMessage(context, "Profile updated successfully");

      const tenant = await (await page.request.get("/api/account/tenants/current")).json();
      const user = await (await page.request.get("/api/account/users/me")).json();
      expect(tenant.logoUrl).toMatch(/^\/logos\/.+\.png/);
      expect(user.avatarUrl).toMatch(/^\/avatars\/.+\.png/);
      for (const imageUrl of [tenant.logoUrl, user.avatarUrl]) {
        const imageResponse = await page.request.get(imageUrl);
        expect(imageResponse.status()).toBe(200);
        expect(imageResponse.headers()["content-type"]).toBe("image/png");
        expect(imageResponse.headers()["x-content-type-options"]).toBe("nosniff");
      }
    })();

    await step("Submit browser uploads without the antiforgery header & verify they are rejected")(async () => {
      const tenantBefore = await (await page.request.get("/api/account/tenants/current")).json();

      const avatarResponse = await submitUploadFromBrowser(
        page,
        "/api/account/users/me/update-avatar",
        pngImageBase64,
        false
      );
      const logoResponse = await submitUploadFromBrowser(
        page,
        "/api/account/tenants/current/update-logo",
        pngImageBase64,
        false
      );

      await expectNetworkErrors(context, [400]);
      for (const response of [avatarResponse, logoResponse]) {
        expect(response.status).toBe(400);
        expect(JSON.parse(response.body).title).toBe("Invalid Antiforgery Token");
      }
      const tenantAfter = await (await page.request.get("/api/account/tenants/current")).json();
      expect(tenantAfter.logoUrl).toBe(tenantBefore.logoUrl);
    })();

    await step("Submit HTML disguised as a PNG logo with the antiforgery header & verify it is rejected")(async () => {
      const response = await submitUploadFromBrowser(
        page,
        "/api/account/tenants/current/update-logo",
        htmlDisguisedAsImageBase64,
        true
      );

      await expectNetworkErrors(context, [400]);
      expect(response.status).toBe(400);
      expect(JSON.parse(response.body).errors.fileStream).toEqual([
        "Image must be a valid JPEG, PNG, GIF, or WebP file."
      ]);
    })();

    await step("Submit an avatar far over the size limit from the browser & verify it is rejected")(async () => {
      const oversizedContentBase64 = Buffer.alloc(3 * 1024 * 1024, 0x89).toString("base64");

      const withHeader = await submitUploadFromBrowser(
        page,
        "/api/account/users/me/update-avatar",
        oversizedContentBase64,
        true
      );
      const withoutHeader = await submitUploadFromBrowser(
        page,
        "/api/account/users/me/update-avatar",
        oversizedContentBase64,
        false
      );

      await expectNetworkErrors(context, [413, 400]);
      expect(withHeader.status).toBe(413);
      expect(withoutHeader.status).toBe(400);
      expect(JSON.parse(withoutHeader.body).title).toBe("Invalid Antiforgery Token");
    })();

    await step("Navigate to users page as Owner & verify invite button is visible")(async () => {
      await page.goto("/account/users");

      await expect(page.getByRole("heading", { name: "Users" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Invite user" })).toBeVisible();
    })();

    await step("Navigate to settings page as Owner & verify tenant name field is editable")(async () => {
      await page.goto("/account/settings");

      await expect(page.getByRole("heading", { name: "Account settings" })).toBeVisible();
      await expect(page.getByRole("textbox", { name: "Account name" })).toBeEnabled();
      await expect(page.getByRole("textbox", { name: "Account name" })).not.toHaveAttribute("readonly");
      await expect(page.getByRole("button", { name: "Save changes" })).toBeVisible();
    })();

    await step("Verify danger zone is visible for Owner")(async () => {
      // Danger zone should be visible to Owners
      await expect(page.getByRole("heading", { name: "Danger zone" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Delete account" })).toBeVisible();
      await expect(
        page.getByText("Delete your account and all data. This action is irreversible—proceed with caution.")
      ).toBeVisible();
    })();

    await step("Open owner's actions menu & verify self-action restrictions")(async () => {
      await page.goto("/account/users");

      // Wait for page to load
      await expect(page.getByRole("heading", { name: "Users" })).toBeVisible();

      // Wait for table to be present
      await page.waitForSelector("tbody tr", { state: "attached" });

      // Find the owner's own row by looking for the email
      const ownerRow = page.locator("tbody tr").filter({ hasText: owner.email }).first();

      // Click the actions button using JavaScript to bypass visibility checks
      const actionsButton = ownerRow.locator("button[aria-label='User actions']").first();
      await actionsButton.dispatchEvent("click");

      // Verify delete menu item is disabled (self-protection)
      await expect(page.getByRole("menuitem", { name: "Delete" })).toBeDisabled();
      // Verify change role menu item is disabled (self-protection)
      await expect(page.getByRole("menuitem", { name: "Change role" })).toBeDisabled();

      // Click outside the menu to close it
      await page.locator("body").click({ position: { x: 10, y: 10 } });

      // Wait for menu to close
      await expect(page.getByRole("menu")).not.toBeVisible();
    })();

    await step("Invite member user & verify user appears in table")(async () => {
      // Invite member user
      await page.getByRole("button", { name: "Invite user" }).click();
      await page.getByRole("textbox", { name: "Email" }).fill(member.email);
      await page.getByRole("button", { name: "Send invite" }).click();
      await expectToastMessage(context, "User invited successfully");
      await expect(page.getByRole("dialog")).not.toBeVisible();

      // Ensure the invitation is complete and the page is stable before proceeding
      await expect(page.locator("tbody").first()).toContainText(member.email);
    })();

    await step("Log out from owner and log in as member & verify authentication")(async () => {
      // Ensure the user table is stable and all users are loaded
      await expect(page.locator("tbody").first().locator("tr")).toHaveCount(2); // owner + member

      // Ensure the invite button is visible and the page is fully interactive
      await expect(page.getByRole("button", { name: "Invite user" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Invite user" })).toBeEnabled();

      // Verify user emails are visible in the table to ensure data is loaded
      await expect(page.locator("tbody").first()).toContainText(owner.email);
      await expect(page.locator("tbody").first()).toContainText(member.email);

      // Mark 401 as expected during logout transition (React Query may have in-flight requests)
      context.monitoring.expectedStatusCodes.push(401);

      // Navigate away from users page first to prevent background requests
      await page.goto("/dashboard");
      await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();

      const triggerButton = page.getByRole("button", { name: "User menu" });
      await triggerButton.dispatchEvent("click");
      const userMenu = page.getByRole("menu");
      await expect(userMenu).toBeVisible();

      // Click menu item with JavaScript evaluate to bypass stability check during animation
      const logoutMenuItem = page.getByRole("menuitem", { name: "Log out" });
      await expect(logoutMenuItem).toBeVisible();
      await logoutMenuItem.dispatchEvent("click");

      // Wait for logout to complete and page to navigate
      await expect(page).toHaveURL("/login?returnPath=%2Fdashboard");

      // Accept whatever return path we get
      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();

      // Login as member
      await page.getByRole("textbox", { name: "Email" }).fill(member.email);
      await page.getByRole("button", { name: "Log in with email" }).click();
      await expect(page.getByRole("heading", { name: "Enter your verification code" })).toBeVisible();
      await typeOneTimeCode(page, getVerificationCode());

      // Complete welcome flow for member (profile setup only)
      await expect(page).toHaveURL(/\/welcome/);
      await expect(page.getByRole("heading", { name: "Let's set up your profile" })).toBeVisible();
      await page.getByRole("textbox", { name: "First name" }).fill(member.firstName);
      await page.getByRole("textbox", { name: "Last name" }).fill(member.lastName);
      await page.getByRole("button", { name: "Continue" }).click();

      await expect(page).toHaveURL("/dashboard");
      await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();
    })();

    await step("Navigate to users page as Member & verify invite button is hidden")(async () => {
      await page.goto("/account/users");

      await expect(page.getByRole("heading", { name: "Users" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Invite user" })).not.toBeVisible();
    })();

    await step("Navigate to account settings as Member & verify tenant name field is readonly")(async () => {
      await page.goto("/account/settings");

      // Members should see readonly account name field
      await expect(page.getByRole("heading", { name: "Account settings" })).toBeVisible();
      await expect(page.getByRole("textbox", { name: "Account name" })).toHaveAttribute("readonly");
      await expect(page.getByText("Only account owners can modify the account name")).toBeVisible();
      await expect(page.getByRole("button", { name: "Save changes" })).not.toBeVisible();
    })();

    await step("Submit a logo upload as Member with the antiforgery header & verify it is forbidden")(async () => {
      const tenantBefore = await (await page.request.get("/api/account/tenants/current")).json();

      const response = await submitUploadFromBrowser(
        page,
        "/api/account/tenants/current/update-logo",
        pngImageBase64,
        true
      );

      await expectNetworkErrors(context, [403]);
      expect(response.status).toBe(403);
      const tenantAfter = await (await page.request.get("/api/account/tenants/current")).json();
      expect(tenantAfter.logoUrl).toBe(tenantBefore.logoUrl);
    })();

    await step("Verify danger zone is hidden for Member")(async () => {
      // Danger zone should be hidden from Members
      await expect(page.getByRole("heading", { name: "Danger zone" })).not.toBeVisible();
      await expect(page.getByRole("button", { name: "Delete account" })).not.toBeVisible();
      await expect(
        page.getByText("Delete your account and all data. This action is irreversible—proceed with caution.")
      ).not.toBeVisible();
    })();

    await step("Open member's actions menu & verify self-action restrictions")(async () => {
      await page.goto("/account/users");

      // Wait for page to load
      await expect(page.getByRole("heading", { name: "Users" })).toBeVisible();

      // Find the member's own row by filtering by email - use first() to handle duplicates
      const memberRow = page.locator("tbody tr").filter({ hasText: member.email }).first();
      const memberActionsButton = memberRow.locator("button[aria-label='User actions']").first();
      await memberActionsButton.dispatchEvent("click");

      // Verify delete and change role menu items are not visible (members don't see these options)
      await expect(page.getByRole("menuitem", { name: "Delete" })).not.toBeVisible();
      await expect(page.getByRole("menuitem", { name: "Change role" })).not.toBeVisible();

      // Verify only View profile is available
      await expect(page.getByRole("menuitem", { name: "View profile" })).toBeVisible();

      // Click outside the menu to close it
      await page.locator("body").click({ position: { x: 10, y: 10 } });
    })();

    // === ACCESS DENIED PAGE TESTS ===
    await step("Navigate to recycle-bin as Member & verify access denied page displays")(async () => {
      await page.goto("/account/users/recycle-bin");

      await expect(page.getByRole("heading", { name: "Access denied" })).toBeVisible();
      await expect(page.getByText("You do not have permission to access this page.")).toBeVisible();
      await expect(page.getByRole("button", { name: "Go to home" })).toBeVisible();
    })();

    await step("Click Go to home on access denied page & verify navigation to home")(async () => {
      await page.getByRole("button", { name: "Go to home" }).click();

      await expect(page).toHaveURL("/dashboard");
    })();
  });

  /**
   * BULK DELETE PERMISSION TESTS
   *
   * Tests that bulk delete functionality is only available to Owners.
   */
  test("should show bulk delete controls only for Owners", async ({ page }) => {
    const context = createTestContext(page);
    const owner = testUser();
    const member = testUser();

    const user1 = testUser();
    const user2 = testUser();

    await step("Create owner account with signup flow & verify home page")(async () => {
      await completeSignupFlow(page, expect, owner, context);
      await expect(page.getByRole("heading", { name: "Your dashboard is empty" })).toBeVisible();
    })();

    await step("Set account name & verify save confirmation")(async () => {
      await page.goto("/account/settings");
      await expect(page.getByRole("heading", { name: "Account settings" })).toBeVisible();
      await page.getByRole("textbox", { name: "Account name" }).fill("Test Organization");
      await page.getByRole("button", { name: "Save changes" }).click();
      await expectToastMessage(context, "Account settings updated successfully");
    })();

    await step("Navigate to users page & verify owner is listed")(async () => {
      await page.goto("/account/users");
      await expect(page.getByRole("heading", { name: "Users" })).toBeVisible();
    })();

    await step("Invite first test user & verify user appears in table")(async () => {
      await page.getByRole("button", { name: "Invite user" }).click();
      await page.getByRole("textbox", { name: "Email" }).fill(user1.email);
      await page.getByRole("button", { name: "Send invite" }).click();

      await expectToastMessage(context, "User invited successfully");
      await expect(page.getByRole("dialog")).not.toBeVisible();
      await expect(page.locator("tbody").first()).toContainText(user1.email);
    })();

    await step("Invite second test user & verify user appears in table")(async () => {
      await page.getByRole("button", { name: "Invite user" }).click();
      await page.getByRole("textbox", { name: "Email" }).fill(user2.email);
      await page.getByRole("button", { name: "Send invite" }).click();

      await expectToastMessage(context, "User invited successfully");
      await expect(page.getByRole("dialog")).not.toBeVisible();
      await expect(page.locator("tbody").first()).toContainText(user2.email);
    })();

    await step("Invite member user & verify all users are in table")(async () => {
      await page.getByRole("button", { name: "Invite user" }).click();
      await page.getByRole("textbox", { name: "Email" }).fill(member.email);
      await page.getByRole("button", { name: "Send invite" }).click();

      await expectToastMessage(context, "User invited successfully");
      await expect(page.getByRole("dialog")).not.toBeVisible();
      await expect(page.locator("tbody").first()).toContainText(member.email);
      // Should now have owner + 3 invited users = 4 total
      await expect(page.locator("tbody").first().locator("tr")).toHaveCount(4);
    })();

    await step("Select multiple users as Owner & verify bulk delete button appears")(async () => {
      // Set desktop viewport to enable multi-select mode
      await page.setViewportSize({ width: 1280, height: 720 });

      // Wait for table to re-render after viewport change and verify rows are visible
      const table = page.getByRole("table", { name: "Users" });
      await expect(table).toBeVisible();
      const rows = table.locator("tbody tr");
      await expect(rows).toHaveCount(4);

      const secondRow = rows.nth(1); // First invited user
      const thirdRow = rows.nth(2); // Second invited user

      // Select first user
      await secondRow.click({ force: true });
      await expect(secondRow).toHaveAttribute("data-state", "selected");

      // Select second user with Ctrl/Cmd modifier for multi-select
      await thirdRow.click({ modifiers: ["ControlOrMeta"] });
      await expect(thirdRow).toHaveAttribute("data-state", "selected");
      await expect(secondRow).toHaveAttribute("data-state", "selected");

      // Verify bulk delete button is visible for Owner
      await expect(page.getByRole("button", { name: "Delete 2 users" })).toBeVisible();

      // Ensure the selections are stable and the UI has updated
      await expect(secondRow).toHaveAttribute("data-state", "selected");
      await expect(thirdRow).toHaveAttribute("data-state", "selected");
    })();

    await step("Log out as owner and log in as member & verify authentication")(async () => {
      // Ensure the bulk delete button is still visible and selections are stable
      await expect(page.getByRole("button", { name: "Delete 2 users" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Delete 2 users" })).toBeEnabled();

      // Verify that the selected rows are still selected
      const allRows = page.locator("tbody").first().locator("tr");
      const secondRow = allRows.nth(1);
      const thirdRow = allRows.nth(2);
      await expect(secondRow).toHaveAttribute("data-state", "selected");
      await expect(thirdRow).toHaveAttribute("data-state", "selected");

      // Mark 401 as expected during logout transition (React Query may have in-flight requests)
      context.monitoring.expectedStatusCodes.push(401);

      // Navigate away from users page first to prevent background requests
      await page.goto("/dashboard");
      await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();

      const triggerButton = page.getByRole("button", { name: "User menu" });
      await triggerButton.dispatchEvent("click");
      const userMenu = page.getByRole("menu");
      await expect(userMenu).toBeVisible();

      // Click menu item with JavaScript evaluate to bypass stability check during animation
      const logoutMenuItem = page.getByRole("menuitem", { name: "Log out" });
      await expect(logoutMenuItem).toBeVisible();
      await logoutMenuItem.dispatchEvent("click");

      // Wait for logout to complete and page to navigate
      await expect(page).toHaveURL("/login?returnPath=%2Fdashboard");

      // Accept whatever return path we get
      await expect(page.getByRole("heading", { name: "Hi! Welcome back" })).toBeVisible();

      // Login as member
      await page.getByRole("textbox", { name: "Email" }).fill(member.email);
      await page.getByRole("button", { name: "Log in with email" }).click();
      await expect(page.getByRole("heading", { name: "Enter your verification code" })).toBeVisible();
      await typeOneTimeCode(page, getVerificationCode());

      // Complete welcome flow for member (profile setup only)
      await expect(page).toHaveURL(/\/welcome/);
      await expect(page.getByRole("heading", { name: "Let's set up your profile" })).toBeVisible();
      await page.getByRole("textbox", { name: "First name" }).fill(member.firstName);
      await page.getByRole("textbox", { name: "Last name" }).fill(member.lastName);
      await page.getByRole("button", { name: "Continue" }).click();

      await expect(page).toHaveURL("/dashboard");
      await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();
    })();

    await step("Navigate to users page as Member & verify no bulk operations available")(async () => {
      // Set viewport to 2xl to avoid side pane backdrop issues before navigating
      await page.setViewportSize({ width: 1536, height: 1024 });
      await page.goto("/account/users");

      // Ensure we can see the users that were created
      // Use first tbody due to mobile rendering creating duplicate tables
      await expect(page.locator("tbody").first().locator("tr")).toHaveCount(4);

      const table = page.getByRole("table", { name: "Users" });
      await expect(table).toBeVisible();
      const rows = table.locator("tbody tr");
      const secondRow = rows.nth(1);
      const thirdRow = rows.nth(2);

      // Select first user as Member
      await secondRow.scrollIntoViewIfNeeded();
      await secondRow.click();
      await expect(secondRow).toHaveAttribute("data-state", "selected");

      // Select second user with Ctrl/Cmd modifier
      await thirdRow.click({ modifiers: ["ControlOrMeta"] });
      await expect(thirdRow).toHaveAttribute("data-state", "selected");

      // Verify bulk delete button is NOT visible for Member even with selections
      await expect(page.getByRole("button", { name: "Delete 2 users" })).not.toBeVisible();
      await expect(page.getByRole("button", { name: "Delete user" })).not.toBeVisible();

      // Reset viewport
      await page.setViewportSize({ width: 1280, height: 720 });
    })();
  });
});
