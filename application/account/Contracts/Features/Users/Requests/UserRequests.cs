using Account.Features.Users.Domain;
using JetBrains.Annotations;
using SharedKernel.Persistence;

namespace Account.Features.Users.Requests;

// Requests of the users endpoints. Each record keeps the name and JSON shape of the command or query it is mapped to,
// because the name is the OpenAPI schema name and the query properties are the query string parameters. The user id of
// ChangeUserRoleCommand travels in the route, not in the body.

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
