namespace Blazor.Client.Shell;

// Whether the sidebar is collapsed, matching the React edition's side menu: below the extra large breakpoint it starts
// collapsed and an expanded sidebar overlays the content; from that breakpoint the user's choice stored under
// side-menu-collapsed applies. shell.js returns the raw stored value; a missing, malformed or unreadable value (storage
// denied) falls back to expanded, so a damaged preference never blocks navigation.
public static class ShellSidebarState
{
    public const string CollapsedStorageKey = "side-menu-collapsed";

    public static bool IsCollapsed(string? storedValue, bool isWide)
    {
        if (!isWide) return true;

        return ParseStored(storedValue) ?? false;
    }

    // Only a choice made on a wide viewport is remembered; below it the collapse is the viewport's, not the user's
    public static bool ShouldPersist(bool isWide)
    {
        return isWide;
    }

    public static string FormatStored(bool collapsed)
    {
        return collapsed ? "true" : "false";
    }

    private static bool? ParseStored(string? value)
    {
        return value switch
        {
            "true" => true,
            "false" => false,
            _ => null
        };
    }
}
