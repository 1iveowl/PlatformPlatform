using Blazor.Client.Session;

namespace Blazor.Client.Shell;

public enum UserMenuItemKind
{
    Profile,
    Preferences,
    Theme,
    SwitchTenant,
    AccountSettings,
    LogOut
}

// One entry of the user menu. Href is set for a link, Tenant for a tenant of the switch account group, Theme for a mode of
// the change theme group, with Checked on the mode the document applies.
public sealed record UserMenuItem(
    UserMenuItemKind Kind,
    string? Href = null,
    TenantSwitcherOption? Tenant = null,
    bool Disabled = false,
    ThemeMode? Theme = null,
    bool Checked = false
);

// The items of the shell's user menu, matching the React edition's UserMenuDropdownContent: Profile, Preferences, the
// change theme group (System, Light and Dark, the current mode checked), the switch account group for a user with more than one tenant (the current tenant marked and not selectable), Account
// settings for an Owner or an Admin, and Log out. While a session transition runs, the tenants and Log out are disabled
// so a second transition cannot start; SessionTransition stays the authority that serializes them.
public static class UserMenuModel
{
    public static IReadOnlyList<UserMenuItem> Create(string? role, IReadOnlyCollection<TenantSwitcherOption> tenants, bool isTransitionBusy, ThemeMode currentTheme = ThemeMode.System)
    {
        var items = new List<UserMenuItem>
        {
            new(UserMenuItemKind.Profile, AppUrls.ToAbsolute("user/profile")),
            new(UserMenuItemKind.Preferences, AppUrls.ToAbsolute("user/preferences"))
        };

        items.AddRange(ThemePreference.Modes.Select(mode => new UserMenuItem(UserMenuItemKind.Theme, Theme: mode, Checked: mode == currentTheme)));

        if (TenantSwitcherOptions.IsVisible(tenants))
        {
            items.AddRange(tenants.Select(tenant => new UserMenuItem(UserMenuItemKind.SwitchTenant, Tenant: tenant, Disabled: tenant.IsCurrent || isTransitionBusy)));
        }

        if (ShellNavigation.CanManageAccount(role)) items.Add(new UserMenuItem(UserMenuItemKind.AccountSettings, AppUrls.ToAbsolute("account/settings")));

        items.Add(new UserMenuItem(UserMenuItemKind.LogOut, Disabled: isTransitionBusy));
        return items;
    }

    // Choosing a link or a transition closes the menu, except while the transition runs: the menu then stays open with its
    // items disabled until the document is left or the transition reports a failure
    public static bool ClosesOnSelect(UserMenuItem item)
    {
        return item.Kind is not (UserMenuItemKind.SwitchTenant or UserMenuItemKind.LogOut);
    }
}
