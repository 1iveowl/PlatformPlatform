using JetBrains.Annotations;

namespace Account.Features.FeatureFlags.Queries;

// The responses of the two configurable feature flag queries. Each flag is its registry key and whether the scope's
// override is active; the name and the description are not carried, because both editions read them from the registry.

[PublicAPI]
public sealed record TenantConfigurableFeatureFlagsResponse(TenantConfigurableFeatureFlag[] Flags);

[PublicAPI]
public sealed record TenantConfigurableFeatureFlag(string FlagKey, bool Enabled);

[PublicAPI]
public sealed record UserConfigurableFeatureFlagsResponse(UserConfigurableFeatureFlag[] Flags);

[PublicAPI]
public sealed record UserConfigurableFeatureFlag(string FlagKey, bool Enabled);
