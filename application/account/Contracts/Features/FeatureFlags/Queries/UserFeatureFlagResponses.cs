using Account.Features.FeatureFlags.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;

namespace Account.Features.FeatureFlags.Queries;

// The user-scoped feature flags of one user as GET /api/back-office/users/{id}/feature-flags returns them: each flag's registry
// key, scope and A/B test settings, and whether it is on for the user and why. The query record stays with its handler in the
// account API.

[PublicAPI]
public sealed record GetUserFeatureFlagsResponse(UserFeatureFlagInfo[] Flags);

[PublicAPI]
public sealed record UserFeatureFlagInfo(
    string FlagKey,
    FeatureFlagScope Scope,
    string Description,
    bool IsAbTestEligible,
    int? BucketStart,
    int? BucketEnd,
    int? RolloutPercentage,
    bool IsEnabled,
    FeatureFlagSource Source,
    bool IsBaseRowActive,
    int RolloutBucket,
    TenantId TenantId,
    int? InclusionThresholdPercentage,
    bool DefaultEnabled,
    AbInclusionPin? UserAbInclusionPin
);
