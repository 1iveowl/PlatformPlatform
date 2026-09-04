import type { ReactNode } from "react";

import { Trans } from "@lingui/react/macro";
import { ErrorCode } from "@repo/infrastructure/auth/AuthenticationMiddleware";
import { AlertCircle, Building2, LogOut, MailX, ShieldAlert, UserX } from "lucide-react";

export type ErrorAction = "login" | "signup" | "contact" | "profile";

export type ErrorDisplay = {
  icon: ReactNode;
  iconBackground: string;
  title: ReactNode;
  message: ReactNode;
  action: ErrorAction;
  secondaryAction?: ErrorAction;
};

export const errorLabelMap: Record<string, string> = {
  [ErrorCode.SessionRevoked]: "Session ended",
  [ErrorCode.SessionNotFound]: "Session expired",
  [ErrorCode.SessionExpired]: "Session expired",
  [ErrorCode.UserNotFound]: "Account not found",
  [ErrorCode.AccountAlreadyExists]: "Account already exists",
  [ErrorCode.EmailNotProvided]: "Email address required",
  [ErrorCode.IdentityMismatch]: "Identity mismatch",
  [ErrorCode.IdentityAlreadyLinked]: "Identity already in use",
  [ErrorCode.AssuranceLevelInsufficient]: "Verification not strong enough",
  [ErrorCode.AuthenticationFailed]: "Authentication failed",
  [ErrorCode.InvalidRequest]: "Invalid request",
  [ErrorCode.AccessDenied]: "Access denied",
  [ErrorCode.TenantDeleted]: "Account deleted"
};

// The identity outcomes share an icon, and keeping them here keeps the main switch readable. The two that can only
// happen to a signed-in user send them back to the profile page, where the retry lives, rather than to login.
function getIdentityErrorDisplay(error: string): ErrorDisplay {
  const identityErrorDisplay = {
    icon: <ShieldAlert className="size-10 text-destructive" />,
    iconBackground: "bg-destructive/10",
    action: "contact" as const
  };

  if (error === ErrorCode.IdentityAlreadyLinked) {
    return {
      ...identityErrorDisplay,
      action: "profile",
      title: <Trans>Identity already in use</Trans>,
      message: (
        <>
          <Trans>This identity is already linked to another account.</Trans>
          <br />
          <Trans>You cannot resolve this yourself. Contact your account administrator.</Trans>
        </>
      )
    };
  }

  if (error === ErrorCode.AssuranceLevelInsufficient) {
    return {
      ...identityErrorDisplay,
      action: "profile",
      title: <Trans>Verification not strong enough</Trans>,
      message: (
        <>
          <Trans>Your identity could not be verified at the required level.</Trans>
          <br />
          <Trans>Please try again, or contact your account administrator.</Trans>
        </>
      )
    };
  }

  return {
    ...identityErrorDisplay,
    title: <Trans>Identity mismatch</Trans>,
    message: (
      <>
        <Trans>This account is linked to a different sign-in identity.</Trans>
        <br />
        <Trans>This can happen when email ownership has changed. Contact your account administrator.</Trans>
      </>
    )
  };
}

export function getErrorDisplay(error: string): ErrorDisplay {
  switch (error) {
    case ErrorCode.SessionRevoked:
      return {
        icon: <LogOut className="size-10 text-muted-foreground" />,
        iconBackground: "bg-muted",
        title: <Trans>Session ended</Trans>,
        message: (
          <>
            <Trans>Your session was ended from another device.</Trans>
            <br />
            <Trans>Please log in again to continue.</Trans>
          </>
        ),
        action: "login"
      };

    case ErrorCode.SessionNotFound:
    case ErrorCode.SessionExpired:
      return {
        icon: <LogOut className="size-10 text-muted-foreground" />,
        iconBackground: "bg-muted",
        title: <Trans>Session expired</Trans>,
        message: (
          <>
            <Trans>Your session has expired.</Trans>
            <br />
            <Trans>Please log in again to continue.</Trans>
          </>
        ),
        action: "login"
      };

    case ErrorCode.UserNotFound:
      return {
        icon: <UserX className="size-10 text-muted-foreground" />,
        iconBackground: "bg-muted",
        title: <Trans>Account not found</Trans>,
        message: <Trans>No account found for this email address. Please sign up to create an account.</Trans>,
        action: "signup",
        secondaryAction: "login"
      };

    case ErrorCode.AccountAlreadyExists:
      return {
        icon: <UserX className="size-10 text-muted-foreground" />,
        iconBackground: "bg-muted",
        title: <Trans>Account already exists</Trans>,
        message: <Trans>An account with this email already exists. Please log in instead.</Trans>,
        action: "login",
        secondaryAction: "signup"
      };

    case ErrorCode.EmailNotProvided:
      return {
        icon: <MailX className="size-10 text-muted-foreground" />,
        iconBackground: "bg-muted",
        title: <Trans>Email address required</Trans>,
        message: (
          <Trans>
            The identity provider did not share a verified email address, which is needed to create an account. Sign up
            with your email instead.
          </Trans>
        ),
        action: "signup",
        secondaryAction: "login"
      };

    case ErrorCode.IdentityMismatch:
    case ErrorCode.IdentityAlreadyLinked:
    case ErrorCode.AssuranceLevelInsufficient:
      return getIdentityErrorDisplay(error);

    case ErrorCode.AuthenticationFailed:
      return {
        icon: <AlertCircle className="size-10 text-destructive" />,
        iconBackground: "bg-destructive/10",
        title: <Trans>Authentication failed</Trans>,
        message: <Trans>We detected a security issue with your login attempt. Please try again.</Trans>,
        action: "login"
      };

    case ErrorCode.InvalidRequest:
      return {
        icon: <AlertCircle className="size-10 text-destructive" />,
        iconBackground: "bg-destructive/10",
        title: <Trans>Invalid request</Trans>,
        message: <Trans>The authentication request was invalid. Please try again.</Trans>,
        action: "login"
      };

    case ErrorCode.AccessDenied:
      return {
        icon: <AlertCircle className="size-10 text-muted-foreground" />,
        iconBackground: "bg-muted",
        title: <Trans>Access denied</Trans>,
        message: <Trans>Authentication was cancelled or denied. Please try again if you want to continue.</Trans>,
        action: "login"
      };

    case ErrorCode.TenantDeleted:
      return {
        icon: <Building2 className="size-10 text-destructive" />,
        iconBackground: "bg-destructive/10",
        title: <Trans>Account deleted</Trans>,
        message: (
          <>
            <Trans>Your account has been deleted.</Trans>
            <br />
            <Trans>Contact the account owner immediately if you believe this is incorrect.</Trans>
          </>
        ),
        action: "login"
      };

    default:
      return {
        icon: <AlertCircle className="size-10 text-destructive" />,
        iconBackground: "bg-destructive/10",
        title: <Trans>Something went wrong</Trans>,
        message: (
          <Trans>An unexpected error occurred. Please try again or contact support if the problem persists.</Trans>
        ),
        action: "login"
      };
  }
}
