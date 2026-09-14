// The WebAssembly client reads identity, runtime configuration and system-scope feature flags from the account API's
// bootstrap endpoint through the gateway, never from values injected into the host page. Each read also sets the feature
// flag state and the antiforgery token of this user scope.

using Account.Client;
using Account.Features.Authentication.Queries;

namespace Blazor.Client.Bootstrap;

public sealed class HttpBootstrapSource(AuthenticationClient authenticationClient, FeatureFlagState featureFlagState, BootstrapAntiforgeryTokenSource antiforgeryTokenSource)
    : IBootstrapSource
{
    private static readonly BootstrapResponse Unauthenticated = new(false, null, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool>(), string.Empty);

    public async Task<BootstrapResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var result = await authenticationClient.GetBootstrapAsync(cancellationToken);
        if (result.IsSuccess)
        {
            antiforgeryTokenSource.Store(result.Value.AntiforgeryToken);
            featureFlagState.Initialize(result.Value);
            return result.Value;
        }

        if (result.Outcome != ApiCallOutcome.Unauthorized)
        {
            throw new InvalidOperationException($"The bootstrap endpoint call ended with {result.Outcome} (status {result.Problem.StatusCode?.ToString() ?? "none"}).");
        }

        // A rejected session is already being handled by UnauthorizedResponseHandler, which leaves the runtime with a full
        // document navigation; the caller only needs to stop treating the user as signed in
        antiforgeryTokenSource.Clear();
        featureFlagState.Reset();
        return Unauthenticated;
    }
}
