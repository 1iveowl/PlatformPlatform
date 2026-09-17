using JetBrains.Annotations;

namespace Account.Features.FeatureFlags.Requests;

// Request bodies of the two override endpoints. Each record keeps the name and JSON shape of the command it is mapped to;
// the flag key is a route value the server binds separately, so it is not part of the body.

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
