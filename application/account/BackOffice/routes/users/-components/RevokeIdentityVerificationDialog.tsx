import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogMedia,
  AlertDialogTitle
} from "@repo/ui/components/AlertDialog";
import { useQueryClient } from "@tanstack/react-query";
import { ShieldOffIcon } from "lucide-react";
import { toast } from "sonner";

import { api } from "@/shared/lib/api/client";

interface RevokeIdentityVerificationDialogProps {
  userId: string;
  isOpen: boolean;
  onOpenChange: (isOpen: boolean) => void;
}

export function RevokeIdentityVerificationDialog({
  userId,
  isOpen,
  onOpenChange
}: Readonly<RevokeIdentityVerificationDialogProps>) {
  const queryClient = useQueryClient();

  const revokeMutation = api.useMutation("delete", "/api/back-office/users/{id}/identity-verification", {
    onSuccess: async () => {
      toast.success(t`Identity verification revoked`);
      await queryClient.invalidateQueries({
        predicate: (query) =>
          Array.isArray(query.queryKey) &&
          query.queryKey[0] === "get" &&
          query.queryKey[1] === "/api/back-office/users/{id}/identity-verification"
      });
      onOpenChange(false);
    }
  });

  const handleRevoke = () => {
    revokeMutation.mutate({ params: { path: { id: userId } } });
  };

  return (
    <AlertDialog open={isOpen} onOpenChange={onOpenChange} trackingTitle="Revoke identity verification">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogMedia className="bg-destructive/10">
            <ShieldOffIcon className="text-destructive" />
          </AlertDialogMedia>
          <AlertDialogTitle>
            <Trans>Revoke identity verification</Trans>
          </AlertDialogTitle>
          <AlertDialogDescription>
            <Trans>
              This clears the proof that this person verified their identity, and lets them verify again with a
              different one. Do this when the wrong identity was bound, for example on a shared device. It cannot be
              undone, and only the person themselves can verify again.
            </Trans>
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel variant="secondary" disabled={revokeMutation.isPending}>
            <Trans>Cancel</Trans>
          </AlertDialogCancel>
          <AlertDialogAction variant="destructive" isPending={revokeMutation.isPending} onClick={handleRevoke}>
            <Trans>Revoke verification</Trans>
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
