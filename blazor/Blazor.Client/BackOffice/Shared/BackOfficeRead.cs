// One back-office read and the state a card, tab or detail page renders from it: loading, loaded, not found or failed. A load
// shows the loading state until its answer arrives; a failure is presented through ApiFailurePresenter and leaves the failed
// state, from which the component offers a retry of the same read. A refresh, after an action, keeps what is shown when it
// fails. A newer load or refresh cancels the one in flight and drops its answer, and so does disposal, so an older request
// never replaces newer state. A 401, or a failure the presentation suppresses as an authentication loss, changes nothing:
// the unauthorized handling has already started leaving the runtime.

using Account.Client;
using Blazor.Client.Forms;

namespace Blazor.Client.BackOffice.Shared;

public enum BackOfficeReadStatus
{
    Loading,
    Loaded,
    NotFound,
    Failed
}

// isNotFoundOn404: a 404 answer to a load is the not-found state, unpresented, rather than a failure
public sealed class BackOfficeRead<TValue>(bool isNotFoundOn404 = false) : IDisposable
{
    private const int NotFoundStatusCode = 404;

    private CancellationTokenSource? _cancellation;
    private bool _disposed;
    private int _version;

    public BackOfficeReadStatus Status { get; private set; } = BackOfficeReadStatus.Loading;

    // The selected value while loaded; default otherwise, and kept by a failed refresh
    public TValue? Value { get; private set; }

    // The presented failure's message while failed, when the presentation has one
    public string? FailureMessage { get; private set; }

    public bool IsLoading => Status == BackOfficeReadStatus.Loading;

    public bool IsLoaded => Status == BackOfficeReadStatus.Loaded;

    public bool IsFailed => Status == BackOfficeReadStatus.Failed;

    public void Dispose()
    {
        _disposed = true;
        _version++;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    // Reads from the loading state. select gives the value to show; null from it is the not-found state. Returns the answer
    // that decided the state, or null when the read was superseded, cancelled or disposed.
    public Task<ApiCallResult<TResponse>?> LoadAsync<TResponse>(
        Func<CancellationToken, Task<ApiCallResult<TResponse>>> read,
        Func<TResponse, TValue?> select,
        ApiFailurePresenter presenter)
    {
        Status = BackOfficeReadStatus.Loading;
        Value = default;
        FailureMessage = null;
        return ReadAsync(read, select, presenter, true);
    }

    // Reads again while keeping what is shown: a failure is presented and leaves the state as it was
    public Task<ApiCallResult<TResponse>?> RefreshAsync<TResponse>(
        Func<CancellationToken, Task<ApiCallResult<TResponse>>> read,
        Func<TResponse, TValue?> select,
        ApiFailurePresenter presenter)
    {
        return ReadAsync(read, select, presenter, false);
    }

    private async Task<ApiCallResult<TResponse>?> ReadAsync<TResponse>(
        Func<CancellationToken, Task<ApiCallResult<TResponse>>> read,
        Func<TResponse, TValue?> select,
        ApiFailurePresenter presenter,
        bool isLoad)
    {
        if (_disposed) return null;

        var version = ++_version;
        if (_cancellation is not null)
        {
            await _cancellation.CancelAsync();
            _cancellation.Dispose();
        }

        var cancellation = _cancellation = new CancellationTokenSource();
        ApiCallResult<TResponse> result;
        try
        {
            result = await read(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return null;
        }

        if (version != _version) return null;

        if (result.IsSuccess)
        {
            Value = select(result.Value);
            Status = Value is null ? BackOfficeReadStatus.NotFound : BackOfficeReadStatus.Loaded;
            FailureMessage = null;
            return result;
        }

        if (result.Outcome == ApiCallOutcome.Unauthorized) return result;

        if (isLoad && isNotFoundOn404 && result.Problem?.StatusCode == NotFoundStatusCode)
        {
            Status = BackOfficeReadStatus.NotFound;
            return result;
        }

        var failure = presenter.Present(result);
        if (failure.Kind == ApiFailureKind.Suppressed || !isLoad) return result;

        Status = BackOfficeReadStatus.Failed;
        FailureMessage = failure.Message;
        return result;
    }
}
