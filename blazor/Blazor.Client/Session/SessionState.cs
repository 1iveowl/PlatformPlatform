// The signed-in user's bootstrap as the interactive surface shows it, shared by the header and the page component of one
// WebAssembly application. Every read goes through IBootstrapSource, so identity comes from the account API and never
// from the host page. A read that started before a newer one completes is discarded, so a slow bootstrap started
// before a profile save cannot put the old name back.
//
// Leaving the authenticated surface (logout, tenant switch, a lost session) cancels the requests started with
// RequestsAborted and clears the previous identity's cached list pages, toasts and bootstrap before the full document
// navigation starts.

using Account.Features.Authentication.Queries;
using Blazor.Client.Bootstrap;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;

namespace Blazor.Client.Session;

public sealed class SessionState : IDisposable
{
    private readonly AuthenticationNavigator _authenticationNavigator;
    private readonly IBootstrapSource _bootstrapSource;
    private readonly DataListPageCache _pageCache;
    private readonly CancellationTokenSource _requestsAborted = new();
    private readonly ToastService _toastService;
    private Task<BootstrapResponse>? _initialRead;
    private long _latestReadSequence;

    public SessionState(IBootstrapSource bootstrapSource, AuthenticationNavigator authenticationNavigator, DataListPageCache pageCache, ToastService toastService)
    {
        _bootstrapSource = bootstrapSource;
        _authenticationNavigator = authenticationNavigator;
        _pageCache = pageCache;
        _toastService = toastService;
        _authenticationNavigator.Leaving += OnLeaving;
    }

    public BootstrapResponse? Current { get; private set; }

    public CancellationToken RequestsAborted => _requestsAborted.Token;

    public void Dispose()
    {
        _authenticationNavigator.Leaving -= OnLeaving;
        _requestsAborted.Dispose();
    }

    public event Action? Changed;

    // The bootstrap of this application, read once and shared by every component that asks before it completes
    public Task<BootstrapResponse> GetAsync()
    {
        if (Current is not null) return Task.FromResult(Current);
        return _initialRead ??= RefreshAsync();
    }

    // Reads the bootstrap again, for instance after the account API refreshed the session's claims. Returns the newest
    // bootstrap this state holds, which is not this read's result when a later read already completed.
    public async Task<BootstrapResponse> RefreshAsync()
    {
        var readSequence = Interlocked.Increment(ref _latestReadSequence);
        var bootstrap = await _bootstrapSource.GetAsync(RequestsAborted);
        if (readSequence != Interlocked.Read(ref _latestReadSequence) || _authenticationNavigator.IsLeaving) return Current ?? bootstrap;

        Current = bootstrap;
        Changed?.Invoke();
        return bootstrap;
    }

    private void OnLeaving()
    {
        _requestsAborted.Cancel();
        _pageCache.Clear();
        _toastService.Clear();
        Current = null;
    }
}
