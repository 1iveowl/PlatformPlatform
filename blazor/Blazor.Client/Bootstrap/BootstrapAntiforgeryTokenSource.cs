// The browser's antiforgery token is the one the account API bootstrap issued. SessionState stores the token of the
// bootstrap it accepts together with the identity and the feature flags, and clears it when the authenticated surface is
// left. A state-changing call before any accepted bootstrap waits for SessionState's read, which it shares with every
// other caller, so a lazy read can neither start a second initial bootstrap nor store a token SessionState discarded.
// SessionState is resolved lazily because it depends on the HttpClient whose handler asks for this token. The bootstrap
// itself is a GET, which never asks for a token, so that read cannot recurse.

using Account.Client;
using Blazor.Client.Session;

namespace Blazor.Client.Bootstrap;

public sealed class BootstrapAntiforgeryTokenSource(IServiceProvider serviceProvider) : IAntiforgeryTokenSource
{
    private string? _antiforgeryToken;
    private bool _hasBootstrap;

    public async ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (!_hasBootstrap) await serviceProvider.GetRequiredService<SessionState>().GetAsync().WaitAsync(cancellationToken);

        return _antiforgeryToken;
    }

    public void Store(string antiforgeryToken)
    {
        _antiforgeryToken = antiforgeryToken;
        _hasBootstrap = true;
    }

    public void Clear()
    {
        _antiforgeryToken = null;
        _hasBootstrap = false;
    }
}
