using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazor.Client.Components.Images;

// A file offered by file-picker.js: its size and declared type, and the blob: preview the module keeps only if it is accepted
public sealed record ChosenFile(long Size, string? ContentType, string PreviewUrl);

// The typed access to wwwroot/js/file-picker.js for ImagePicker. The handle is disposed with the wrapper, which revokes the
// preview URL, forgets the selection and removes the listeners. Every call tolerates a document that is already gone (a full
// document navigation leaving the authenticated surface).
public sealed class FilePickerInterop(IJSRuntime javaScriptRuntime) : IAsyncDisposable
{
    private const string ModulePath = "./js/file-picker.js";

    private IJSObjectReference? _handle;
    private IJSObjectReference? _module;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_handle is not null)
            {
                await _handle.InvokeVoidAsync("dispose");
                await _handle.DisposeAsync();
            }

            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The document is already gone, and its listeners and blob URLs with it
        }

        _handle = null;
        _module = null;
    }

    public async ValueTask AttachAsync<TComponent>(ElementReference dropZone, ElementReference input, DotNetObjectReference<TComponent> dotNet) where TComponent : class
    {
        try
        {
            _module ??= await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _handle = await _module.InvokeAsync<IJSObjectReference>("attach", dropZone, input, dotNet);
        }
        catch (JSDisconnectedException)
        {
            // The document is being replaced; the picker stays inert
        }
    }

    public async ValueTask OpenAsync()
    {
        if (_handle is null) return;

        try
        {
            await _handle.InvokeVoidAsync("open");
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
        if (_handle is null) return null;

        IJSStreamReference? streamReference;
        try
        {
            streamReference = await _handle.InvokeAsync<IJSStreamReference?>("readSelected", cancellationToken);
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
        if (_handle is null) return;

        try
        {
            await _handle.InvokeVoidAsync("reset");
        }
        catch (JSDisconnectedException)
        {
            // The document is being replaced, and the preview with it
        }
    }
}
