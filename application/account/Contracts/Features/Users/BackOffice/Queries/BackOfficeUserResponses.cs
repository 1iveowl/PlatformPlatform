using Account.Features.Authentication.Domain;
using Account.Features.ExternalAuthentication.Domain;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Users.Domain;
using JetBrains.Annotations;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;

namespace Account.Features.Users.BackOffice.Queries;

// The back office's users as GET /api/back-office/users, /{id}, /{id}/sessions and /{id}/login-history return them: one page of
// the users across every account, one user with the accounts it is a member of, that person's sessions across those accounts,
// and the sign-in attempts of the last 30 days. The query records stay with their handlers in the account API.

[PublicAPI]
public sealed record BackOfficeUsersResponse(int TotalCount, int PageSize, int TotalPages, int CurrentPageOffset, BackOfficeUserSummary[] Users);

[PublicAPI]
public sealed record BackOfficeUserSummary(
    UserId Id,
    TenantId TenantId,
    string TenantName,
    SubscriptionPlan TenantPlan,
    PlannedSubscriptionChange? TenantPlannedChange,
    bool TenantHasEverSubscribed,
    string Email,
    string? FirstName,
    string? LastName,
    string? Title,
    UserRole Role,
    bool EmailConfirmed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    string? AvatarUrl
);

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserActivityFilter
{
    ActiveLast24Hours,
    ActiveLast7Days,
    ActiveLast30Days,
    InactiveOver30Days
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortableBackOfficeUserProperties
{
    Name,
    Email,
    Role,
    LastSeenAt,
    CreatedAt
}

[PublicAPI]
public sealed record BackOfficeUserDetailResponse(
    UserId Id,
    TenantId TenantId,
    string TenantName,
    string Email,
    string? FirstName,
    string? LastName,
    string? Title,
    UserRole Role,
    bool EmailConfirmed,
    string Locale,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    DateTimeOffset? LastSeenAt,
    string? AvatarUrl,
    BackOfficeUserTenantMembership[] TenantMemberships,
    AbInclusionPin? AbInclusionPin
);

// A "tenant membership" is another user record sharing the same email in a different tenant. Each row in the back-office
// User detail Tenants section corresponds to a single user-record-per-tenant; we expose its UserId so the frontend can
// link the row to that other user's detail page when needed. We also surface the tenant logo, plan, currency, MRR and
// country to render a rich tenant card without requiring a per-membership tenant detail fetch from the SPA.
[PublicAPI]
public sealed record BackOfficeUserTenantMembership(
    UserId UserId,
    TenantId TenantId,
    string TenantName,
    string? TenantLogoUrl,
    SubscriptionPlan Plan,
    PlannedSubscriptionChange? PlannedChange,
    bool HasEverSubscribed,
    decimal? MonthlyRecurringRevenue,
    decimal? ScheduledPriceAmount,
    string? Currency,
    DateTimeOffset? RenewalDate,
    string? Country,
    UserRole Role,
    bool EmailConfirmed,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt
);

[PublicAPI]
public sealed record BackOfficeUserSessionsResponse(int TotalCount, int PageSize, int TotalPages, int CurrentPageOffset, BackOfficeUserSession[] Sessions);

[PublicAPI]
public sealed record BackOfficeUserSession(
    SessionId Id,
    TenantId TenantId,
    string TenantName,
    string? TenantLogoUrl,
    LoginMethod LoginMethod,
    DeviceType DeviceType,
    string UserAgent,
    string IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActiveAt,
    DateTimeOffset? RevokedAt,
    SessionRevokedReason? RevokedReason,
    DateTimeOffset ExpiresAt
);

[PublicAPI]
public sealed record BackOfficeUserLoginHistoryResponse(BackOfficeUserLoginEntry[] Entries);

[PublicAPI]
public sealed record BackOfficeUserLoginEntry(
    LoginEventKind Kind,
    LoginMethod Method,
    LoginEventOutcome Outcome,
    DateTimeOffset OccurredAt,
    string? FailureReason,
    ExternalProviderType? ExternalProvider
);

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LoginEventKind
{
    Email,
    External
}

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LoginEventOutcome
{
    Pending,
    Succeeded,
    Failed
}
