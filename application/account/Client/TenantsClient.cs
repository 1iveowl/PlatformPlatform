using Account.Features.Tenants.Queries;
using Account.Features.Tenants.Requests;

namespace Account.Client;

public sealed class TenantsClient(HttpClient httpClient)
{
    private const string LogoFormFieldName = "file";
    private const string LogoFileName = "logo";

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

    public Task<ApiCallResult> UpdateLogoAsync(UpdateTenantLogoCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendFileAsync(HttpMethod.Post, AccountApiRoutes.UpdateTenantLogo, LogoFormFieldName, command.FileStream, command.ContentType, LogoFileName, cancellationToken);
    }

    public Task<ApiCallResult> RemoveLogoAsync(CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Delete, AccountApiRoutes.RemoveTenantLogo, cancellationToken);
    }
}
