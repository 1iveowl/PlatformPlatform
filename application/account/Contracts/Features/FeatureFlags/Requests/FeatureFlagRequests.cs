using JetBrains.Annotations;

namespace Account.Features.FeatureFlags.Requests;

// Request bodies of the two override endpoints and of the back office's rollout percentage. Each record keeps the name and
// JSON shape of the command it is mapped to; the flag key is a route value the server binds separately, so it is not part of
// the body.

[PublicAPI]
public sealed record SetTenantFeatureFlagOwnerCommand
{
    public required bool Enabled { get; init; }
}

[PublicAPI]
public sealed record SetUserFeatureFlagCommand
{
    public required bool Enabled { get; init; }
}

// The body of PUT /api/back-office/feature-flags/{flagKey}/rollout-percentage: a whole number from 0 to 100. The flag key is a
// route value, and the server's validator refuses a flag that is not an A/B test and a percentage outside that range.
[PublicAPI]
public sealed record SetFeatureFlagRolloutPercentageCommand
{
    public required int RolloutPercentage { get; init; }
}
