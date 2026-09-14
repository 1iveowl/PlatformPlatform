// The WebAssembly client reads identity, runtime configuration and system-scope feature flags from the account API's
// bootstrap endpoint through the gateway, never from values injected into the host page.

using System.Net;
using Account.Features.Authentication.Queries;

namespace Blazor.Client.Bootstrap;

public sealed class HttpBootstrapSource(HttpClient httpClient) : IBootstrapSource
{
    public const string BootstrapPath = "/api/account/bootstrap";

    private static readonly BootstrapResponse Unauthenticated = new(false, null, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool>(), string.Empty);

    public async Task<BootstrapResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(BootstrapPath, cancellationToken);

        // A rejected session is already being handled by UnauthorizedResponseHandler, which leaves the runtime with a full
        // document navigation; the caller only needs to stop treating the user as signed in
        if (response.StatusCode == HttpStatusCode.Unauthorized) return Unauthenticated;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BootstrapResponse>(cancellationToken)
               ?? throw new InvalidOperationException("The bootstrap endpoint returned no content.");
    }
}
