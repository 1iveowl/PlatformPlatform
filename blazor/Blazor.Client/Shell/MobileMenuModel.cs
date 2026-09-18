namespace Blazor.Client.Shell;

// What the shell's mobile menu offers below the small breakpoint, where the sidebar and the user menu are hidden: the
// navigation, the theme modes, the languages, a way to write to support and the user block with Log out. The theme modes
// are the user menu's own items, so both chooser show the same state, and the support entry only renders when the brand
// names an address, which platform-settings.jsonc may leave empty.
public static class MobileMenuModel
{
    // The link that opens the device's mail application addressed to support, or null when the brand names no address
    public static string? SupportHref(string? supportEmail)
    {
        var address = supportEmail?.Trim();
        return string.IsNullOrEmpty(address) || !address.Contains('@') ? null : $"mailto:{Uri.EscapeDataString(address)}";
    }

    // The theme modes of the user menu's model, in its order, with the mode the document applies marked
    public static IReadOnlyList<UserMenuItem> ThemeItems(IReadOnlyList<UserMenuItem> items)
    {
        return items.Where(item => item.Kind == UserMenuItemKind.Theme).ToArray();
    }
}
