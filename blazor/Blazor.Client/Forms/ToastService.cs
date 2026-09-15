// The in-house toast queue behind ToastRegion. Interactive surfaces use it instead of the component library's toast
// provider, which writes inline style attributes that the host's nonce-only style policy blocks. The region renders only
// classes from the external stylesheet, so no style attribute is written in markup or from script.
// Toasts stay until dismissed or replaced, so their actions remain reachable by keyboard and screen reader users.

namespace Blazor.Client.Forms;

public enum ToastKind
{
    Error,
    Warning
}

// ActionLabel names the toast's single action button; OnAction runs when it is pressed
public sealed record Toast(
    long Id,
    ToastKind Kind,
    string Title,
    string? Message,
    string TestId,
    string? ActionLabel,
    Action? OnAction
);

public sealed class ToastService
{
    // The oldest toast is dropped beyond this many, so repeated failures cannot fill the screen
    public const int MaximumVisibleToasts = 3;

    private readonly List<Toast> _toasts = [];
    private long _nextId;

    public IReadOnlyList<Toast> Toasts => _toasts;

    public event Action? Changed;

    public Toast Show(ToastKind kind, string title, string? message, string testId, string? actionLabel = null, Action? onAction = null)
    {
        var toast = new Toast(++_nextId, kind, title, message, testId, actionLabel, onAction);
        _toasts.Add(toast);
        if (_toasts.Count > MaximumVisibleToasts) _toasts.RemoveAt(0);

        Changed?.Invoke();
        return toast;
    }

    public void Dismiss(long id)
    {
        if (_toasts.RemoveAll(toast => toast.Id == id) > 0) Changed?.Invoke();
    }

    // Authentication loss leaves the document; clearing first keeps no failure text on screen while it unloads
    public void Clear()
    {
        if (_toasts.Count == 0) return;

        _toasts.Clear();
        Changed?.Invoke();
    }
}
