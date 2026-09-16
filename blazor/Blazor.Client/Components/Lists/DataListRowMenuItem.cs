using Blazor.Client.Components.Menus;

namespace Blazor.Client.Components.Lists;

// One action of a row menu. A disabled item stays visible, so the reason for an unavailable action can be explained.
public sealed record DataListRowMenuItem(string Label, Func<Task> OnSelect, bool Disabled = false, string? TestId = null);

public static class DataListRowMenuNavigation
{
    // The next enabled item from an index in a direction, wrapping around; -1 when every item is disabled. Start from -1
    // for the first enabled item and from the item count for the last.
    public static int NextEnabled(IReadOnlyList<DataListRowMenuItem> items, int from, int step)
    {
        return MenuNavigation.NextEnabled(items, item => item.Disabled, from, step);
    }
}
