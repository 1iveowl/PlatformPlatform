using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazor.Client.Components.Images;

// A file offered by file-picker.js: its size and declared type, and the blob: preview the module keeps only if it is accepted
public sealed record ChosenFile(long Size, string? ContentType, string PreviewUrl);

// The typed access to wwwroot/js/file-picker.js for ImagePicker. The handle is disposed with the wrapper, which revokes the
// preview URL, forgets the selection and removes the listeners. The picker can be torn down while the import or the attach is
// still in flight: DisposeAsync waits for the attach to settle and then releases the handle and the module once, so a late
// handle never keeps its listeners and the picker can release its .NET reference after it. Every call made after teardown
// does nothing. Every call tolerates a document that is already gone (a full document navigation leaving the authenticated
// surface).
public sealed class FilePickerInterop(IJSRuntime javaScriptRuntime) : IAsyncDisposable
{
    private const string ModulePath = "./js/file-picker.js";

    private Task? _attach;
    private IJSObjectReference? _handle;
    private bool _isDisposed;
    private IJSObjectReference? _module;

    private IJSObjectReference? Handle => _isDisposed ? null : _handle;

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;

        _isDisposed = true;

        // The attach reports its own failure to the picker; here it only has to settle, so what it acquired is released below
        if (_attach is not null) await _attach.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

        var handle = _handle;
        var module = _module;
        _handle = null;
        _module = null;
        try
        {
            if (handle is not null) await JavaScriptRelease.HandleAsync(handle);
        }
        finally
        {
            if (module is not null) await JavaScriptRelease.ReferenceAsync(module);
        }
    }

    public async ValueTask AttachAsync<TComponent>(ElementReference dropZone, ElementReference input, DotNetObjectReference<TComponent> dotNet) where TComponent : class
    {
        if (_isDisposed || _attach is not null) return;

        await (_attach = AttachOnceAsync(dropZone, input, dotNet));
    }

    public async ValueTask OpenAsync()
    {
        if (Handle is not { } handle) return;

        try
        {
            await handle.InvokeVoidAsync("open");
        }
        catch (JSDisconnectedException)
        {
            // The document is being replaced
        }
    }

    // The selected file's content, or null when nothing is selected. The browser stream refuses a file larger than
    // maximumSize before reading it, so the copy never holds more than that; it is read in full while the stream reference
    // is alive, because the reference is released here.
    public async ValueTask<Stream?> ReadSelectedAsync(long maximumSize, CancellationToken cancellationToken)
    {
        if (Handle is not { } handle) return null;

        IJSStreamReference? streamReference;
        try
        {
            streamReference = await handle.InvokeAsync<IJSStreamReference?>("readSelected", cancellationToken);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }

        if (streamReference is null) return null;

        await using (streamReference)
        {
            await using var browserStream = await streamReference.OpenReadStreamAsync(maximumSize, cancellationToken);
            var content = new MemoryStream((int)streamReference.Length);
            await browserStream.CopyToAsync(content, cancellationToken);
            content.Position = 0;
            return content;
        }
    }

    public async ValueTask ResetAsync()
    {
        if (Handle is not { } handle) return;

        try
        {
            await handle.InvokeVoidAsync("reset");
        }
        catch (JSDisconnectedException)
        {
            // The document is being replaced, and the preview with it
        }
    }

    // A handle that arrives after teardown is still kept, so DisposeAsync, which waits for this attach, releases it
    private async Task AttachOnceAsync<TComponent>(ElementReference dropZone, ElementReference input, DotNetObjectReference<TComponent> dotNet) where TComponent : class
    {
        try
        {
            _module = await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            if (_isDisposed) return;

            _handle = await _module.InvokeAsync<IJSObjectReference>("attach", dropZone, input, dotNet);
        }
        catch (JSDisconnectedException)
        {
            // The document is being replaced; the picker stays inert
        }
    }
}
