// The navigation the back office's sidebar and mobile menu render, matching the React back office's side menu: Navigation
// (Dashboard, Accounts, Users), Billing (Invoices, Billing events) only when the subscription setting is on, and Platform
// (Feature flags). The React back office's Developer group (the components showcase) is not ported. An item is current on
// its own page and on the pages below it, so an account's detail page marks Accounts.

using Blazor.Client.Shell;

namespace Blazor.Client.BackOffice.Shell;

public enum BackOfficeNavigationGroup
{
    Navigation,
    Billing,
    Platform
}

public enum BackOfficeNavigationTarget
{
    Dashboard,
    Accounts,
    Users,
    Invoices,
    BillingEvents,
    FeatureFlags
}

public sealed record BackOfficeNavigationItem(BackOfficeNavigationGroup Group, BackOfficeNavigationTarget Target, string Href, bool IsCurrent);

public static class BackOfficeNavigation
{
    private static readonly (BackOfficeNavigationGroup Group, BackOfficeNavigationTarget Target, string Page)[] Items =
    [
        (BackOfficeNavigationGroup.Navigation, BackOfficeNavigationTarget.Dashboard, ""),
        (BackOfficeNavigationGroup.Navigation, BackOfficeNavigationTarget.Accounts, "accounts"),
        (BackOfficeNavigationGroup.Navigation, BackOfficeNavigationTarget.Users, "users"),
        (BackOfficeNavigationGroup.Billing, BackOfficeNavigationTarget.Invoices, "invoices"),
        (BackOfficeNavigationGroup.Billing, BackOfficeNavigationTarget.BillingEvents, "billing-events"),
        (BackOfficeNavigationGroup.Platform, BackOfficeNavigationTarget.FeatureFlags, "feature-flags")
    ];

    // currentUri is the absolute document URI from NavigationManager.Uri; the query and fragment do not change the current item
    public static IReadOnlyList<BackOfficeNavigationItem> Create(bool isSubscriptionEnabled, string currentUri)
    {
        var currentPath = ShellNavigation.GetCurrentPath(currentUri);
        return Items
            .Where(item => item.Group != BackOfficeNavigationGroup.Billing || isSubscriptionEnabled)
            .Select(item =>
                {
                    var href = item.Page.Length == 0 ? BackOfficeUrls.Home : BackOfficeUrls.ToAbsolute(item.Page);
                    return new BackOfficeNavigationItem(item.Group, item.Target, href, IsCurrent(item.Target, href, currentPath));
                }
            )
            .ToArray();
    }

    private static bool IsCurrent(BackOfficeNavigationTarget target, string href, string currentPath)
    {
        if (string.Equals(href, currentPath, StringComparison.Ordinal)) return true;

        // The dashboard is the back office's home, so every page is below it; only the other items own their subpages
        return target != BackOfficeNavigationTarget.Dashboard && currentPath.StartsWith($"{href}/", StringComparison.Ordinal);
    }
}
