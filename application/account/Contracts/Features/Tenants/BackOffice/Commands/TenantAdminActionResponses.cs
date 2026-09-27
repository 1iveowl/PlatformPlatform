using JetBrains.Annotations;

namespace Account.Features.Tenants.BackOffice.Commands;

// The results of the back office's admin actions on an account, as POST /api/back-office/tenants/{id}/reconcile-with-stripe
// and /{id}/replay-archived-stripe-events return them. The command records stay with their handlers in the account API.

[PublicAPI]
public sealed record ReconcileTenantWithStripeResponse(
    int BillingEventsAppended,
    bool HasDriftDetected,
    int DriftDiscrepancyCount,
    DateTimeOffset ReconciledAt,
    ArchivedEventsAwaitingConfirmation? ArchivedEventsAwaitingConfirmation
);

/// <summary>
///     Set on <see cref="ReconcileTenantWithStripeResponse" /> when the local stripe_events archive contains
///     events older than Stripe's 30-day events.list retention window that have no matching billing_events
///     row yet. The reconcile flow never auto-replays archive data; surfacing this block tells the
///     back-office admin to confirm before the replay command projects the cold-backup payloads into the
///     BillingEvent ledger.
/// </summary>
[PublicAPI]
public sealed record ArchivedEventsAwaitingConfirmation(int Count, DateTimeOffset OldestOccurredAt, DateTimeOffset NewestOccurredAt);

[PublicAPI]
public sealed record ReplayArchivedTenantStripeEventsResponse(int BillingEventsAppended, DateTimeOffset ReplayedAt);
