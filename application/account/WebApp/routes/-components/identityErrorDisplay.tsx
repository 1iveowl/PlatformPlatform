import { Trans } from "@lingui/react/macro";
import { ErrorCode } from "@repo/infrastructure/auth/AuthenticationMiddleware";
import { ShieldAlert } from "lucide-react";

import type { ErrorDisplay } from "./errorDisplay";

// The identity outcomes share an icon, and keeping them here keeps the main switch readable. The two that can only
// happen to a signed-in user send them back to the profile page, where the retry lives, rather than to login.
export function getIdentityErrorDisplay(error: string): ErrorDisplay {
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

  // Not a failure of the sign-in so much as a step that has not happened yet, so it sends the person to log in by
  // another means rather than to an administrator: verifying requires being signed in first.
  if (error === ErrorCode.IdentityNotVerified) {
    return {
      ...identityErrorDisplay,
      action: "login",
      title: <Trans>Identity not verified</Trans>,
      message: (
        <>
          <Trans>This MitID has not been used to verify an account.</Trans>
          <br />
          <Trans>Log in another way, then verify your identity from your profile to sign in with MitID.</Trans>
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
