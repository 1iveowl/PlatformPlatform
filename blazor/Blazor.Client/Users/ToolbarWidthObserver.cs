using Blazor.Client.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazor.Client.Users;

// Tells the users page whether its toolbar is wide enough for inline filters (UsersFilterModel), through
// wwwroot/js/toolbar-width.js. The page can be torn down at any await of the attach, so ModuleAttachment owns the module, the
// handle and the reference: an import that completes after teardown never observes, and a late handle is disconnected by its own
// dispose before the reference is released. Once disposed, a width the module still reports reaches no one.
public sealed class ToolbarWidthObserver(IJSRuntime javaScriptRuntime, Action<bool> widthChanged) : IAsyncDisposable
{
    private const string ModulePath = "./js/toolbar-width.js";
    private readonly ModuleAttachment<ToolbarWidthObserver> _attachment = new(javaScriptRuntime, ModulePath);
    private bool _isDisposed;

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        await _attachment.DisposeAsync();
    }

    public Task ObserveAsync(ElementReference toolbar, int thresholdRem)
    {
        return _attachment.AttachAsync(this, "observeToolbarWidth", reference => [toolbar, reference, thresholdRem]);
    }

    [JSInvokable]
    public Task OnToolbarWidthChanged(bool isWide)
    {
        if (!_isDisposed) widthChanged(isWide);
        return Task.CompletedTask;
    }
}
