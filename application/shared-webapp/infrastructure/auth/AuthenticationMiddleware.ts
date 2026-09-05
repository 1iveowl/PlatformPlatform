import type { Middleware } from "openapi-fetch";

import { loginPath } from "./constants";
import { createLoginUrlWithReturnPath } from "./util";

type AuthenticationMiddlewareOptions = {
  customLoginPath?: string;
};

// IMPORTANT: Must be kept in sync with UnauthorizedReason enum in shared-kernel/SharedKernel/Authentication/UnauthorizedReason.cs
const UnauthorizedReason = {
  Revoked: "Revoked",
  ReplayAttackDetected: "ReplayAttackDetected",
  SessionNotFound: "SessionNotFound",
  TenantDeleted: "TenantDeleted"
} as const;

// Error codes used in /error page query parameter
export const ErrorCode = {
  SessionRevoked: "session_revoked",
  SessionNotFound: "session_not_found",
  UserNotFound: "user_not_found",
  IdentityMismatch: "identity_mismatch",
  SessionExpired: "session_expired",
  AuthenticationFailed: "authentication_failed",
  InvalidRequest: "invalid_request",
  AccessDenied: "access_denied",
  AccountAlreadyExists: "account_already_exists",
  TenantDeleted: "tenant_deleted",
  EmailNotProvided: "email_not_provided",
  IdentityAlreadyLinked: "identity_already_linked",
  AssuranceLevelInsufficient: "assurance_level_insufficient"
} as const;

const unauthorizedReasonHeaderKey = "x-unauthorized-reason";

function getErrorCodeFromUnauthorizedReason(reason: string | null): string | null {
  switch (reason) {
    case UnauthorizedReason.ReplayAttackDetected:
      // Replay detection is overwhelmingly false-positive-driven for legitimate users (e.g. closing
      // the browser mid cold-start refresh leaves their cookie stale and trips the grace check).
      // Skipping the error page sends them straight to the login screen — same as if their session
      // had simply expired. Server-side detection, telemetry, and session revocation remain intact
      // for monitoring genuine attacks; the curious user can review their sessions at /user/sessions.
      return null;
    case UnauthorizedReason.Revoked:
      return ErrorCode.SessionRevoked;
    case UnauthorizedReason.SessionNotFound:
      return ErrorCode.SessionNotFound;
    case UnauthorizedReason.TenantDeleted:
      return ErrorCode.TenantDeleted;
    default:
      return null;
  }
}

export function createAuthenticationMiddleware(options?: AuthenticationMiddlewareOptions): Middleware {
  return {
    onResponse(context) {
      if (context.response.status === 401) {
        const loginUrl = createLoginUrlWithReturnPath(options?.customLoginPath ?? loginPath);
        const unauthorizedReason = context.response.headers.get(unauthorizedReasonHeaderKey);
        const errorCode = getErrorCodeFromUnauthorizedReason(unauthorizedReason);

        if (errorCode) {
          const errorUrl = new URL(loginUrl);
          errorUrl.pathname = "/error";
          errorUrl.searchParams.set("error", errorCode);
          globalThis.location.href = errorUrl.href;
        } else {
          globalThis.location.href = loginUrl;
        }
      }
    }
  };
}
