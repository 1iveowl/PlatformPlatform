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
    private readonly ModuleAttachment<ViewportState> _attachment = new(javaScriptRuntime, ModulePath);
    private Task? _attach;
    private bool _isDisposed;

    public ViewportMatches Matches { get; private set; } = ViewportMatches.Widest;

    public async ValueTask DisposeAsync()
    {
        _isDisposed = true;
        await _attachment.DisposeAsync();
    }

    public event Action? Changed;

    public bool Reaches(Breakpoint breakpoint)
    {
        return Matches.Reaches(breakpoint);
    }

    // Idempotent: the first interactive component that needs the width attaches the module, and every caller awaits that one
    // attach. A container disposed before the width is read keeps the widest viewport and notifies no one.
    public Task AttachAsync()
    {
        return _attach ??= AttachOnceAsync();
    }

    [JSInvokable]
    public Task OnViewportChanged(ViewportMatches matches)
    {
        if (!_isDisposed) Apply(matches);
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

    private async Task AttachOnceAsync()
    {
        var handle = await _attachment.AttachAsync(this, "attachViewport", reference => [reference]);
        if (handle is null) return;

        try
        {
            var matches = await handle.InvokeAsync<ViewportMatches>("read");
            if (!_isDisposed) Apply(matches);
        }
        catch (JSDisconnectedException)
        {
            // The document is already gone; the state stays at the widest viewport
        }
    }
}
