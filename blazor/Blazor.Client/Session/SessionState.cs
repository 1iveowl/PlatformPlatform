// The signed-in user's bootstrap as the interactive surface shows it, shared by the header, the page component and the
// antiforgery token source of one WebAssembly application. Every read goes through IBootstrapSource, so identity comes
// from the account API and never from the host page.
//
// Only the most recently started read of the current generation is accepted, and it is committed as a whole: the
// identity here, the antiforgery token and the feature flags change together under one lock before Changed and the flag
// state's notification are raised. An older read that completes later changes nothing, so a slow bootstrap started
// before a profile save cannot put the old name back, and a bootstrap of the previous tenant cannot restore its token or
// flags. Callers that ask before a bootstrap is accepted share one read; a read that failed is forgotten, so the next call
// reads again, and nothing retries on its own.
//
// Leaving the authenticated surface (logout, tenant switch, a lost session) moves to a new generation, cancels the
// requests started with RequestsAborted and clears the identity, the antiforgery token, the feature flags and the previous
// identity's cached list pages and toasts before the full document navigation starts. A read that completes after that
// is cancelled rather than committed.

using Account.Features.Authentication.Queries;
using Blazor.Client.Bootstrap;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;

namespace Blazor.Client.Session;

public sealed class SessionState : IDisposable
{
    private readonly AuthenticationNavigator _authenticationNavigator;
    private readonly IBootstrapSource _bootstrapSource;
    private readonly Lock _lock = new();
    private readonly DataListPageCache _pageCache;
    private readonly CancellationTokenSource _requestsAborted = new();
    private readonly ToastService _toastService;
    private bool _departed;
    private long _generation;
    private PendingRead? _latestRead;
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

    // The accepted bootstrap, or the read in progress that every caller shares until one is accepted. A read that failed
    // or was cancelled is not shared, so calling again after a failure reads again.
    public Task<BootstrapResponse> GetAsync()
    {
        PendingRead read;
        lock (_lock)
        {
            if (_departed) return Task.FromCanceled<BootstrapResponse>(RequestsAborted);
            if (Current is not null) return Task.FromResult(Current);
            if (_latestRead is { Completion.Task.IsFaulted: false, Completion.Task.IsCanceled: false }) return _latestRead.Completion.Task;

            read = BeginRead();
        }

        return RunAsync(read);
    }

    // Reads the bootstrap again, for instance after the account API refreshed the session's claims. Returns the newest
    // bootstrap this state holds, which is not this read's result when a later read was started before this one completed.
    public Task<BootstrapResponse> RefreshAsync()
    {
        PendingRead read;
        lock (_lock)
        {
            if (_departed) return Task.FromCanceled<BootstrapResponse>(RequestsAborted);

            read = BeginRead();
        }

        return RunAsync(read);
    }

    private PendingRead BeginRead()
    {
        var read = new PendingRead(++_latestReadSequence, _generation, new TaskCompletionSource<BootstrapResponse>(TaskCreationOptions.RunContinuationsAsynchronously));
        _latestRead = read;
        return read;
    }

    private Task<BootstrapResponse> RunAsync(PendingRead read)
    {
        _ = CompleteAsync(read);
        return read.Completion.Task;
    }

    private async Task CompleteAsync(PendingRead read)
    {
        try
        {
            var bootstrap = await _bootstrapSource.GetAsync(RequestsAborted);
            read.Completion.TrySetResult(await AcceptAsync(read, bootstrap));
        }
        catch (OperationCanceledException)
        {
            read.Completion.TrySetCanceled(RequestsAborted);
        }
        catch (Exception exception)
        {
            if (IsDeparted(read))
            {
                read.Completion.TrySetCanceled(RequestsAborted);
            }
            else
            {
                read.Completion.TrySetException(exception);
            }
        }
    }

    private Task<BootstrapResponse> AcceptAsync(PendingRead read, BootstrapResponse bootstrap)
    {
        Action notifyTransportState;
        lock (_lock)
        {
            if (read.Generation != _generation) throw new OperationCanceledException(RequestsAborted);

            // A read superseded by a later one changes nothing and answers with what this state holds or will hold
            if (read.Sequence != _latestReadSequence) return Current is not null ? Task.FromResult(Current) : _latestRead!.Completion.Task;

            Current = bootstrap;
            notifyTransportState = _bootstrapSource.Apply(bootstrap);
        }

        notifyTransportState();
        Changed?.Invoke();
        return Task.FromResult(bootstrap);
    }

    private bool IsDeparted(PendingRead read)
    {
        lock (_lock)
        {
            return read.Generation != _generation;
        }
    }

    private void OnLeaving()
    {
        Action notifyTransportState;
        lock (_lock)
        {
            _departed = true;
            _generation++;
            _latestRead = null;
            Current = null;
            notifyTransportState = _bootstrapSource.Apply(null);
        }

        _requestsAborted.Cancel();
        _pageCache.Clear();
        _toastService.Clear();
        notifyTransportState();
    }

    private sealed record PendingRead(long Sequence, long Generation, TaskCompletionSource<BootstrapResponse> Completion);
}
