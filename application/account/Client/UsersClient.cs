using Account.Features.Users.Queries;
using Account.Features.Users.Requests;
using SharedKernel.Domain;

namespace Account.Client;

public sealed class UsersClient(HttpClient httpClient)
{
    private readonly AccountApiTransport _transport = new(httpClient);

    public Task<ApiCallResult<UsersResponse>> GetUsersAsync(GetUsersQuery query, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<UsersResponse>(AccountApiRoutes.GetUsers(query), cancellationToken);
    }

    public Task<ApiCallResult<UserDetails>> GetUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<UserDetails>(AccountApiRoutes.User(userId), cancellationToken);
    }

    public Task<ApiCallResult<CurrentUserResponse>> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<CurrentUserResponse>(AccountApiRoutes.CurrentUser, cancellationToken);
    }

    public Task<ApiCallResult> UpdateCurrentUserAsync(UpdateCurrentUserCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Put, AccountApiRoutes.CurrentUser, command, cancellationToken);
    }

    public Task<ApiCallResult> ChangeUserRoleAsync(UserId userId, ChangeUserRoleCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Put, AccountApiRoutes.ChangeUserRole(userId), command, cancellationToken);
    }

    public Task<ApiCallResult> DeleteUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Delete, AccountApiRoutes.User(userId), cancellationToken);
    }

    public Task<ApiCallResult> BulkDeleteUsersAsync(BulkDeleteUsersCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Post, AccountApiRoutes.BulkDeleteUsers, command, cancellationToken);
    }

    public Task<ApiCallResult> InviteUserAsync(InviteUserCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Post, AccountApiRoutes.InviteUser, command, cancellationToken);
    }

    public Task<ApiCallResult> ChangeThemeAsync(ChangeThemeCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Put, AccountApiRoutes.ChangeTheme, command, cancellationToken);
    }

    public Task<ApiCallResult<DeletedUsersResponse>> GetDeletedUsersAsync(GetDeletedUsersQuery query, CancellationToken cancellationToken)
    {
        return _transport.GetAsync<DeletedUsersResponse>(AccountApiRoutes.GetDeletedUsers(query), cancellationToken);
    }

    public Task<ApiCallResult> RestoreUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Post, AccountApiRoutes.RestoreUser(userId), cancellationToken);
    }

    public Task<ApiCallResult> PurgeUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Delete, AccountApiRoutes.PurgeUser(userId), cancellationToken);
    }

    public Task<ApiCallResult> BulkPurgeUsersAsync(BulkPurgeUsersCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Post, AccountApiRoutes.BulkPurgeUsers, command, cancellationToken);
    }

    // Returns the number of users the server purged
    public Task<ApiCallResult<int>> EmptyRecycleBinAsync(CancellationToken cancellationToken)
    {
        return _transport.SendAsync<int>(HttpMethod.Post, AccountApiRoutes.EmptyRecycleBin, cancellationToken);
    }
}
