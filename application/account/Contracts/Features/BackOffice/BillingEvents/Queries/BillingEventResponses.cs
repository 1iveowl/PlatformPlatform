using Account.Features.Subscriptions.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;

namespace Account.Features.BackOffice.BillingEvents.Queries;

// The back office's billing events as GET /api/back-office/billing-events returns them, for every account or for one, and
// the sort values the query binds. The query record and its handler stay in the account API.

[PublicAPI]
public sealed record BillingEventsResponse(int TotalCount, int PageSize, int TotalPages, int CurrentPageOffset, BillingEventSummary[] BillingEvents);

[PublicAPI]
public sealed record BillingEventSummary(
    BillingEventId Id,
    TenantId TenantId,
    string TenantName,
    string? TenantLogoUrl,
    string? Country,
    BillingEventType EventType,
    SubscriptionPlan? FromPlan,
    SubscriptionPlan? ToPlan,
    decimal? AmountDelta,
    decimal? PreviousAmount,
    decimal? NewAmount,
    decimal CommittedMrr,
    string? Currency,
    DateTimeOffset OccurredAt
);

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortableBillingEventProperties
{
    OccurredAt,
    EventType,
    TenantName
}
