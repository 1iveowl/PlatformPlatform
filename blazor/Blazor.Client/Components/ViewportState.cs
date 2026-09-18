using Microsoft.JSInterop;

namespace Blazor.Client.Components;

// Which breakpoints the viewport reaches. The widths are minimum widths, so a viewport that reaches Medium also reaches
// Small; ViewportMatchesTests pins that order.
public sealed record ViewportMatches(bool Small, bool Medium, bool Large, bool ExtraLarge, bool ExtraExtraLarge)
{
    // What the server assumes while it renders or prerenders, and what the browser falls back to when the module cannot be
    // imported: the widest viewport, where every surface keeps the docked, side-by-side layout it had before this pass
    public static ViewportMatches Widest { get; } = new(true, true, true, true, true);

    public bool Reaches(Breakpoint breakpoint)
    {
        return breakpoint switch
        {
            Breakpoint.Small => Small,
            Breakpoint.Medium => Medium,
            Breakpoint.Large => Large,
            Breakpoint.ExtraLarge => ExtraLarge,
            Breakpoint.ExtraExtraLarge => ExtraExtraLarge,
            _ => throw new ArgumentOutOfRangeException(nameof(breakpoint), breakpoint, null)
        };
    }
}

// The viewport width, for the components that must decide in C# because the DOM changes with it, not only its layout: a side
// pane that becomes a modal dialog below Medium, a list that loads differently below Small. Layout alone stays a media query
// in the host stylesheet and never reaches here.
//
// The service is registered once per container, so one runtime attaches wwwroot/js/viewport.js once however many components
// subscribe, and repeated navigation between pages adds no second set of listeners. During static rendering and prerendering
// there is no browser to ask, so the state stays at the widest viewport and AttachAsync is never called.
public sealed class ViewportState(IJSRuntime javaScriptRuntime) : IAsyncDisposable
{
    private const string ModulePath = "./js/viewport.js";
    private IJSObjectReference? _handle;
    private bool _isDisposed;
    private IJSObjectReference? _module;

    private DotNetObjectReference<ViewportState>? _reference;

    public ViewportMatches Matches { get; private set; } = ViewportMatches.Widest;

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
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
            // The document is gone, and its listeners with it
        }

        _handle = null;
        _module = null;
        _reference?.Dispose();
        _reference = null;
    }

    public event Action? Changed;

    public bool Reaches(Breakpoint breakpoint)
    {
        return Matches.Reaches(breakpoint);
    }

    // Idempotent: the first interactive component that needs the width attaches the module, every later call returns at once
    public async Task AttachAsync()
    {
        if (_isDisposed || _reference is not null) return;

        _reference = DotNetObjectReference.Create(this);
        try
        {
            _module = await javaScriptRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _handle = await _module.InvokeAsync<IJSObjectReference>("attachViewport", _reference);
            Apply(await _handle.InvokeAsync<ViewportMatches>("read"));
        }
        catch (JSDisconnectedException)
        {
            // The document is already gone; the state stays at the widest viewport
        }
    }

    [JSInvokable]
    public Task OnViewportChanged(ViewportMatches matches)
    {
        Apply(matches);
        return Task.CompletedTask;
    }

    // Returns whether the width changed, so a caller can tell a real crossing from a report that repeats what it knew
    public bool Apply(ViewportMatches matches)
    {
        if (Matches == matches) return false;

        Matches = matches;
        Changed?.Invoke();
        return true;
    }
}
