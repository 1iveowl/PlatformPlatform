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

    public Task<ApiCallResult> ChangeThemeAsync(ChangeThemeCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Put, AccountApiRoutes.ChangeTheme, command, cancellationToken);
    }
}
