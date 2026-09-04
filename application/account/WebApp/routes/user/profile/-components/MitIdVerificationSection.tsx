import { Trans } from "@lingui/react/macro";
import { useFeatureFlag } from "@repo/infrastructure/featureFlags/useFeatureFlag";
import { Badge } from "@repo/ui/components/Badge";
import { Button } from "@repo/ui/components/Button";
import { Separator } from "@repo/ui/components/Separator";
import { Skeleton } from "@repo/ui/components/Skeleton";
import { useFormatDate } from "@repo/ui/hooks/useSmartDate";
import { ShieldCheckIcon } from "lucide-react";
import { useState } from "react";

import { api, ExternalProviderType, IdentityAssuranceLevel } from "@/shared/lib/api/client";

export function MitIdVerificationSection() {
  const { enabled: isMitIdVerificationEnabled } = useFeatureFlag("mitid-verification");

  if (!isMitIdVerificationEnabled) {
    return null;
  }

  return <VerificationSection />;
}

function VerificationSection() {
  const [isRedirecting, setIsRedirecting] = useState(false);

  const { data: verificationStatus, isLoading } = api.useQuery("get", "/api/account/authentication/verification");

  const startVerificationMutation = api.useMutation(
    "post",
    "/api/account/authentication/{provider}/verification/start",
    {
      onSuccess: (data) => {
        // A full page navigation is required because the identity provider is a different origin
        globalThis.location.href = data.authorizationUrl;
      },
      onError: () => setIsRedirecting(false)
    }
  );

  const handleVerify = () => {
    setIsRedirecting(true);
    startVerificationMutation.mutate({
      params: { path: { provider: ExternalProviderType.MitId } },
      body: { returnPath: "/user/profile" }
    });
  };

  return (
    <div className="mt-12 flex flex-col gap-4">
      <h3>
        <Trans>Identity verification</Trans>
      </h3>
      <Separator />
      <p className="text-sm text-muted-foreground">
        <Trans>Prove who you are with MitID. Your name and personal identification number are not stored.</Trans>
      </p>

      {isLoading && <Skeleton className="h-10 w-40" />}

      {!isLoading && verificationStatus?.isVerified && (
        <VerifiedState assuranceLevel={verificationStatus.assuranceLevel} verifiedAt={verificationStatus.verifiedAt} />
      )}

      {!isLoading && !verificationStatus?.isVerified && (
        <div className="flex sm:justify-start">
          <Button
            type="button"
            variant="outline"
            onClick={handleVerify}
            isPending={isRedirecting}
            disabled={isRedirecting}
          >
            {!isRedirecting && <ShieldCheckIcon className="size-5" aria-hidden={true} />}
            {isRedirecting ? <Trans>Redirecting...</Trans> : <Trans>Verify with MitID</Trans>}
          </Button>
        </div>
      )}
    </div>
  );
}

type VerifiedStateProps = {
  assuranceLevel: IdentityAssuranceLevel | null;
  verifiedAt: string | null;
};

function VerifiedState({ assuranceLevel, verifiedAt }: Readonly<VerifiedStateProps>) {
  const formatDate = useFormatDate();
  const verifiedDate = formatDate(verifiedAt);

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <ShieldCheckIcon className="size-5 text-muted-foreground" aria-hidden={true} />
        <span className="text-sm">
          <Trans>Verified with MitID</Trans>
        </span>
        <Badge variant="secondary">{getAssuranceLevelLabel(assuranceLevel)}</Badge>
      </div>
      <span className="text-sm text-muted-foreground">
        <Trans>Verified on {verifiedDate}</Trans>
      </span>
    </div>
  );
}

function getAssuranceLevelLabel(assuranceLevel: IdentityAssuranceLevel | null) {
  switch (assuranceLevel) {
    case IdentityAssuranceLevel.Low:
      return <Trans>Low assurance</Trans>;
    case IdentityAssuranceLevel.Substantial:
      return <Trans>Substantial assurance</Trans>;
    case IdentityAssuranceLevel.High:
      return <Trans>High assurance</Trans>;
    default:
      return <Trans>Unknown assurance</Trans>;
  }
}
