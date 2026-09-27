using Account.Features.FeatureFlags.Domain;
using JetBrains.Annotations;
using SharedKernel.FeatureFlags;

namespace Account.Features.FeatureFlags.Queries;

// The tenant-scoped feature flags of one account as GET /api/back-office/tenants/{id}/feature-flags returns them: each flag's
// registry key, scope, plan and A/B test settings, and whether it is on for the account and why. The query record stays with
// its handler in the account API.

[PublicAPI]
public sealed record GetTenantFeatureFlagsResponse(TenantFeatureFlagInfo[] Flags);

[PublicAPI]
public sealed record TenantFeatureFlagInfo(
    string FlagKey,
    FeatureFlagScope Scope,
    string Description,
    string? RequiredPlan,
    bool IsAbTestEligible,
    int? BucketStart,
    int? BucketEnd,
    int? RolloutPercentage,
    bool IsEnabled,
    FeatureFlagSource Source,
    bool IsBaseRowActive,
    int RolloutBucket,
    int? InclusionThresholdPercentage,
    bool DefaultEnabled,
    AbInclusionPin? TenantAbInclusionPin
);
