// The bootstrap contract as a component reads it in either render mode: the host's server-side adapter during
// prerendering and the account API's bootstrap endpoint in the browser. Both produce the account API contract.
//
// A read changes no client state. SessionState decides which read it accepts and then calls Apply, so the identity it
// holds, the antiforgery token and the feature flags of the user scope always come from the same bootstrap.

using Account.Features.Authentication.Queries;

namespace Blazor.Client.Bootstrap;

public interface IBootstrapSource
{
    Task<BootstrapResponse> GetAsync(CancellationToken cancellationToken = default);

    // Stores the accepted bootstrap's transport state for the user scope, or clears it when null, without notifying
    // anyone; the returned notification is raised by the caller after its own state is committed
    Action Apply(BootstrapResponse? bootstrap);
}
