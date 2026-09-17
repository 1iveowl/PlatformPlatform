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
  loginMethod: string;
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

/**
 * A user of the signed-in user's account as the recycle bin endpoint returns it
 */
export interface AccountApiDeletedUser {
  id: string;
  email: string;
  role: AccountApiUser["role"];
}

/**
 * Invite one user to the signed-in user's account through the account API and return the response, which the caller asserts
 * @param page Playwright page instance of a signed-in user
 * @param email The email address to invite
 */
export function inviteUserThroughAccountApi(page: Page, email: string): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "POST", "/api/account/users/invite", { email });
}

/**
 * Soft delete one user through the account API and return the response, which the caller asserts
 * @param page Playwright page instance of a signed-in user
 * @param userId The id of the user to delete
 */
export function deleteUserResponseThroughAccountApi(page: Page, userId: string): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "DELETE", `/api/account/users/${encodeURIComponent(userId)}`);
}

/**
 * Soft delete several users in one request through the account API and return the response, which the caller asserts
 * @param page Playwright page instance of a signed-in user
 * @param userIds The ids of the users to delete
 */
export function bulkDeleteUsersThroughAccountApi(page: Page, userIds: string[]): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "POST", "/api/account/users/bulk-delete", { userIds });
}

/**
 * List the deleted users of the signed-in user's account through the account API and return the response
 * @param page Playwright page instance of a signed-in user
 */
export function getDeletedUsersResponseThroughAccountApi(page: Page): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "GET", "/api/account/users/deleted?PageSize=1000");
}

/**
 * The deleted users of the signed-in owner's or admin's account, read through the account API
 * @param page Playwright page instance of a signed-in owner or admin
 */
export async function getDeletedUsersThroughAccountApi(page: Page): Promise<AccountApiDeletedUser[]> {
  const response = await getDeletedUsersResponseThroughAccountApi(page);
  expect(response.status, response.body).toBe(200);
  return (JSON.parse(response.body) as { users: AccountApiDeletedUser[] }).users;
}

/**
 * The emails of the active users of the signed-in user's account, read through the account API
 * @param page Playwright page instance of a signed-in user
 */
export async function getUserEmailsThroughAccountApi(page: Page): Promise<string[]> {
  const response = await sendAccountApiRequest(page, "GET", "/api/account/users?PageSize=1000");
  expect(response.status, response.body).toBe(200);
  return (JSON.parse(response.body) as { users: AccountApiUser[] }).users.map((user) => user.email).sort();
}

/**
 * Restore one deleted user through the account API and return the response, which the caller asserts
 * @param page Playwright page instance of a signed-in user
 * @param userId The id of the deleted user
 */
export function restoreUserThroughAccountApi(page: Page, userId: string): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "POST", `/api/account/users/${encodeURIComponent(userId)}/restore`);
}

/**
 * Permanently delete one deleted user through the account API and return the response, which the caller asserts
 * @param page Playwright page instance of a signed-in user
 * @param userId The id of the deleted user
 */
export function purgeUserThroughAccountApi(page: Page, userId: string): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "DELETE", `/api/account/users/${encodeURIComponent(userId)}/purge`);
}

/**
 * Permanently delete several deleted users in one request through the account API and return the response
 * @param page Playwright page instance of a signed-in user
 * @param userIds The ids of the deleted users
 */
export function bulkPurgeUsersThroughAccountApi(page: Page, userIds: string[]): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "POST", "/api/account/users/deleted/bulk-purge", { userIds });
}

/**
 * Permanently delete every user in the signed-in user's recycle bin through the account API and return the response
 * @param page Playwright page instance of a signed-in user
 */
export function emptyRecycleBinThroughAccountApi(page: Page): Promise<AccountApiResponse> {
  return sendAccountApiRequest(page, "POST", "/api/account/users/deleted/empty-recycle-bin");
}

/**
 * Update the signed-in user's own profile through the account API. The endpoint asks the gateway to refresh the
 * authentication tokens, so the response carries cookies with claims issued from the user's current role
 * @param page Playwright page instance of a signed-in user
 * @param profile The profile to save
 */
export async function refreshClaimsByUpdatingProfileThroughAccountApi(page: Page, profile: { firstName: string; lastName: string }): Promise<void> {
  const response = await sendAccountApiRequest(page, "PUT", "/api/account/users/me", { ...profile, title: "" });
  expect(response.status, response.body).toBe(200);
}

/**
 * Well-formed user ids that belong to no user: the given id with its third-to-last character changed, so no id is the given
 * user's, and its last two characters numbering the ids
 * @param userId A real user id to derive the ids from
 * @param count The number of ids
 */
export function unknownUserIds(userId: string, count: number): string[] {
  const alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
  const changed = alphabet[(alphabet.indexOf(userId.at(-3)!.toUpperCase()) + 1) % alphabet.length];
  return Array.from({ length: count }, (_, index) => `${userId.slice(0, -3)}${changed}${alphabet[Math.floor(index / 32) % 32]}${alphabet[index % 32]}`);
}

/**
 * Expect an account API response to be a validation problem with the given message on the given property
 * @param response The response to assert
 * @param property The camel-cased property the message belongs to
 * @param message The expected message, in English as the API returns it
 */
export function expectAccountApiValidationProblem(response: AccountApiResponse, property: string, message: string): void {
  expect(response.status).toBe(400);
  expect((JSON.parse(response.body) as { errors?: Record<string, string[]> }).errors).toEqual({ [property]: [message] });
}

/**
 * A file the avatar upload is sent: its name, declared content type and bytes
 */
export interface AvatarUploadFile {
  name: string;
  mimeType: string;
  buffer: Buffer;
}

/**
 * Which antiforgery token a direct avatar upload carries: the token the bootstrap issues for the page's own session, none,
 * or a token issued to another signed-in user
 */
export type AvatarUploadToken = { kind: "session" } | { kind: "none" } | { kind: "other"; token: string };

/**
 * Post a file to the avatar upload endpoint as a browser multipart form from the page's document, with the session
 * cookies and the chosen antiforgery token, bypassing the Blazor picker's client-side checks
 * @param page Playwright page instance of a signed-in user, on a page of the gateway's origin
 * @param file The file to send in the "file" field
 * @param token The antiforgery token the request carries
 */
export function uploadAvatarThroughAccountApi(page: Page, file: AvatarUploadFile, token: AvatarUploadToken): Promise<AccountApiResponse> {
  return page.evaluate(
    async ({ name, mimeType, bytes, token }) => {
      const headers: Record<string, string> = {};
      if (token.kind === "session") {
        const bootstrap = await fetch("/api/account/bootstrap", { credentials: "same-origin" });
        headers["x-xsrf-token"] = ((await bootstrap.json()) as { antiforgeryToken: string }).antiforgeryToken;
      }
      if (token.kind === "other") headers["x-xsrf-token"] = token.token;
      const form = new FormData();
      form.append("file", new File([new Uint8Array(bytes)], name, { type: mimeType }));
      const response = await fetch("/api/account/users/me/update-avatar", { method: "POST", credentials: "same-origin", headers, body: form });
      return { status: response.status, body: await response.text() };
    },
    { name: file.name, mimeType: file.mimeType, bytes: [...file.buffer], token }
  );
}

/**
 * The antiforgery token the bootstrap issues for the page's signed-in session
 * @param page Playwright page instance of a signed-in user, on a page of the gateway's origin
 */
export function readAntiforgeryToken(page: Page): Promise<string> {
  return page.evaluate(async () => {
    const bootstrap = await fetch("/api/account/bootstrap", { credentials: "same-origin" });
    return ((await bootstrap.json()) as { antiforgeryToken: string }).antiforgeryToken;
  });
}

/**
 * The stored avatar URL of the signed-in user, read from the account API, or null when the user has none
 * @param page Playwright page instance of a signed-in user, on a page of the gateway's origin
 */
export async function getAvatarUrlThroughAccountApi(page: Page): Promise<string | null> {
  const response = await sendAccountApiRequest(page, "GET", "/api/account/users/me");
  expect(response.status, response.body).toBe(200);
  return (JSON.parse(response.body) as { avatarUrl: string | null }).avatarUrl;
}
