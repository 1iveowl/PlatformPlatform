using JetBrains.Annotations;
using SharedKernel.FeatureFlags;

namespace Account.Features.FeatureFlags.Queries;

// Every feature flag as GET /api/back-office/feature-flags returns it: the registry's flags with their base row's activation
// and rollout, followed by the orphaned rows whose key the registry no longer declares, and the soft-deleted ones when
// IncludeDeleted is set. The query record stays with its handler in the account API.

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
