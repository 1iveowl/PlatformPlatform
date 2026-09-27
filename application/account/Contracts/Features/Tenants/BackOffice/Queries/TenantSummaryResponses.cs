using Account.Features.Subscriptions.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;

namespace Account.Features.Tenants.BackOffice.Queries;

// The back office's accounts list as GET /api/back-office/tenants returns it: one page of tenant rows with their plan, revenue,
// renewal, planned change and first owner, and the filter and sort values the query binds. The query record and the
// factory that builds a row from the aggregates stay with their handler in the account API.

[PublicAPI]
public sealed record TenantsResponse(int TotalCount, int PageSize, int TotalPages, int CurrentPageOffset, TenantSummary[] Tenants);

[PublicAPI]
public sealed record TenantSummary(
    TenantId Id,
    string Name,
    string? LogoUrl,
    SubscriptionPlan Plan,
    decimal? MonthlyRecurringRevenue,
    decimal? ScheduledPriceAmount,
    string? Currency,
    DateTimeOffset? RenewalDate,
    PlannedSubscriptionChange? PlannedChange,
    bool HasEverSubscribed,
    string? Country,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    TenantOwnerSummary? Owner
);

[PublicAPI]
public sealed record TenantOwnerSummary(UserId UserId, string? FirstName, string? LastName, string Email);

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlannedSubscriptionChange
{
    Cancellation,
    ScheduledPlanChange
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TenantStatusFilter
{
    Active,
    Downgrading,
    Canceling,
    Canceled,
    Free
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortableTenantProperties
{
    Name,
    Plan,
    MonthlyRecurringRevenue,
    RenewalDate,
    Status,
    Country,
    CreatedAt,
    ModifiedAt
}
