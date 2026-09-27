using System.Globalization;
using Account.Features.Authentication.Domain;
using Account.Features.FeatureFlags.Domain;
using Account.Features.FeatureFlags.Queries;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Users.BackOffice.Queries;
using Account.Features.Users.Domain;
using Blazor.Client.BackOffice.Users;
using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.BackOffice;

// The user detail's tab model and the texts of its tabs. The tab is the React back office's tab parameter with its values,
// Accounts when absent or unknown, including the identity tab the Blazor back office does not show yet. The in-memory lists
// page the response they were given, and the badges read as the React back office's do.
public sealed class UserDetailTests
{
    private const string UserUrl = "https://back-office.dev.localhost:9001/blazor/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0";

    private static readonly UserId SampleUserId = new("usr_01JMVAW4T4320KJ3A7EJMCG8R0");

    [Theory]
    [InlineData("", UserDetailTab.Accounts)]
    [InlineData("?tab=overview", UserDetailTab.Accounts)]
    [InlineData("?tab=logins", UserDetailTab.Logins)]
    [InlineData("?tab=sessions", UserDetailTab.Sessions)]
    [InlineData("?tab=feature-flags", UserDetailTab.FeatureFlags)]
    [InlineData("?sessionsPageOffset=1&tab=sessions", UserDetailTab.Sessions)]
    [InlineData("?tab=identity", UserDetailTab.Accounts)]
    [InlineData("?tab=Logins", UserDetailTab.Accounts)]
    [InlineData("?tab=", UserDetailTab.Accounts)]
    [InlineData("?tab=logins&tab=feature-flags", UserDetailTab.FeatureFlags)]
    public void FromUri_ShouldReadTheTabParameterWithAccountsAsTheFallback(string query, UserDetailTab expected)
    {
        // Act
        var tab = UserDetailTabs.FromUri($"{UserUrl}{query}");

        // Assert
        tab.Should().Be(expected);
    }

    [Fact]
    public void Links_ShouldListTheFourTabsInTheReactOrderWithAccountsLeftOutOfTheUrl()
    {
        // Act
        var links = UserDetailTabs.Links(SampleUserId, UserDetailTab.Sessions);

        // Assert
        links.Select(link => link.Tab).Should().Equal(UserDetailTab.Accounts, UserDetailTab.Logins, UserDetailTab.Sessions, UserDetailTab.FeatureFlags);
        links.Select(link => link.Href).Should().Equal(
            "/blazor/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0",
            "/blazor/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0?tab=logins",
            "/blazor/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0?tab=sessions",
            "/blazor/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0?tab=feature-flags"
        );
        links.Single(link => link.IsCurrent).Tab.Should().Be(UserDetailTab.Sessions);
        links.Select(link => link.TestId).Should().Equal("user-tab-overview", "user-tab-logins", "user-tab-sessions", "user-tab-feature-flags");
    }

    [Fact]
    public void Tab_WhenWrittenAndReadBack_ShouldRoundTripThroughTheUrl()
    {
        foreach (var tab in UserDetailTabs.Tabs)
        {
            // Act
            var read = UserDetailTabs.FromUri($"https://back-office.dev.localhost:9001{UserDetailTabs.ToUrl(SampleUserId, tab)}");

            // Assert
            read.Should().Be(tab);
        }
    }

    [Theory]
    [InlineData("en-US", UserDetailTab.Logins, "Logins")]
    [InlineData("da-DK", UserDetailTab.Sessions, "Sessioner")]
    [InlineData("da-DK", UserDetailTab.Accounts, "Konti")]
    public void Label_ShouldNameTheTabInTheCurrentCulture(string culture, UserDetailTab tab, string expected)
    {
        // Arrange
        using var scope = new CultureScope(culture);

        // Act
        var label = UserDetailTabs.Label(tab);

        // Assert
        label.Should().Be(expected);
    }

    [Theory]
    [InlineData(LoginEventOutcome.Succeeded, "TooManyRetries", "Succeeded")]
    [InlineData(LoginEventOutcome.Failed, "TooManyRetries", "TooManyRetries")]
    [InlineData(LoginEventOutcome.Pending, null, "Pending")]
    [InlineData(LoginEventOutcome.Failed, null, "Failed")]
    public void GetOutcome_ShouldReadAsTheReactOutcomeBadge(LoginEventOutcome outcome, string? failureReason, string expected)
    {
        // Arrange
        using var scope = new CultureScope("en-US");
        var entry = new BackOfficeUserLoginEntry(LoginEventKind.Email, LoginMethod.OneTimePassword, outcome, DateTimeOffset.UnixEpoch, failureReason, null);

        // Act
        var badge = UserDetailFormat.GetOutcome(entry);

        // Assert
        badge.Label.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "en-US", "1 membership")]
    [InlineData(3, "en-US", "3 memberships")]
    [InlineData(2, "da-DK", "2 medlemskaber")]
    public void GetMembershipsLabel_ShouldCountTheMemberships(int count, string culture, string expected)
    {
        // Arrange
        using var scope = new CultureScope(culture);

        // Act
        var label = UserDetailFormat.GetMembershipsLabel(count);

        // Assert
        label.Should().Be(expected);
    }

    [Fact]
    public void GetStatusAndMrr_WhenTheMembershipIsCanceling_ShouldReadAsTheAccountsList()
    {
        // Arrange
        using var scope = new CultureScope("en-US");
        var membership = Membership(SubscriptionPlan.Standard, PlannedSubscriptionChange.Cancellation, 29m, "EUR");

        // Act
        var status = UserDetailFormat.GetStatus(membership);
        var mrr = UserDetailFormat.GetMrr(membership);

        // Assert
        status.Should().Be(TenantStatusFilter.Canceling);
        mrr.Next.Should().NotBeNull();
    }

    [Fact]
    public void GetSessionStatusAndLastSeen_ShouldReadRevokedAndFallBackToTheSignIn()
    {
        // Arrange
        using var scope = new CultureScope("en-US");
        var createdAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        var revoked = Session(createdAt, null, createdAt.AddHours(1));
        var active = Session(createdAt, createdAt.AddMinutes(5), null);

        // Act
        var revokedStatus = UserDetailFormat.GetSessionStatus(revoked);
        var activeStatus = UserDetailFormat.GetSessionStatus(active);

        // Assert
        revokedStatus.Label.Should().Be("Revoked");
        activeStatus.Label.Should().Be("Active");
        UserDetailFormat.GetLastSeen(revoked).Should().Be(createdAt);
        UserDetailFormat.GetLastSeen(active).Should().Be(createdAt.AddMinutes(5));
    }

    [Fact]
    public void LoginsPage_ShouldKeyEachAttemptByItsPositionAndPageInMemory()
    {
        // Arrange
        var entries = Enumerable.Range(0, 30)
            .Select(_ => new BackOfficeUserLoginEntry(LoginEventKind.Email, LoginMethod.OneTimePassword, LoginEventOutcome.Succeeded, DateTimeOffset.UnixEpoch, null, null))
            .ToArray();
        var rows = UserLoginsListSource.ToRows(entries);

        // Act
        var page = UserLoginsListSource.Page(rows, new DataListRequest(new Dictionary<string, string>(), UserLoginsListSource.DefaultOrderBy, SortOrder.Ascending, 1, 25));

        // Assert
        page.Page!.TotalCount.Should().Be(30);
        page.Page.Items.Select(UserLoginsListSource.KeyOf).Should().Equal("25", "26", "27", "28", "29");
        rows.Select(UserLoginsListSource.KeyOf).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ListIds_ShouldCarryTheUserIdSoOneUsersPagesNeverAnswerForAnothers()
    {
        // Arrange
        var otherUserId = new UserId("usr_01JMVAW4T4320KJ3A7EJMCG8R1");

        // Act
        var listIds = new[]
        {
            UserMembershipsListSource.ListId(SampleUserId), UserLoginsListSource.ListId(SampleUserId), UserSessionsListSource.ListId(SampleUserId),
            UserFeatureFlagsListSource.ListId(SampleUserId)
        };

        // Assert
        listIds.Should().OnlyHaveUniqueItems().And.OnlyContain(listId => listId.EndsWith(SampleUserId.Value));
        UserSessionsListSource.ListId(otherUserId).Should().NotBe(UserSessionsListSource.ListId(SampleUserId));
    }

    [Fact]
    public void FeatureFlags_ShouldShowTheInclusionColumnOnlyWithAnAbTestAndNameAManualOverride()
    {
        // Arrange
        var abTest = Flag("beta-features", true, FeatureFlagSource.Manual, 40);
        var plain = Flag("compact-view", false, FeatureFlagSource.Default, null);

        // Act
        var withAbTest = UserFeatureFlagsListSource.ShowsInclusionColumn([abTest, plain]);
        var withoutAbTest = UserFeatureFlagsListSource.ShowsInclusionColumn([plain]);

        // Assert
        withAbTest.Should().BeTrue();
        withoutAbTest.Should().BeFalse();
        UserFeatureFlagsListSource.IsManualOverride(abTest).Should().BeTrue();
        UserFeatureFlagsListSource.IsManualOverride(plain).Should().BeFalse();
        UserFeatureFlagsListSource.GetInclusionThreshold(abTest).Should().Be("40%");
        UserFeatureFlagsListSource.NameOf(Flag("not-in-registry", false, FeatureFlagSource.Default, null)).Should().Be("not-in-registry");
    }

    private static BackOfficeUserTenantMembership Membership(SubscriptionPlan plan, PlannedSubscriptionChange? plannedChange, decimal? mrr, string? currency)
    {
        return new BackOfficeUserTenantMembership(SampleUserId, new TenantId(42), "Acme", null, plan, plannedChange, true, mrr, null, currency, null, "DK", UserRole.Owner, true,
            DateTimeOffset.UnixEpoch, null
        );
    }

    private static BackOfficeUserSession Session(DateTimeOffset createdAt, DateTimeOffset? lastActiveAt, DateTimeOffset? revokedAt)
    {
        return new BackOfficeUserSession(new SessionId("sess_01JMVAW4T4320KJ3A7EJMCG8R0"), new TenantId(42), "Acme", null, LoginMethod.OneTimePassword, DeviceType.Desktop, "", "127.0.0.1",
            createdAt, lastActiveAt, revokedAt, revokedAt is null ? null : SessionRevokedReason.Revoked, createdAt.AddDays(30)
        );
    }

    private static UserFeatureFlagInfo Flag(string key, bool isAbTest, FeatureFlagSource source, int? inclusionThreshold)
    {
        return new UserFeatureFlagInfo(key, FeatureFlagScope.User, "", isAbTest, null, null, null, true, source, true, 3, new TenantId(42), inclusionThreshold, false, null);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string locale)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }
}
