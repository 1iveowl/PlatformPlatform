using JetBrains.Annotations;

namespace Account.Features.Subscriptions.Domain;

// A billing drift finding the subscription stores and the back office's account detail carries, with the name, namespace and
// JSON shape the account API already uses.

[PublicAPI]
public sealed record DriftDiscrepancy(
    DriftDiscrepancyKind Kind,
    string Description,
    DriftSeverity Severity,
    BillingEventType? ExpectedEventType = null,
    string? ExpectedValue = null,
    string? ActualValue = null,
    DateTimeOffset? OccurredAt = null
);

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DriftDiscrepancyKind
{
    MissingEvent,
    ExtraEvent,
    FieldDisagree,
    SubscriptionStateMismatch,

    /// <summary>
    ///     A Stripe event arrived whose payload combined multiple state changes that the writer couldn't
    ///     decompose into a single domain transition (e.g. a customer.subscription.updated whose
    ///     previous_attributes contain both a cancel_at_period_end toggle and a price change). The
    ///     event is recorded as <c>BillingEventType.Unclassified</c>; this discrepancy surfaces it on
    ///     the drift banner so an admin can investigate in Stripe Dashboard.
    /// </summary>
    UnclassifiedStripeEvent,

    /// <summary>
    ///     Stripe sent an event whose <c>api_version</c> doesn't have a matching
    ///     <c>IStripeEventPayloadResolver</c>. The event is preserved unchanged in
    ///     <c>stripe_events</c>; the replayer skips it and surfaces this discrepancy so the
    ///     resolver-per-version mapping can be extended.
    /// </summary>
    UnsupportedStripeApiVersion,

    /// <summary>
    ///     The same Stripe event id was observed twice with different payloads (SHA-256 hash
    ///     mismatch on the second arrival). The original row is preserved; the divergence is
    ///     surfaced for forensic review. Either Stripe redelivered an event with mutated content
    ///     (their bug to investigate) or our hashing is broken (our bug to investigate).
    /// </summary>
    StripeEventPayloadDivergence,

    /// <summary>
    ///     A persisted BillingEvent row's denormalized fields (CommittedMrr, AmountDelta, PreviousAmount, NewAmount)
    ///     no longer match what a fresh replay produces, typically because an older event was recovered after a
    ///     newer event was already classified and persisted. The persisted row is left untouched per the
    ///     append-only invariant; this discrepancy surfaces the wrongness for operator review.
    /// </summary>
    BillingEventDenormalizationStale,

    /// <summary>
    ///     Stripe returned a payment with <c>total_taxes</c> greater than the display amount, which would
    ///     otherwise produce a negative <c>AmountExcludingTax</c>. The value is clamped at zero so the DB
    ///     CHECK does not reject the row (which would 500 the webhook and trigger infinite Stripe retries),
    ///     but the LTV totals silently undercount until the underlying Stripe anomaly is investigated.
    /// </summary>
    AmountExcludingTaxClamped,

    /// <summary>
    ///     The subscription has a <c>ScheduledPlan</c> set but <c>ScheduledPriceAmount</c> is null. The
    ///     MRR KPI falls back to the current (higher) price in this state, silently distorting BLENDED MRR.
    ///     Originates from edge cases in <c>SyncStateFromStripe</c> where a cancel-then-reschedule pair
    ///     landed in the same sync window and the diff-based transition detector did not fire; the
    ///     unconditional reconciliation in <c>SyncStateFromStripe</c> now prevents this, and this drift
    ///     check stands as defence-in-depth so any future regression surfaces on the next sync.
    /// </summary>
    ScheduledPriceMissing
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DriftSeverity
{
    Warning,
    Critical
}
