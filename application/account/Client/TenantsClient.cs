using Account.Features.Tenants.Queries;
using Account.Features.Tenants.Requests;

namespace Account.Client;

public sealed class TenantsClient(HttpClient httpClient)
{
    private readonly AccountApiTransport _transport = new(httpClient);

    public Task<ApiCallResult<GetTenantsForUserResponse>> GetTenantsAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<GetTenantsForUserResponse>(AccountApiRoutes.Tenants, cancellationToken);
    }

    public Task<ApiCallResult<TenantResponse>> GetCurrentTenantAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<TenantResponse>(AccountApiRoutes.CurrentTenant, cancellationToken);
    }

    public Task<ApiCallResult> UpdateCurrentTenantAsync(UpdateCurrentTenantCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Put, AccountApiRoutes.CurrentTenant, command, cancellationToken);
    }
}
