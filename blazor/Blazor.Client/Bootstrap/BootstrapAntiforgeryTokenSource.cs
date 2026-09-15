// The browser's antiforgery token is the one the account API bootstrap issued. HttpBootstrapSource stores it after every
// read, so a state-changing call sends the token of the latest bootstrap. A state-changing call before any bootstrap read
// reads it once here; IBootstrapSource is resolved lazily because it depends on the HttpClient whose handler asks for this
// token. The bootstrap itself is a GET, which never asks for a token, so that read cannot recurse.

using Account.Client;

namespace Blazor.Client.Bootstrap;

public sealed class BootstrapAntiforgeryTokenSource(IServiceProvider serviceProvider) : IAntiforgeryTokenSource
{
    private string? _antiforgeryToken;
    private bool _hasBootstrap;

    public async ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (!_hasBootstrap) await serviceProvider.GetRequiredService<IBootstrapSource>().GetAsync(cancellationToken);

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
