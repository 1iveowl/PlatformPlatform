// Whether the publish this runtime was loaded from is still served. One fingerprinted asset route of the document that
// started this runtime is captured once and re-requested on every in-app navigation, which is the moment a tab that has
// been open across a deployment is used again. A 404 means this document's asset set is gone: the client is stale from then
// on, its next write is refused by StaleClientRequestHandler and the surface asks for a reload.
//
// The captured route must survive an enhanced navigation, which replaces the document with one from the publish that is
// served now, so this is a scoped service of the WebAssembly application rather than state on a component. A runtime that
// finds no fingerprinted route in its document learns nothing and never becomes stale on this account; the version window
// is then the only signal.

using Microsoft.JSInterop;

namespace Blazor.Client.Bootstrap;

public sealed class StaleAssetProbe(IJSRuntime javaScriptRuntime, ClientVersionState versionState) : IAsyncDisposable
{
    private const string ModulePath = "./js/stale-assets.js";
    private const int NotFound = 404;

    private IJSObjectReference? _module;

    // The asset route this runtime watches, once one was captured
    public string? CapturedAssetUrl { get; private set; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The document is already gone
        }
    }

    // Captures the first fingerprinted asset route of the document that started this runtime, and keeps it
    public async Task StartAsync()
    {
        if (CapturedAssetUrl is not null) return;

        try
        {
            var module = await GetModuleAsync();
            if (module is null) return;

            var urls = await module.InvokeAsync<string[]>("readAssetUrls", AppUrls.PathBase);
            CapturedAssetUrl = FingerprintedAsset.SelectWatchable(urls);
        }
        catch (JSDisconnectedException)
        {
            // The document is gone; nothing is watched
        }
    }

    public async Task CheckAsync()
    {
        if (CapturedAssetUrl is null || versionState.IsStale) return;

        try
        {
            var module = await GetModuleAsync();
            if (module is null) return;

            var status = await module.InvokeAsync<int?>("requestStatus", CapturedAssetUrl);
            if (status == NotFound) versionState.ReportMissingAsset(CapturedAssetUrl);
        }
        catch (JSDisconnectedException)
        {
            // The document is gone; the answer no longer matters
        }
    }

    private async ValueTask<IJSObjectReference?> GetModuleAsync()
    {
        try
        {
            return _module ??= await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
        }
        catch (JSDisconnectedException)
        {
            // The document is gone, so nothing can be imported into it
            return null;
        }
    }
}
