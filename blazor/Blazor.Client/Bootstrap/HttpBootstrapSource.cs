// Spike code (Blazor edition, stage B2): the WebAssembly client obtains identity, runtime configuration and system-scope
// feature flags from the bootstrap endpoint instead of from values injected into the host page.

namespace Blazor.Client.Bootstrap;

public sealed class HttpBootstrapSource(HttpClient httpClient) : IBootstrapSource
{
    public async Task<BootstrapResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<BootstrapResponse>("api/bootstrap", cancellationToken)
               ?? throw new InvalidOperationException("The bootstrap endpoint returned no content.");
    }
}
