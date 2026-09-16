using Account.Features.Users.Domain;
using JetBrains.Annotations;
using SharedKernel.Domain;

namespace Account.Features.Users.Queries;

[PublicAPI]
public sealed record UsersResponse(int TotalCount, int PageSize, int TotalPages, int CurrentPageOffset, UserDetails[] Users);

[PublicAPI]
public sealed record UserDetails(
    UserId Id,
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
);

[PublicAPI]
public sealed record CurrentUserResponse(
    UserId Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    string Email,
    UserRole Role,
    string FirstName,
    string LastName,
    string Title,
    string? AvatarUrl
);
