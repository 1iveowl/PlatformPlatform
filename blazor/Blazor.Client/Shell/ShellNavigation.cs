// The navigation the shell's sidebar and mobile menu render, matching the React edition's side menus: Workspace (Home),
// User (Profile, Preferences, Sessions) and Account (Settings, Users), the Account group only for an Owner or an Admin.
// Account overview and billing are left out until their flags and surfaces exist in this edition. The server stays the
// authority for every permission; this only decides which links render.

namespace Blazor.Client.Shell;

public enum ShellNavigationGroup
{
    Workspace,
    User,
    Account
}

public enum ShellNavigationTarget
{
    Home,
    Profile,
    Preferences,
    Sessions,
    AccountSettings,
    Users
}

public sealed record ShellNavigationItem(ShellNavigationGroup Group, ShellNavigationTarget Target, string Href, bool IsCurrent);

public static class ShellNavigation
{
    private static readonly (ShellNavigationGroup Group, ShellNavigationTarget Target, string Path)[] Items =
    [
        (ShellNavigationGroup.Workspace, ShellNavigationTarget.Home, "app"),
        (ShellNavigationGroup.User, ShellNavigationTarget.Profile, "user/profile"),
        (ShellNavigationGroup.User, ShellNavigationTarget.Preferences, "user/preferences"),
        (ShellNavigationGroup.User, ShellNavigationTarget.Sessions, "user/sessions"),
        (ShellNavigationGroup.Account, ShellNavigationTarget.AccountSettings, "account/settings"),
        (ShellNavigationGroup.Account, ShellNavigationTarget.Users, "account/users")
    ];

    public static bool CanManageAccount(string? role)
    {
        return role is nameof(UserRole.Owner) or nameof(UserRole.Admin);
    }

    // currentUri is the absolute document URI from NavigationManager.Uri; the query and fragment do not change the current item
    public static IReadOnlyList<ShellNavigationItem> Create(string? role, string currentUri)
    {
        var currentPath = GetCurrentPath(currentUri);
        return Items
            .Where(item => item.Group != ShellNavigationGroup.Account || CanManageAccount(role))
            .Select(item =>
                {
                    var href = AppUrls.ToAbsolute(item.Path);
                    return new ShellNavigationItem(item.Group, item.Target, href, string.Equals(href, currentPath, StringComparison.Ordinal));
                }
            )
            .ToArray();
    }

    // The part of a document URI that decides which item is current: the path without a trailing slash, query or fragment
    public static string GetCurrentPath(string uri)
    {
        var path = Uri.TryCreate(uri, UriKind.Absolute, out var absolute) ? absolute.AbsolutePath : uri.Split('?', '#')[0];
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }
}
