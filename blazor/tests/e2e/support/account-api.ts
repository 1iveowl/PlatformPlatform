import { expect, type Page } from "@playwright/test";

/**
 * A user as the account API returns it in the users list and the single-user lookup
 */
export interface AccountApiUser {
  id: string;
  email: string;
  role: "Owner" | "Admin" | "Member";
}

/**
 * The status and body of an account API response
 */
export interface AccountApiResponse {
  status: number;
  body: string;
}

/**
 * Send a request to the account API through the gateway from the page's document, as the page's signed-in user: the
 * request carries the session cookies and the antiforgery token the bootstrap endpoint issues for that session, exactly as
 * the Blazor client's handler chain sends it. Used to seed data the Blazor edition has no UI for yet and to prove that the
 * API itself rejects what the UI only disables.
 * @param page Playwright page instance of a signed-in user, on a page of the gateway's origin
 * @param method The HTTP method
 * @param path The account API path, for example "/api/account/users/invite"
 * @param data The JSON body, if any
 */
export function sendAccountApiRequest(page: Page, method: "GET" | "POST" | "PUT" | "DELETE", path: string, data?: object): Promise<AccountApiResponse> {
  return page.evaluate(
    async ({ method, path, data }) => {
      const bootstrap = await fetch("/api/account/bootstrap", { credentials: "same-origin" });
      const { antiforgeryToken } = (await bootstrap.json()) as { antiforgeryToken: string };
      const response = await fetch(path, {
        method,
        credentials: "same-origin",
        headers: { "content-type": "application/json", "x-xsrf-token": antiforgeryToken },
        body: data === undefined ? undefined : JSON.stringify(data)
      });
      return { status: response.status, body: await response.text() };
    },
    { method, path, data }
  );
}

/**
 * Invite users to the signed-in owner's account through the account API, the endpoint the React invite dialog calls
 * @param page Playwright page instance of a signed-in owner
 * @param emails The email addresses to invite
 */
export async function inviteUsersThroughAccountApi(page: Page, emails: string[]): Promise<void> {
  for (const email of emails) {
    const response = await sendAccountApiRequest(page, "POST", "/api/account/users/invite", { email });
    expect(response.status, `Invite ${email}: ${response.body}`).toBe(200);
  }
}

/**
 * Find a user of the signed-in user's account by email through the account API
 * @param page Playwright page instance of a signed-in user
 * @param email The exact email address
 */
export async function findUserThroughAccountApi(page: Page, email: string): Promise<AccountApiUser> {
  const response = await sendAccountApiRequest(page, "GET", `/api/account/users?Search=${encodeURIComponent(email)}`);
  expect(response.status).toBe(200);
  const { users } = JSON.parse(response.body) as { users: AccountApiUser[] };

  const user = users.find((candidate) => candidate.email === email);
  expect(user, `No user with email ${email}`).toBeDefined();
  return user!;
}

/**
 * Change a user's role through the account API and return the response, which the caller asserts
 * @param page Playwright page instance of a signed-in user
 * @param userId The id of the user whose role changes
 * @param userRole The new role
 */
export function changeUserRoleThroughAccountApi(page: Page, userId: string, userRole: AccountApiUser["role"]): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "PUT", `/api/account/users/${encodeURIComponent(userId)}/change-user-role`, { userRole });
}

/**
 * A session as the account API's sessions endpoint returns it
 */
export interface AccountApiSession {
  id: string;
  isCurrent: boolean;
}

/**
 * List the signed-in user's sessions through the account API
 * @param page Playwright page instance of a signed-in user
 */
export async function getSessionsThroughAccountApi(page: Page): Promise<AccountApiSession[]> {
  const response = await sendAccountApiRequest(page, "GET", "/api/account/authentication/sessions");
  expect(response.status, response.body).toBe(200);
  return (JSON.parse(response.body) as { sessions: AccountApiSession[] }).sessions;
}

/**
 * The id of the session the page's own cookies belong to, as the account API reports it
 * @param page Playwright page instance of a signed-in user
 */
export async function getCurrentSessionIdThroughAccountApi(page: Page): Promise<string> {
  const current = (await getSessionsThroughAccountApi(page)).filter((session) => session.isCurrent);
  expect(current).toHaveLength(1);
  return current[0].id;
}

/**
 * Revoke one session of the signed-in user by its exact id through the account API
 * @param page Playwright page instance of a signed-in user whose session is not the one revoked
 * @param sessionId The id of the session to revoke
 */
export async function revokeSessionThroughAccountApi(page: Page, sessionId: string): Promise<void> {
  const response = await sendAccountApiRequest(page, "DELETE", `/api/account/authentication/sessions/${encodeURIComponent(sessionId)}`);
  expect(response.status, response.body).toBe(200);
}

/**
 * Delete a user of the signed-in owner's account through the account API
 * @param page Playwright page instance of a signed-in owner
 * @param userId The id of the user to delete
 */
export async function deleteUserThroughAccountApi(page: Page, userId: string): Promise<void> {
  const response = await sendAccountApiRequest(page, "DELETE", `/api/account/users/${encodeURIComponent(userId)}`);
  expect(response.status, response.body).toBe(200);
}

/**
 * Expect an account API response to be a problem with the given status and detail
 * @param response The response to assert
 * @param status The expected HTTP status
 * @param detail The expected problem detail, in English as the API returns it
 */
export function expectAccountApiProblem(response: AccountApiResponse, status: number, detail: string): void {
  expect(response.status).toBe(status);
  expect((JSON.parse(response.body) as { detail?: string }).detail).toBe(detail);
}
