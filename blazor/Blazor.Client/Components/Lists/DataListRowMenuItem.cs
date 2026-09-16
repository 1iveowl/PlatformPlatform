namespace Blazor.Client.Components.Lists;

// One action of a row menu. A disabled item stays visible, so the reason for an unavailable action can be explained.
public sealed record DataListRowMenuItem(string Label, Func<Task> OnSelect, bool Disabled = false, string? TestId = null);

public static class DataListRowMenuNavigation
{
    // The next enabled item from an index in a direction, wrapping around; -1 when every item is disabled. Start from -1
    // for the first enabled item and from the item count for the last.
    public static int NextEnabled(IReadOnlyList<DataListRowMenuItem> items, int from, int step)
    {
        for (var offset = 1; offset <= items.Count; offset++)
        {
            var index = ((from + step * offset) % items.Count + items.Count) % items.Count;
            if (!items[index].Disabled) return index;
        }

        return -1;
    }
}
