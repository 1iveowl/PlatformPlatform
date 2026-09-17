using Account.Features.Users.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;
using SharedKernel.Persistence;

namespace Account.Features.Users.Requests;

// Requests of the users endpoints. Each record keeps the name and JSON shape of the command or query it is mapped to,
// because the name is the OpenAPI schema name and the query properties are the query string parameters. The user id of
// ChangeUserRoleCommand travels in the route, not in the body, and BulkDeleteUsersCommand carries the ids of the users to
// delete in the body.

[PublicAPI]
public sealed record GetUsersQuery(
    string? Search = null,
    UserRole? UserRole = null,
    UserStatus? UserStatus = null,
    DateTimeOffset? StartDate = null,
    DateTimeOffset? EndDate = null,
    SortableUserProperties OrderBy = SortableUserProperties.Name,
    SortOrder SortOrder = SortOrder.Ascending,
    int? PageOffset = null,
    int PageSize = 25
);

[PublicAPI]
public sealed record ChangeUserRoleCommand
{
    public required UserRole UserRole { get; init; }
}

[PublicAPI]
public sealed record UpdateCurrentUserCommand(string FirstName, string LastName, string Title);

[PublicAPI]
public sealed record BulkDeleteUsersCommand(UserId[] UserIds);

[PublicAPI]
public sealed record InviteUserCommand(string Email);

// The theme is a device preference the browser stores; the account API only records the change as telemetry. Theme and
// FromTheme are the selected modes (system, light or dark), ResolvedTheme the light or dark theme the browser applied.
[PublicAPI]
public sealed record ChangeThemeCommand(string FromTheme, string Theme, string? ResolvedTheme);
