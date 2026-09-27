// The user detail's tabs and their place in the URL. The tab is the React back office's search parameter tab with its values
// (overview for Accounts, logins, sessions, identity, feature-flags), Accounts is the default and is left out of the URL, and
// any other value falls back to Accounts.

using Blazor.Client.Components.Lists;
using SharedKernel.Domain;

namespace Blazor.Client.BackOffice.Users;

public enum UserDetailTab
{
    Accounts,
    Logins,
    Sessions,
    Identity,
    FeatureFlags
}

public sealed record UserDetailTabLink(UserDetailTab Tab, string Label, string Href, bool IsCurrent, string TestId);

public static class UserDetailTabs
{
    public const string TabParameter = "tab";

    // The React tab strip's order
    public static readonly IReadOnlyList<UserDetailTab> Tabs = [UserDetailTab.Accounts, UserDetailTab.Logins, UserDetailTab.Sessions, UserDetailTab.Identity, UserDetailTab.FeatureFlags];

    public static string ToValue(UserDetailTab tab)
    {
        return tab switch
        {
            UserDetailTab.Logins => "logins",
            UserDetailTab.Sessions => "sessions",
            UserDetailTab.Identity => "identity",
            UserDetailTab.FeatureFlags => "feature-flags",
            _ => "overview"
        };
    }

    // Exact values only, as the React router's schema accepts them; anything else is Accounts
    public static UserDetailTab Parse(string? value)
    {
        return Tabs.FirstOrDefault(tab => string.Equals(ToValue(tab), value?.Trim(), StringComparison.Ordinal));
    }

    // The tab the URL names; the last tab parameter wins, as the React router reads it
    public static UserDetailTab FromUri(string uri)
    {
        var value = DataListQueryString.Decode(new Uri(uri).Query).LastOrDefault(pair => pair.Name == TabParameter).Value;
        return Parse(value);
    }

    // The user's page on the tab, with nothing else in its query: another tab's list state is not carried across
    public static string ToUrl(UserId userId, UserDetailTab tab)
    {
        var userUrl = BackOfficeUsersListSource.UserUrl(userId);
        return tab == UserDetailTab.Accounts ? userUrl : $"{userUrl}?{TabParameter}={ToValue(tab)}";
    }

    public static IReadOnlyList<UserDetailTabLink> Links(UserId userId, UserDetailTab current)
    {
        return Tabs.Select(tab => new UserDetailTabLink(tab, Label(tab), ToUrl(userId, tab), tab == current, $"user-tab-{ToValue(tab)}")).ToArray();
    }

    public static string Label(UserDetailTab tab)
    {
        return tab switch
        {
            UserDetailTab.Logins => BackOfficeStrings.Logins,
            UserDetailTab.Sessions => AccountStrings.Sessions,
            UserDetailTab.Identity => BackOfficeStrings.IdentityTab,
            UserDetailTab.FeatureFlags => BackOfficeStrings.FeatureFlags,
            _ => BackOfficeStrings.Accounts
        };
    }
}
