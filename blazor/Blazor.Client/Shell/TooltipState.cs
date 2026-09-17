namespace Blazor.Client.Shell;

// When the in-house Tooltip shows: while a mouse or pen hovers its anchor, while focus is inside it, and after a touch tap
// until the next tap or until focus leaves; the tap that closes it keeps it hidden while focus stays. Escape hides it until
// every one of those ends, so the reason does not reappear while the pointer or focus that showed it is still there.
public sealed class TooltipState
{
    private bool _dismissed;
    private bool _focused;
    private bool _hovered;
    private bool _tapped;

    public bool IsOpen => !_dismissed && (_hovered || _focused || _tapped);

    public void PointerEntered(string? pointerType)
    {
        if (pointerType == "touch") return;

        _hovered = true;
    }

    public void PointerLeft(string? pointerType)
    {
        if (pointerType == "touch") return;

        _hovered = false;
        ResetDismissalWhenInactive();
    }

    public void PointerReleased(string? pointerType)
    {
        if (pointerType != "touch") return;

        if (_dismissed || !_tapped)
        {
            _dismissed = false;
            _tapped = true;
            return;
        }

        // A tap usually focuses the control too, so closing hides the tooltip until focus leaves
        _tapped = false;
        _dismissed = true;
    }

    public void FocusEntered()
    {
        _focused = true;
    }

    public void FocusLeft()
    {
        _focused = false;
        _tapped = false;
        ResetDismissalWhenInactive();
    }

    public void KeyPressed(string key)
    {
        if (key == "Escape" && IsOpen) _dismissed = true;
    }

    private void ResetDismissalWhenInactive()
    {
        if (!_hovered && !_focused && !_tapped) _dismissed = false;
    }
}
