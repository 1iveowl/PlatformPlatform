using Account.Features.FeatureFlags.Queries;
using Account.Features.Subscriptions.Domain;
using Account.Features.Users.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;
using SharedKernel.Persistence;

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

// The body of PUT /api/back-office/feature-flags/{flagKey}/tenant-override: the account and whether the flag is on for it. The
// flag key is a route value.
[PublicAPI]
public sealed record SetTenantFeatureFlagInternalCommand
{
    public required TenantId TenantId { get; init; }

    public required bool Enabled { get; init; }
}

// The body of PUT /api/back-office/feature-flags/{flagKey}/user-override: the user, the account the user belongs to and whether
// the flag is on for that user. The flag key is a route value.
[PublicAPI]
public sealed record SetUserFeatureFlagInternalCommand
{
    public required UserId UserId { get; init; }

    public required TenantId TenantId { get; init; }

    public required bool Enabled { get; init; }
}

// The query string of GET /api/back-office/feature-flags/{flagKey}/tenants, which binds GetFeatureFlagTenantsQuery with
// [AsParameters]: PascalCase names, one repeated Plans parameter per plan, enum names, no State for every state, and the
// server's defaults of Name, ascending, the first page and 25 rows. The flag key is a route value.
[PublicAPI]
public sealed record GetFeatureFlagTenantsQuery(
    string? Search = null,
    SubscriptionPlan[]? Plans = null,
    FeatureFlagAudienceState? State = null,
    bool HasOverride = false,
    SortableFeatureFlagTenantProperties OrderBy = SortableFeatureFlagTenantProperties.Name,
    SortOrder SortOrder = SortOrder.Ascending,
    int PageOffset = 0,
    int PageSize = 25
);

// The query string of GET /api/back-office/feature-flags/{flagKey}/users, which binds GetFeatureFlagUsersQuery the same way,
// with one repeated Roles parameter per role
[PublicAPI]
public sealed record GetFeatureFlagUsersQuery(
    string? Search = null,
    UserRole[]? Roles = null,
    FeatureFlagAudienceState? State = null,
    bool HasOverride = false,
    SortableFeatureFlagUserProperties OrderBy = SortableFeatureFlagUserProperties.Name,
    SortOrder SortOrder = SortOrder.Ascending,
    int PageOffset = 0,
    int PageSize = 25
);
