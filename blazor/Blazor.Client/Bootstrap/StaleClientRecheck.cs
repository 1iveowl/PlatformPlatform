// What the write gate does before it forwards a mutation: read both staleness signals again, because a tab that has been
// open across a deployment and then writes without navigating first has learned from neither. The asset probe otherwise
// runs only on an in-app navigation, and the server version is read only when the session commits a bootstrap, so a
// deployment of the account API alone would never reach an open tab at all.
//
// A re-check that cannot complete never blocks the write: an unreachable server, a document that is gone or a version
// neither side states is not evidence that this client is stale, and refusing every write on it would take the
// application down on an outage. One extra request, or two, is the price of a write; writes are rare.
//
// The bootstrap is read through the bootstrap source rather than through SessionState, whose refresh also republishes the
// identity and the feature flags to every surface that shows them; this re-check changes nothing a surface can see except
// the one decision it exists for. The read is a GET, so it passes the gate it is called from and cannot recurse.

using Account.Features.Authentication.Queries;

namespace Blazor.Client.Bootstrap;

public sealed class StaleClientRecheck(IServiceProvider serviceProvider, ClientVersionState versionState)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await CheckAssetSetAsync();
        if (versionState.IsStale) return;

        await ReadServerVersionAsync(cancellationToken);
    }

    // The probe lives in the WebAssembly application, because it needs a document to read its asset routes from; a runtime
    // registered without one is left with the version window as its only signal
    private async Task CheckAssetSetAsync()
    {
        var probe = serviceProvider.GetService<IStaleAssetProbe>();
        if (probe is null) return;

        try
        {
            await probe.CheckAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The probe could not ask, so nothing was learned about the publish
        }
    }

    private async Task ReadServerVersionAsync(CancellationToken cancellationToken)
    {
        BootstrapResponse bootstrap;
        try
        {
            bootstrap = await serviceProvider.GetRequiredService<IBootstrapSource>().GetAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The bootstrap could not be read, so the server's version is unchanged as far as this runtime knows
            return;
        }

        // A response that states no version, which is what a rejected session and an unauthenticated answer carry, would
        // otherwise turn a client already known to be unsupported back into an unknown one
        if (ClientVersionWindow.ReadServerVersion(bootstrap) is null) return;

        versionState.Apply(bootstrap);
    }
}
