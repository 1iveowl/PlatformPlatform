// The account detail's tabs and their place in the URL. The tab is the React back office's search parameter tab with its
// values (overview, users, feature-flags), Overview is the default and is left out of the URL, and any other value falls back
// to Overview: invoices and billing-events included, whose tabs the Blazor back office does not show yet.

using System.Globalization;
using Blazor.Client.Components.Lists;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Accounts;

public enum AccountDetailTab
{
    Overview,
    Users,
    FeatureFlags
}

public sealed record AccountDetailTabLink(AccountDetailTab Tab, string Label, string Href, bool IsCurrent, string TestId);

public static class AccountDetailTabs
{
    public const string TabParameter = "tab";

    public static readonly IReadOnlyList<AccountDetailTab> Tabs = [AccountDetailTab.Overview, AccountDetailTab.Users, AccountDetailTab.FeatureFlags];

    public static string ToValue(AccountDetailTab tab)
    {
        return tab switch
        {
            AccountDetailTab.Users => "users",
            AccountDetailTab.FeatureFlags => "feature-flags",
            _ => "overview"
        };
    }

    // Exact values only, as the React router's schema accepts them; anything else is Overview
    public static AccountDetailTab Parse(string? value)
    {
        return Tabs.FirstOrDefault(tab => string.Equals(ToValue(tab), value?.Trim(), StringComparison.Ordinal));
    }

    // The tab the URL names; the last tab parameter wins, as the React router reads it
    public static AccountDetailTab FromUri(string uri)
    {
        var value = DataListQueryString.Decode(new Uri(uri).Query).LastOrDefault(pair => pair.Name == TabParameter).Value;
        return Parse(value);
    }

    // The account's detail page below the back office, keyed by the tenant id as in the React back office
    public static string AccountUrl(TenantId tenantId)
    {
        return BackOfficeUrls.ToAbsolute($"accounts/{tenantId.Value.ToString(CultureInfo.InvariantCulture)}");
    }

    // The account's page on the tab, with nothing else in its query: another tab's list state is not carried across
    public static string ToUrl(TenantId tenantId, AccountDetailTab tab)
    {
        return tab == AccountDetailTab.Overview ? AccountUrl(tenantId) : $"{AccountUrl(tenantId)}?{TabParameter}={ToValue(tab)}";
    }

    public static IReadOnlyList<AccountDetailTabLink> Links(TenantId tenantId, AccountDetailTab current)
    {
        return Tabs.Select(tab => new AccountDetailTabLink(tab, Label(tab), ToUrl(tenantId, tab), tab == current, $"account-tab-{ToValue(tab)}")).ToArray();
    }

    public static string Label(AccountDetailTab tab)
    {
        return tab switch
        {
            AccountDetailTab.Users => CommonStrings.Users,
            AccountDetailTab.FeatureFlags => BackOfficeStrings.FeatureFlags,
            _ => BackOfficeStrings.Overview
        };
    }
}
