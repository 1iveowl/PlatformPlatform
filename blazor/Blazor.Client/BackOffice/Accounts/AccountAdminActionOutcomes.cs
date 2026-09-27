// What the account's admin actions report, the React back office's reconcile and disaster recovery dialogs as toasts. The
// reconcile result is titled by whether drift remains; its text says the account matches Stripe when nothing was appended
// and no drift remains, how many billing events were appended when any were, and how many drift discrepancies remain
// otherwise. Drift that remains offers disaster recovery, the replay of the account's archived Stripe events.

using System.Globalization;
using Account.Features.Tenants.BackOffice.Commands;
using Blazor.Client.Forms;

namespace Blazor.Client.BackOffice.Accounts;

public sealed record AccountAdminActionToast(ToastKind Kind, string Title, string Message, bool OffersDisasterRecovery);

public static class AccountAdminActionOutcomes
{
    public static AccountAdminActionToast FromReconcile(ReconcileTenantWithStripeResponse result)
    {
        var title = result.HasDriftDetected ? BackOfficeStrings.ReconcileCompleteWithDrift : BackOfficeStrings.ReconcileComplete;
        var reconciledAt = AccountFormat.FormatDate(result.ReconciledAt);
        var message = result is { BillingEventsAppended: 0, HasDriftDetected: false }
            ? BackOfficeStrings.ReconcileMatchesStripe
            : result.BillingEventsAppended > 0
                ? string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.ReconcileAppendedEvents, result.BillingEventsAppended, reconciledAt)
                : string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.ReconcileDriftRemains, result.DriftDiscrepancyCount, reconciledAt);
        return new AccountAdminActionToast(result.HasDriftDetected ? ToastKind.Warning : ToastKind.Success, title, message, result.HasDriftDetected);
    }

    public static AccountAdminActionToast FromReplay(ReplayArchivedTenantStripeEventsResponse result)
    {
        var message = string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.DisasterRecoveryReplayed, result.BillingEventsAppended, AccountFormat.FormatDate(result.ReplayedAt));
        return new AccountAdminActionToast(ToastKind.Success, BackOfficeStrings.DisasterRecoveryComplete, message, false);
    }

    // The disaster recovery confirmation quotes the archived events reconcile found; opened without them, it names the caveat only
    public static string GetReplayConfirmation(ArchivedEventsAwaitingConfirmation? awaiting)
    {
        if (awaiting is null) return BackOfficeStrings.DisasterRecoveryConfirmation;

        return string.Format(
            CultureInfo.CurrentCulture, BackOfficeStrings.DisasterRecoveryArchivedConfirmation, awaiting.Count,
            AccountFormat.FormatDate(awaiting.OldestOccurredAt), AccountFormat.FormatDate(awaiting.NewestOccurredAt)
        );
    }
}
