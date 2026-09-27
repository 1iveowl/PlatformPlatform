using JetBrains.Annotations;

namespace Account.Features.Subscriptions.Domain;

// The subscription and billing types the back-office responses carry, with the name, namespace and JSON shape the account
// API already uses, so the OpenAPI document does not change.

[PublicAPI]
[IdPrefix("pymnt")]
[JsonConverter(typeof(StronglyTypedIdJsonConverter<string, PaymentTransactionId>))]
public sealed record PaymentTransactionId(string Value) : StronglyTypedUlid<PaymentTransactionId>(Value)
{
    public override string ToString()
    {
        return Value;
    }
}

[PublicAPI]
[IdPrefix("bilevt")]
[JsonConverter(typeof(StronglyTypedIdJsonConverter<string, BillingEventId>))]
public sealed record BillingEventId(string Value) : StronglyTypedUlid<BillingEventId>(Value)
{
    public override string ToString()
    {
        return Value;
    }
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubscriptionPlan
{
    Basis = 0,
    Standard = 1,
    Premium = 2
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentTransactionStatus
{
    Succeeded,
    Failed,
    Pending,
    Refunded,
    Cancelled
}

/// <summary>
///     The type of subscription-relevant Stripe event recorded by the BillingEvent log.
///     IMPORTANT: when adding a new value, also add it to the multi-select on /billing-events
///     (see <c>application/account/BackOffice/routes/billing-events/-components/BillingEventsToolbar.tsx</c>,
///     constant <c>ALL_EVENT_TYPES</c>). The toolbar is hand-maintained and does not enumerate the
///     enum at runtime, so operators won't be able to filter by a new type until that list is updated.
/// </summary>
[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BillingEventType
{
    SubscriptionCreated,
    SubscriptionRenewed,
    SubscriptionUpgraded,
    SubscriptionDowngradeScheduled,
    SubscriptionDowngradeCancelled,
    SubscriptionDowngraded,
    SubscriptionCancelled,
    SubscriptionReactivated,
    SubscriptionExpired,
    SubscriptionImmediatelyCancelled,
    SubscriptionSuspended,

    /// <summary>
    ///     Stripe transitioned the subscription's status from active to past_due (a payment failed).
    ///     Fires alongside <see cref="PaymentFailed" /> from the corresponding invoice.payment_failed event;
    ///     pairs with <see cref="SubscriptionReactivated" /> when payment recovers and status returns to active.
    ///     Carries forward CommittedMrr unchanged and AmountDelta=null: the customer is still on the plan,
    ///     just behind on payment.
    /// </summary>
    SubscriptionPastDue,
    PaymentFailed,
    PaymentRecovered,
    PaymentRefunded,
    BillingInfoAdded,
    BillingInfoUpdated,
    PaymentMethodUpdated,

    /// <summary>
    ///     A recognized subscription-relevant Stripe event that doesn't move state we care about (e.g.
    ///     a subscription_schedule.updated arriving with status=canceled after a cancellation, where
    ///     phases haven't changed). Hidden from the timeline UI; carries forward CommittedMrr unchanged
    ///     and AmountDelta=null so it's invisible to MRR trend computation.
    /// </summary>
    NoOp,

    /// <summary>
    ///     A Stripe event whose payload combines multiple state changes that the writer can't decompose
    ///     into a single domain transition (e.g. a customer.subscription.updated whose previous_attributes
    ///     contain both a cancel_at_period_end toggle and a price change). Triggers the drift banner so
    ///     an admin can investigate in Stripe Dashboard.
    /// </summary>
    Unclassified
}
