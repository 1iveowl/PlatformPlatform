using Microsoft.JSInterop;

namespace Blazor.Client.Components;

// One owner's attachment to a JavaScript module in wwwroot/js: the imported module, the .NET reference the module calls back on,
// and the handle its attach function returns, whose dispose removes the listeners. The owner can be torn down at any await of
// the attach, so the attachment owns all three from the moment they arrive. An import that completes after teardown is never
// attached, and DisposeAsync waits for the attach in flight before it releases the handle, the module and the reference, each
// once and in that order, so the module never calls back on a released reference.
//
// For an owner that attaches one handle for its whole lifetime and creates the reference for it: DataList, ModalDialog,
// SidePane, UnsavedChangesGuard and ViewportState. A wrapper whose callers own the reference and which attaches several
// handles keeps its own bookkeeping (ShellInterop, FilePickerInterop).
public sealed class ModuleAttachment<TOwner>(IJSRuntime javaScriptRuntime, string modulePath) : IAsyncDisposable where TOwner : class
{
    private Task<IJSObjectReference?>? _attach;
    private IJSObjectReference? _handle;
    private bool _isDisposed;
    private IJSObjectReference? _module;
    private DotNetObjectReference<TOwner>? _reference;

    // The attached handle; null until the attach completes and again from the moment the owner is torn down
    public IJSObjectReference? Handle => _isDisposed ? null : _handle;

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;

        _isDisposed = true;

        // The attach in flight reports its own failure to the owner that awaits it; here it only has to settle, so that what it
        // acquired after this point is released below with the rest
        if (_attach is not null) await ((Task)_attach).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReleaseAsync();
    }

    // Imports the module and calls its attach function once, with the arguments built around the reference; a later call returns
    // the same attach. The result is null when the owner was torn down first or the document is already gone.
    public Task<IJSObjectReference?> AttachAsync(TOwner owner, string identifier, Func<DotNetObjectReference<TOwner>, object?[]> arguments)
    {
        return _attach ??= _isDisposed ? Task.FromResult<IJSObjectReference?>(null) : AttachOnceAsync(owner, identifier, arguments);
    }

    private async Task<IJSObjectReference?> AttachOnceAsync(TOwner owner, string identifier, Func<DotNetObjectReference<TOwner>, object?[]> arguments)
    {
        try
        {
            _module = await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", modulePath);
            if (_isDisposed) return null;

            _reference = DotNetObjectReference.Create(owner);
            _handle = await _module.InvokeAsync<IJSObjectReference>(identifier, arguments(_reference));
            return Handle;
        }
        catch (JSDisconnectedException)
        {
            // The document is already gone, so nothing is attached; what was acquired before is released
            await ReleaseAsync();
            return null;
        }
        catch
        {
            // A failed attach releases what it acquired before the failure reaches the owner
            await ReleaseAsync();
            throw;
        }
    }

    // Every field is cleared before it is released, so a second release finds nothing, and a step that fails still lets the
    // later steps run
    private async ValueTask ReleaseAsync()
    {
        var handle = _handle;
        var module = _module;
        var reference = _reference;
        _handle = null;
        _module = null;
        _reference = null;

        try
        {
            if (handle is not null) await JavaScriptRelease.HandleAsync(handle);
        }
        finally
        {
            try
            {
                if (module is not null) await JavaScriptRelease.ReferenceAsync(module);
            }
            finally
            {
                reference?.Dispose();
            }
        }
    }
}
