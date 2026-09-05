import { Trans } from "@lingui/react/macro";
import { Badge } from "@repo/ui/components/Badge";
import { Button } from "@repo/ui/components/Button";
import { Card } from "@repo/ui/components/Card";
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from "@repo/ui/components/Empty";
import { Skeleton } from "@repo/ui/components/Skeleton";
import { useFormatDate } from "@repo/ui/hooks/useSmartDate";
import { ShieldCheckIcon } from "lucide-react";
import { useState } from "react";

import { api, ExternalProviderType, IdentityAssuranceLevel } from "@/shared/lib/api/client";

import { RevokeIdentityVerificationDialog } from "./RevokeIdentityVerificationDialog";

interface UserIdentityVerificationSectionProps {
  userId: string;
}

export function UserIdentityVerificationSection({ userId }: Readonly<UserIdentityVerificationSectionProps>) {
  const [isRevokeDialogOpen, setIsRevokeDialogOpen] = useState(false);

  const { data, isLoading, isError } = api.useQuery("get", "/api/back-office/users/{id}/identity-verification", {
    params: { path: { id: userId } }
  });

  if (isLoading) {
    return <UserIdentityVerificationSkeleton />;
  }

  // Most people never verify, so the unverified case is the common one and must not read as a failure. A failed
  // request is deliberately not shown as "not verified" either, because that would be an untrue statement about a
  // person's identity rather than a missing value.
  if (isError || !data?.isVerified) {
    return (
      <section className="flex flex-col gap-3">
        <h3>
          <Trans>Identity verification</Trans>
        </h3>
        <Empty className="border">
          <EmptyHeader>
            <EmptyTitle>
              {isError ? <Trans>Verification status unavailable</Trans> : <Trans>Not verified</Trans>}
            </EmptyTitle>
            <EmptyDescription>
              {isError ? (
                <Trans>The verification status could not be loaded. Try again.</Trans>
              ) : (
                <Trans>This person has not proven their identity with an electronic ID.</Trans>
              )}
            </EmptyDescription>
          </EmptyHeader>
        </Empty>
      </section>
    );
  }

  return (
    <section className="flex flex-col gap-3">
      <h3>
        <Trans>Identity verification</Trans>
      </h3>
      <Card className="flex flex-col gap-4 p-4">
        <div className="flex flex-wrap items-center gap-2">
          <ShieldCheckIcon className="size-5 text-muted-foreground" aria-hidden={true} />
          <span className="text-sm font-medium">{getProviderLabel(data.provider)}</span>
          <Badge variant="secondary">{getAssuranceLevelLabel(data.assuranceLevel)}</Badge>
        </div>

        <dl className="grid gap-3 text-sm sm:grid-cols-2">
          <VerificationFact label={<Trans>Verified on</Trans>} value={data.verifiedAt} />
          {/* The provider's own authentication time, which is what freshness is measured against. It can differ
              from when the row was written, and a support conversation about a stale verification needs both. */}
          <VerificationFact label={<Trans>Authenticated on</Trans>} value={data.authenticatedAt} />
        </dl>

        <div className="flex sm:justify-end">
          <Button type="button" variant="destructive" onClick={() => setIsRevokeDialogOpen(true)}>
            <Trans>Revoke verification</Trans>
          </Button>
        </div>
      </Card>

      <RevokeIdentityVerificationDialog
        userId={userId}
        isOpen={isRevokeDialogOpen}
        onOpenChange={setIsRevokeDialogOpen}
      />
    </section>
  );
}

function VerificationFact({ label, value }: Readonly<{ label: React.ReactNode; value: string | null | undefined }>) {
  const formatDate = useFormatDate();

  return (
    <div className="flex flex-col gap-1">
      <dt className="text-muted-foreground">{label}</dt>
      <dd>{value ? formatDate(value) : <Trans>Unknown</Trans>}</dd>
    </div>
  );
}

function getProviderLabel(provider: ExternalProviderType | null | undefined) {
  switch (provider) {
    case ExternalProviderType.MitId:
      return <Trans>Verified with MitID</Trans>;
    case ExternalProviderType.Google:
      return <Trans>Verified with Google</Trans>;
    case ExternalProviderType.Entra:
      return <Trans>Verified with Microsoft</Trans>;
    default:
      return <Trans>Verified</Trans>;
  }
}

function getAssuranceLevelLabel(assuranceLevel: IdentityAssuranceLevel | null | undefined) {
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

function UserIdentityVerificationSkeleton() {
  return (
    <section className="flex flex-col gap-3">
      <Skeleton className="h-6 w-40" />
      <Skeleton className="h-36 w-full rounded-lg" />
    </section>
  );
}
