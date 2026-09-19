// The WebAssembly client reads identity, runtime configuration and system-scope feature flags from the account API's
// bootstrap endpoint through the gateway, never from values injected into the host page. A read only returns the
// response; the antiforgery token, the feature flag state and the version window of this user scope change when
// SessionState applies the bootstrap it accepted.

using Account.Client;
using Account.Features.Authentication.Queries;

namespace Blazor.Client.Bootstrap;

public sealed class HttpBootstrapSource(
    AuthenticationClient authenticationClient,
    FeatureFlagState featureFlagState,
    BootstrapAntiforgeryTokenSource antiforgeryTokenSource,
    ClientVersionState versionState
) : IBootstrapSource
{
    private static readonly BootstrapResponse Unauthenticated = new(false, null, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool>(), string.Empty);

    public async Task<BootstrapResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var result = await authenticationClient.GetBootstrapAsync(cancellationToken);
        if (result.IsSuccess) return result.Value;

        // A server outage or a network failure is never reported as a signed-out user
        if (result.Outcome != ApiCallOutcome.Unauthorized)
        {
            throw new InvalidOperationException($"The bootstrap endpoint call ended with {result.Outcome} (status {result.Problem.StatusCode?.ToString() ?? "none"}).");
        }

        // A rejected session is already being handled by UnauthorizedResponseHandler, which leaves the runtime with a full
        // document navigation and clears the user scope's state; the caller only needs to stop treating the user as signed in
        return Unauthenticated;
    }

    public Action Apply(BootstrapResponse? bootstrap)
    {
        // The supported version window is read from the same accepted bootstrap as the identity, so the server version this
        // client compares itself with always comes from the authenticated channel
        versionState.Apply(bootstrap);

        if (bootstrap is null)
        {
            antiforgeryTokenSource.Clear();
        }
        else
        {
            antiforgeryTokenSource.Store(bootstrap.AntiforgeryToken);
        }

        return featureFlagState.Replace(bootstrap);
    }
}
