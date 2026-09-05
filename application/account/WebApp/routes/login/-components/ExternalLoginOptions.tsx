import { Trans } from "@lingui/react/macro";
import { useFeatureFlag } from "@repo/infrastructure/featureFlags/useFeatureFlag";
import { Button } from "@repo/ui/components/Button";

import googleIconUrl from "@/shared/images/google-icon.svg";
import microsoftIconUrl from "@/shared/images/microsoft-icon.svg";
import mitIdLogoWhiteUrl from "@/shared/images/mitid-logo-white.svg";
import { ExternalProviderType } from "@/shared/lib/api/client";

type ExternalLoginOptionsProps = {
  pendingProvider: ExternalProviderType | null;
  isPending: boolean;
  onSelect: (provider: ExternalProviderType) => void;
};

/**
 * The providers this deployment offers as an alternative to an email one-time password. Renders nothing at all when
 * none are enabled, so the divider above them never appears on its own.
 */
export function ExternalLoginOptions({ pendingProvider, isPending, onSelect }: Readonly<ExternalLoginOptionsProps>) {
  const { enabled: isGoogleOAuthEnabled } = useFeatureFlag("google-oauth");
  const { enabled: isEntraOAuthEnabled } = useFeatureFlag("entra-oauth");
  const { enabled: isMitIdLoginEnabled } = useFeatureFlag("mitid-login");

  if (!isGoogleOAuthEnabled && !isEntraOAuthEnabled && !isMitIdLoginEnabled) {
    return null;
  }

  return (
    <>
      <div className="flex w-full items-center gap-4">
        <div className="h-px flex-1 bg-border" />
        <span className="text-sm text-muted-foreground">
          <Trans>or</Trans>
        </span>
        <div className="h-px flex-1 bg-border" />
      </div>
      {isGoogleOAuthEnabled && (
        <Button
          type="button"
          variant="outline"
          className="w-full"
          onClick={() => onSelect(ExternalProviderType.Google)}
          isPending={pendingProvider === ExternalProviderType.Google}
          disabled={isPending}
        >
          {pendingProvider !== ExternalProviderType.Google && (
            <img src={googleIconUrl} alt="" aria-hidden="true" className="size-5" />
          )}
          {pendingProvider === ExternalProviderType.Google ? (
            <Trans>Redirecting...</Trans>
          ) : (
            <Trans>Log in with Google</Trans>
          )}
        </Button>
      )}
      {isEntraOAuthEnabled && (
        <Button
          type="button"
          variant="outline"
          className="w-full"
          onClick={() => onSelect(ExternalProviderType.Entra)}
          isPending={pendingProvider === ExternalProviderType.Entra}
          disabled={isPending}
        >
          {pendingProvider !== ExternalProviderType.Entra && (
            <img src={microsoftIconUrl} alt="" aria-hidden="true" className="size-5" />
          )}
          {pendingProvider === ExternalProviderType.Entra ? (
            <Trans>Redirecting...</Trans>
          ) : (
            <Trans>Log in with Microsoft</Trans>
          )}
        </Button>
      )}
      {isMitIdLoginEnabled && (
        <MitIdLoginButton pendingProvider={pendingProvider} isPending={isPending} onSelect={onSelect} />
      )}
    </>
  );
}

/**
 * MitID prescribes this button's colour, height, corner radius and typeface, and the label must be one of five
 * approved phrases, of which "Log in with MitID" is not one. Those values are audited by the broker, so they are
 * pinned here as a deliberate exception to the design system rather than mapped onto the nearest token, and the
 * button will not match the outline styling of the two beside it. The hover and active shades are opaque rather than
 * an opacity of the resting colour, because a translucent tint composites against the page and would resolve to a
 * different colour in the light and dark themes. The wordmark stands in for the word MitID and its alt text supplies
 * that word, so the accessible name is the full approved phrase without the visible text repeating it.
 */
function MitIdLoginButton({ pendingProvider, isPending, onSelect }: Readonly<ExternalLoginOptionsProps>) {
  return (
    <Button
      type="button"
      onClick={() => onSelect(ExternalProviderType.MitId)}
      isPending={pendingProvider === ExternalProviderType.MitId}
      disabled={isPending}
      className="h-12 w-full rounded-[4px] bg-[#0060e6] py-1 pr-3 pl-4 text-base font-semibold text-white hover:bg-[#0056cf] active:bg-[#004db8]"
      style={{ fontFamily: '"IBM Plex Sans", Helvetica, Arial, sans-serif' }}
    >
      {pendingProvider === ExternalProviderType.MitId ? (
        <Trans>Redirecting...</Trans>
      ) : (
        <Trans>
          Log on with <img src={mitIdLogoWhiteUrl} alt="MitID" className="relative -top-[1.5px] h-4 w-auto" />
        </Trans>
      )}
    </Button>
  );
}
