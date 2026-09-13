// Spike code (Blazor edition, stage B3): hand-written mirrors of the account API's users contracts (GetUsersQuery,
// UsersResponse, UserDetails, UserRole, UserStatus, SortableUserProperties, SortOrder). Stage C replaces them with the
// shared contracts assembly; until then a backend rename is a runtime failure here, not a compile error.

using System.Text.Json.Serialization;

namespace Blazor.Client.Users;

[JsonConverter(typeof(JsonStringEnumConverter<UserRole>))]
public enum UserRole
{
    Member,
    Admin,
    Owner
}

[JsonConverter(typeof(JsonStringEnumConverter<UserStatus>))]
public enum UserStatus
{
    Active,
    Pending
}

[JsonConverter(typeof(JsonStringEnumConverter<SortableUserProperties>))]
public enum SortableUserProperties
{
    CreatedAt,
    LastSeenAt,
    Name,
    Email,
    Role
}

[JsonConverter(typeof(JsonStringEnumConverter<SortOrder>))]
public enum SortOrder
{
    Ascending,
    Descending
}

public sealed record UsersResponse(int TotalCount, int PageSize, int TotalPages, int CurrentPageOffset, UserDetails[] Users);

public sealed record UserDetails(
    string Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    DateTimeOffset? LastSeenAt,
    string Email,
    UserRole Role,
    string FirstName,
    string LastName,
    string Title,
    bool EmailConfirmed,
    string? AvatarUrl
)
{
    public string DisplayName => $"{FirstName} {LastName}".Trim();
}
