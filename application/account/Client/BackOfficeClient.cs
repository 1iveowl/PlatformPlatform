using Account.Features.BackOffice.Queries;
using Account.Features.BackOffice.Requests;
using SharedKernel.Domain;

namespace Account.Client;

// The back-office API on the back-office host. The registrant gives it its own HttpClient: the back-office identity comes from
// the platform authentication in front of that host, not from the app's session, so the app's handler chain (bootstrap
// antiforgery token, feature flags, version gate, app login on 401) does not apply to it.
public sealed class BackOfficeClient(HttpClient httpClient)
{
    private readonly AccountApiTransport _transport = new(httpClient);

    public Task<ApiCallResult<MeResponse>> GetMeAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<MeResponse>(AccountApiRoutes.BackOfficeMe, cancellationToken);
    }

    public Task<ApiCallResult> SetTenantAbInclusionPinAsync(TenantId tenantId, SetTenantAbInclusionPinCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Put, AccountApiRoutes.SetTenantAbInclusionPin(tenantId), command, cancellationToken);
    }
}
