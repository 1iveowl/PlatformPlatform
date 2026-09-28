using Account.Features.FeatureFlags.Domain;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Users.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;

namespace Account.Features.FeatureFlags.Queries;

// Every feature flag as GET /api/back-office/feature-flags returns it: the registry's flags with their base row's activation
// and rollout, followed by the orphaned rows whose key the registry no longer declares, and the soft-deleted ones when
// IncludeDeleted is set, and one flag's tenants and users as GET /api/back-office/feature-flags/{flagKey}/tenants and /users
// return them, with each row's evaluated state, its source and its override dates, and the sort values those queries bind.
// The query records stay with their handlers in the account API.

[PublicAPI]
public sealed record GetFeatureFlagsResponse(FeatureFlagInfo[] Flags);

[PublicAPI]
public sealed record FeatureFlagInfo(
    string Key,
    FeatureFlagScope Scope,
    FeatureFlagAdminLevel AdminLevel,
    string Description,
    bool IsAbTestEligible,
    bool ConfigurableByTenant,
    bool ConfigurableByUser,
    string? RequiredPlan,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? EnabledAt,
    DateTimeOffset? DisabledAt,
    int? RolloutBucketStart,
    int? RolloutBucketEnd,
    int? RolloutPercentage,
    bool IsActive,
    bool IsKillSwitchEnabled,
    bool IsStableModule,
    DateTimeOffset? OrphanedAt,
    DateTimeOffset? DeletedAt
);

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortableFeatureFlagTenantProperties
{
    Name,
    Plan,
    MonthlyRecurringRevenue,
    RenewalDate,
    IsEnabled,
    OverrideUpdatedAt,
    InclusionThresholdPercentage
}

[PublicAPI]
public sealed record GetFeatureFlagTenantsResponse(
    int TotalCount,
    int PageSize,
    int TotalPages,
    int CurrentPageOffset,
    int EnabledCount,
    int DisabledCount,
    int OverrideCount,
    FeatureFlagTenantInfo[] Tenants
);

// Field names mirror TenantSummary so Mapster's convention-based mapping covers the shared subset. Override fields
// (RolloutBucket, IsEnabled, Source) come from the feature-flag evaluation and are applied via `with` on top of Adapt.
[PublicAPI]
public sealed record FeatureFlagTenantInfo(
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
    TenantOwnerSummary? Owner,
    int RolloutBucket,
    bool IsEnabled,
    FeatureFlagSource Source,
    int? InclusionThresholdPercentage,
    bool DefaultEnabled,
    DateTimeOffset? OverrideEnabledAt,
    DateTimeOffset? OverrideDisabledAt,
    AbInclusionPin? TenantAbInclusionPin
);

[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortableFeatureFlagUserProperties
{
    Name,
    TenantName,
    Role,
    LastSeenAt,
    IsEnabled,
    OverrideUpdatedAt,
    InclusionThresholdPercentage
}

[PublicAPI]
public sealed record GetFeatureFlagUsersResponse(
    int TotalCount,
    int PageSize,
    int TotalPages,
    int CurrentPageOffset,
    int EnabledCount,
    int DisabledCount,
    int OverrideCount,
    FeatureFlagUserInfo[] Users
);

// Field names mirror the User aggregate so Mapster's convention-based mapping covers the user subset. Tenant-derived
// fields (TenantName, TenantPlan) and override fields (RolloutBucket, IsEnabled, Source) are applied via `with`.
[PublicAPI]
public sealed record FeatureFlagUserInfo(
    UserId Id,
    TenantId TenantId,
    string Email,
    string? FirstName,
    string? LastName,
    string? AvatarUrl,
    UserRole Role,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset CreatedAt,
    string TenantName,
    SubscriptionPlan TenantPlan,
    int RolloutBucket,
    bool IsEnabled,
    FeatureFlagSource Source,
    int? InclusionThresholdPercentage,
    bool DefaultEnabled,
    DateTimeOffset? OverrideEnabledAt,
    DateTimeOffset? OverrideDisabledAt,
    AbInclusionPin? UserAbInclusionPin
);
