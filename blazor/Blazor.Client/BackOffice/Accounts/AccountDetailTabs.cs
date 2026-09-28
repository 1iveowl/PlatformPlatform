// The account detail's tabs and their place in the URL. The tab is the React back office's search parameter tab with its
// values (overview, users, invoices, billing-events, feature-flags), Overview is the default and is left out of the URL, and
// any other value falls back to Overview. The two billing tabs exist only with the subscription setting on; with it off their
// values fall back to Overview too, as the React account page does.

using System.Globalization;
using Blazor.Client.Components.Lists;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Accounts;

public enum AccountDetailTab
{
    Overview,
    Users,
    Invoices,
    BillingEvents,
    FeatureFlags
}

public sealed record AccountDetailTabLink(AccountDetailTab Tab, string Label, string Href, bool IsCurrent, string TestId);

public static class AccountDetailTabs
{
    public const string TabParameter = "tab";

    public static readonly IReadOnlyList<AccountDetailTab> Tabs =
        [AccountDetailTab.Overview, AccountDetailTab.Users, AccountDetailTab.Invoices, AccountDetailTab.BillingEvents, AccountDetailTab.FeatureFlags];

    // The tabs shown: the billing tabs only with the subscription setting on
    public static IReadOnlyList<AccountDetailTab> Available(bool isSubscriptionEnabled)
    {
        return isSubscriptionEnabled ? Tabs : Tabs.Where(tab => !IsBillingTab(tab)).ToArray();
    }

    public static bool IsBillingTab(AccountDetailTab tab)
    {
        return tab is AccountDetailTab.Invoices or AccountDetailTab.BillingEvents;
    }

    public static string ToValue(AccountDetailTab tab)
    {
        return tab switch
        {
            AccountDetailTab.Users => "users",
            AccountDetailTab.Invoices => "invoices",
            AccountDetailTab.BillingEvents => "billing-events",
            AccountDetailTab.FeatureFlags => "feature-flags",
            _ => "overview"
        };
    }

    // Exact values only, as the React router's schema accepts them; anything else, or a tab not shown, is Overview
    public static AccountDetailTab Parse(string? value, bool isSubscriptionEnabled)
    {
        return Available(isSubscriptionEnabled).FirstOrDefault(tab => string.Equals(ToValue(tab), value?.Trim(), StringComparison.Ordinal));
    }

    // The tab the URL names; the last tab parameter wins, as the React router reads it
    public static AccountDetailTab FromUri(string uri, bool isSubscriptionEnabled)
    {
        var value = DataListQueryString.Decode(new Uri(uri).Query).LastOrDefault(pair => pair.Name == TabParameter).Value;
        return Parse(value, isSubscriptionEnabled);
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

    public static IReadOnlyList<AccountDetailTabLink> Links(TenantId tenantId, AccountDetailTab current, bool isSubscriptionEnabled)
    {
        return Available(isSubscriptionEnabled).Select(tab => new AccountDetailTabLink(tab, Label(tab), ToUrl(tenantId, tab), tab == current, $"account-tab-{ToValue(tab)}")).ToArray();
    }

    public static string Label(AccountDetailTab tab)
    {
        return tab switch
        {
            AccountDetailTab.Users => CommonStrings.Users,
            AccountDetailTab.Invoices => BackOfficeStrings.Invoices,
            AccountDetailTab.BillingEvents => BackOfficeStrings.BillingEvents,
            AccountDetailTab.FeatureFlags => BackOfficeStrings.FeatureFlags,
            _ => BackOfficeStrings.Overview
        };
    }
}
