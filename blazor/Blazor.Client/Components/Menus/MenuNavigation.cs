namespace Blazor.Client.Components.Menus;

// The keyboard model every in-house menu shares (the row menu of a DataList and the shell's user menu): Arrow keys move to
// the next enabled item and wrap around, Home and End move to the first and last enabled item.
public static class MenuNavigation
{
    // The next enabled item from an index in a direction, wrapping around; -1 when every item is disabled. Start from -1
    // for the first enabled item and from the item count for the last.
    public static int NextEnabled<TItem>(IReadOnlyList<TItem> items, Func<TItem, bool> isDisabled, int from, int step)
    {
        for (var offset = 1; offset <= items.Count; offset++)
        {
            var index = ((from + step * offset) % items.Count + items.Count) % items.Count;
            if (!isDisabled(items[index])) return index;
        }

        return -1;
    }

    // The index a key moves the active item to, or null when the key is not a movement key
    public static int? MoveForKey<TItem>(string key, IReadOnlyList<TItem> items, Func<TItem, bool> isDisabled, int activeIndex)
    {
        return key switch
        {
            "ArrowDown" => NextEnabled(items, isDisabled, activeIndex, 1),
            "ArrowUp" => NextEnabled(items, isDisabled, activeIndex, -1),
            "Home" => NextEnabled(items, isDisabled, -1, 1),
            "End" => NextEnabled(items, isDisabled, items.Count, -1),
            _ => null
        };
    }
}
