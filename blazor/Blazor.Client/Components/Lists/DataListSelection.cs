// The selection model of a DataList, over the keys of the rows on the current page in their rendered order.
// Click: a plain click selects only that row and activates it; Ctrl or Cmd toggles the row; Shift selects the range from
// the anchor (the last row clicked or toggled) to the row. Space toggles and Shift+Arrow extends, in multiple mode only.
// In single mode a toggle selects the row alone, and a range is a plain selection. With no selection mode, nothing is
// selected and a plain click only activates.

namespace Blazor.Client.Components.Lists;

public enum DataListSelectionMode
{
    None,
    Single,
    Multiple
}

public enum DataListHeaderSelection
{
    None,
    Some,
    All
}

public sealed class DataListSelection(DataListSelectionMode mode)
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public DataListSelectionMode Mode { get; } = mode;

    public IReadOnlySet<string> Keys => _keys;

    public int? AnchorIndex { get; private set; }

    // Returns true when the click activates the row
    public bool Click(IReadOnlyList<string> pageKeys, int index, bool toggle, bool range)
    {
        if (index < 0 || index >= pageKeys.Count) return false;
        if (Mode == DataListSelectionMode.None) return !toggle && !range;

        if (Mode == DataListSelectionMode.Multiple && range)
        {
            SelectRange(pageKeys, AnchorIndex ?? index, index);
            return false;
        }

        if (toggle)
        {
            Toggle(pageKeys, index);
            return false;
        }

        _keys.Clear();
        _keys.Add(pageKeys[index]);
        AnchorIndex = index;
        return true;
    }

    public void Toggle(IReadOnlyList<string> pageKeys, int index)
    {
        if (Mode == DataListSelectionMode.None || index < 0 || index >= pageKeys.Count) return;
        var key = pageKeys[index];
        var wasSelected = _keys.Contains(key);
        if (Mode == DataListSelectionMode.Single) _keys.Clear();
        if (wasSelected)
        {
            _keys.Remove(key);
        }
        else
        {
            _keys.Add(key);
        }

        AnchorIndex = index;
    }

    // Shift+Arrow, Shift+Home and Shift+End: the anchor stays where the extension started
    public void Extend(IReadOnlyList<string> pageKeys, int fromIndex, int toIndex)
    {
        if (Mode != DataListSelectionMode.Multiple || fromIndex < 0 || fromIndex >= pageKeys.Count || toIndex < 0 || toIndex >= pageKeys.Count) return;
        SelectRange(pageKeys, AnchorIndex ?? fromIndex, toIndex);
    }

    // The header checkbox: selects every row on the page unless all are already selected, then clears them
    // Selects the loaded rows, never more than maxKeys in total, and clears them once the cap is reached; unloaded rows are never
    // implied
    public void ToggleAll(IReadOnlyList<string> pageKeys, int maxKeys = int.MaxValue)
    {
        if (Mode != DataListSelectionMode.Multiple) return;
        if (GetHeaderSelection(pageKeys) == DataListHeaderSelection.All || _keys.Count >= maxKeys)
        {
            _keys.ExceptWith(pageKeys);
        }
        else
        {
            foreach (var key in pageKeys)
            {
                if (_keys.Count >= maxKeys) break;
                _keys.Add(key);
            }
        }

        AnchorIndex = null;
    }

    public DataListHeaderSelection GetHeaderSelection(IReadOnlyList<string> pageKeys)
    {
        var selectedOnPage = pageKeys.Count(_keys.Contains);
        if (selectedOnPage == 0) return DataListHeaderSelection.None;
        return selectedOnPage == pageKeys.Count ? DataListHeaderSelection.All : DataListHeaderSelection.Some;
    }

    // Deselects every key that is not loaded; returns true when the selection changed
    public bool Retain(IReadOnlyList<string> loadedKeys)
    {
        var removed = _keys.RemoveWhere(key => !loadedKeys.Contains(key));
        if (removed > 0) AnchorIndex = null;
        return removed > 0;
    }

    // Returns true when anything was selected
    public bool Clear()
    {
        AnchorIndex = null;
        if (_keys.Count == 0) return false;
        _keys.Clear();
        return true;
    }

    private void SelectRange(IReadOnlyList<string> pageKeys, int anchorIndex, int index)
    {
        var anchor = Math.Clamp(anchorIndex, 0, pageKeys.Count - 1);
        _keys.Clear();
        for (var position = Math.Min(anchor, index); position <= Math.Max(anchor, index); position++)
        {
            _keys.Add(pageKeys[position]);
        }

        AnchorIndex = anchor;
    }
}
