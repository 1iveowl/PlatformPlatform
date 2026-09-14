using Account.Features.Authentication.Queries;
using Account.Features.Authentication.Requests;

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
}
