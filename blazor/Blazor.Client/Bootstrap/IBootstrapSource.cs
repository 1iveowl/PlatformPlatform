// The bootstrap contract as a component reads it in either render mode: the host's server-side adapter during
// prerendering and the account API's bootstrap endpoint in the browser. Both produce the account API contract.

using Account.Features.Authentication.Queries;

namespace Blazor.Client.Bootstrap;

public interface IBootstrapSource
{
    Task<BootstrapResponse> GetAsync(CancellationToken cancellationToken = default);
}
