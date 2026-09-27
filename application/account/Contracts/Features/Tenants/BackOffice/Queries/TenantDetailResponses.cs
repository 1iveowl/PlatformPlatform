using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.Domain;
using Account.Features.Users.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;

namespace Account.Features.Tenants.BackOffice.Queries;

// The back office's account detail as GET /api/back-office/tenants/{id}, /{id}/user-counts and /{id}/users return it: the
// account with its subscription, billing details and drift state, its user counts, and one page of its users. The query
// records stay with their handlers in the account API.

[PublicAPI]
public sealed record TenantDetailResponse(
    TenantId Id,
    string Name,
    SubscriptionPlan Plan,
    SubscriptionPlan? ScheduledPlan,
    decimal? ScheduledPriceAmount,
    bool CancelAtPeriodEnd,
    decimal? MonthlyRecurringRevenue,
    string? Currency,
    DateTimeOffset? RenewalDate,
    DateTimeOffset? SubscribedSince,
    bool HasEverSubscribed,
    string? BillingName,
    string? TaxId,
    BillingAddressResponse? BillingAddress,
    PaymentMethodResponse? PaymentMethod,
    decimal? LifetimeValue,
    TenantState State,
    SuspensionReason? SuspensionReason,
    DateTimeOffset? SuspendedAt,
    string? LogoUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    bool HasDriftDetected,
    DateTimeOffset? DriftCheckedAt,
    DriftDiscrepancy[] DriftDiscrepancies,
    string? StripeCustomerUrl,
    AbInclusionPin? AbInclusionPin
);

[PublicAPI]
public sealed record BillingAddressResponse(
    string? Line1,
    string? Line2,
    string? PostalCode,
    string? City,
    string? State,
    string? Country
);

[PublicAPI]
public sealed record PaymentMethodResponse(string Brand, string Last4, int ExpMonth, int ExpYear);

[PublicAPI]
public sealed record TenantUserCountsResponse(int TotalUsers, int ActiveUsers, int PendingUsers);

[PublicAPI]
public sealed record TenantUsersResponse(int TotalCount, int PageSize, int TotalPages, int CurrentPageOffset, TenantUserSummary[] Users);

[PublicAPI]
public sealed record TenantUserSummary(
    UserId Id,
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
