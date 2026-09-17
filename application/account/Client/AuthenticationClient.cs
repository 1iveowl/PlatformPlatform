using Account.Features.Authentication.Queries;
using Account.Features.Authentication.Requests;
using SharedKernel.Authentication.TokenGeneration;

namespace Account.Client;

public sealed class AuthenticationClient(HttpClient httpClient)
{
    private readonly AccountApiTransport _transport = new(httpClient);

    public Task<ApiCallResult<BootstrapResponse>> GetBootstrapAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<BootstrapResponse>(AccountApiRoutes.Bootstrap, cancellationToken);
    }

    public Task<ApiCallResult> LogoutAsync(CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Post, AccountApiRoutes.Logout, cancellationToken);
    }

    public Task<ApiCallResult> SwitchTenantAsync(SwitchTenantCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Post, AccountApiRoutes.SwitchTenant, command, cancellationToken);
    }

    public Task<ApiCallResult<UserSessionsResponse>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<UserSessionsResponse>(AccountApiRoutes.Sessions, cancellationToken);
    }

    // Own sessions only; the account API refuses another user's session, an unknown one and one already revoked
    public Task<ApiCallResult> RevokeSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Delete, AccountApiRoutes.RevokeSession(sessionId), cancellationToken);
    }
}
