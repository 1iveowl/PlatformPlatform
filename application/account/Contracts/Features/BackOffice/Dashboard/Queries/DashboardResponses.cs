using Account.Features.Authentication.Domain;
using Account.Features.Subscriptions.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;

namespace Account.Features.BackOffice.Dashboard.Queries;

// The back-office dashboard's period, its trend metric and the responses of its KPI, recent activity and chart endpoints,
// as /api/back-office/dashboard returns them. The query records stay with their handlers in the account API.

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DashboardTrendPeriod
{
    Last7Days,
    Last30Days,
    Last90Days
}

[PublicAPI]
public sealed record BackOfficeDashboardKpisResponse(
    DashboardTrendPeriod Period,
    long TotalTenants,
    long ActiveTenants,
    long TrialTenants,
    long CanceledTenants,
    long NewTenantsInPeriod,
    long? NewTenantsDeltaPercent,
    long TotalUsers,
    long ActiveUsersInPeriod,
    decimal BlendedMonthlyRecurringRevenue,
    decimal? BlendedMonthlyRecurringRevenueDeltaPercent,
    decimal TotalRevenue,
    string? Currency,
    long ActiveSessionsLast24Hours
);

[PublicAPI]
public sealed record BackOfficeDashboardRecentSignupsResponse(BackOfficeDashboardRecentSignup[] Signups);

[PublicAPI]
public sealed record BackOfficeDashboardRecentSignup(
    TenantId TenantId,
    string Name,
    string? TenantLogoUrl,
    DateTimeOffset CreatedAt,
    BackOfficeDashboardRecentSignupOwner? Owner
);

[PublicAPI]
public sealed record BackOfficeDashboardRecentSignupOwner(UserId UserId, string? FirstName, string? LastName, string Email);

[PublicAPI]
public sealed record BackOfficeDashboardRecentLoginsResponse(BackOfficeDashboardLogin[] Logins);

[PublicAPI]
public sealed record BackOfficeDashboardLogin(
    UserId? UserId,
    string Email,
    string? FirstName,
    string? LastName,
    string? AvatarUrl,
    TenantId? TenantId,
    string? TenantName,
    string? TenantLogoUrl,
    LoginMethod Method,
    DateTimeOffset OccurredAt
);

[PublicAPI]
public sealed record BackOfficeDashboardRecentPaymentsResponse(BackOfficeDashboardPayment[] Payments);

[PublicAPI]
public sealed record BackOfficeDashboardPayment(
    PaymentTransactionId Id,
    TenantId TenantId,
    string TenantName,
    string? TenantLogoUrl,
    DateTimeOffset Date,
    SubscriptionPlan? Plan,
    decimal Amount,
    string Currency,
    PaymentTransactionStatus Status
);

[PublicAPI]
public sealed record BackOfficeDashboardRecentStripeEventsResponse(BackOfficeDashboardStripeEvent[] Events);

[PublicAPI]
public sealed record BackOfficeDashboardStripeEvent(
    BillingEventId Id,
    TenantId TenantId,
    string TenantName,
    string? TenantLogoUrl,
    BillingEventType Type,
    SubscriptionPlan? FromPlan,
    SubscriptionPlan? ToPlan,
    decimal? AmountDelta,
    string? Currency,
    DateTimeOffset OccurredAt
);

[PublicAPI]
public sealed record BackOfficeDashboardMrrTrendResponse(
    DashboardTrendPeriod Period,
    string? Currency,
    BackOfficeDashboardMrrTrendPoint[] Points,
    BackOfficeDashboardMrrTrendPoint[] PriorPoints
);

[PublicAPI]
public sealed record BackOfficeDashboardMrrTrendPoint(DateOnly Date, decimal MonthlyRecurringRevenue);

[PublicAPI]
public sealed record BackOfficeDashboardRevenueTrendResponse(
    DashboardTrendPeriod Period,
    string? Currency,
    BackOfficeDashboardRevenueTrendPoint[] Points,
    BackOfficeDashboardRevenueTrendPoint[] PriorPoints
);

[PublicAPI]
public sealed record BackOfficeDashboardRevenueTrendPoint(DateOnly Date, decimal Revenue);

[PublicAPI]
public sealed record BackOfficeDashboardPlanDistributionResponse(
    long TotalTenants,
    BackOfficeDashboardPlanDistributionEntry[] Distribution
);

[PublicAPI]
public sealed record BackOfficeDashboardPlanDistributionEntry(SubscriptionPlan Plan, long Count, double Percentage);

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DashboardTrendMetric
{
    NewTenants,
    NewUsers,
    LoginActivity
}

[PublicAPI]
public sealed record BackOfficeDashboardTrendsResponse(
    DashboardTrendMetric Metric,
    DashboardTrendPeriod Period,
    BackOfficeDashboardTrendPoint[] Points,
    BackOfficeDashboardTrendPoint[] PriorPoints
);

[PublicAPI]
public sealed record BackOfficeDashboardTrendPoint(DateOnly Date, long Value);
