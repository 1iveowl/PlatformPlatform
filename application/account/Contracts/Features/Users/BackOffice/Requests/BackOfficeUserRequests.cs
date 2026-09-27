using Account.Features.Users.BackOffice.Queries;
using Account.Features.Users.Domain;
using JetBrains.Annotations;
using SharedKernel.FeatureFlags;
using SharedKernel.Persistence;

namespace Account.Features.Users.BackOffice.Requests;

// Requests of the back office's user endpoints. GetBackOfficeUsersQuery keeps the name and the query string parameters of the
// query GET /api/back-office/users binds with [AsParameters]: PascalCase property names, enum names, and one repeated Roles
// parameter per role. The server's defaults are LastSeenAt, descending, the first page and 25 rows.
[PublicAPI]
public sealed record GetBackOfficeUsersQuery(
    string? Search = null,
    UserRole[]? Roles = null,
    UserActivityFilter? Activity = null,
    SortableBackOfficeUserProperties OrderBy = SortableBackOfficeUserProperties.LastSeenAt,
    SortOrder SortOrder = SortOrder.Descending,
    int PageOffset = 0,
    int PageSize = 25
);

// The query string of GET /api/back-office/users/{id}/sessions: the first page and 25 rows by default. The user id is the route
// value, not a parameter.
[PublicAPI]
public sealed record GetBackOfficeUserSessionsQuery(int PageOffset = 0, int PageSize = 25);

// The body of PUT /api/back-office/users/{id}/ab-inclusion-pin, which the account API maps to its command of the same name. The
// user id travels in the route; a null pin clears it.
[PublicAPI]
public sealed record SetUserAbInclusionPinCommand(AbInclusionPin? AbInclusionPin);
