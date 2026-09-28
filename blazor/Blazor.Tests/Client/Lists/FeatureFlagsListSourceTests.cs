using System.Net;
using System.Text;
using Account.Client;
using Account.Features.FeatureFlags.Queries;
using Blazor.Client.BackOffice.FeatureFlags;
using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.FeatureFlags;
using SharedKernel.Localization;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

// The back office's feature flag list: one read of GET /api/back-office/feature-flags, with IncludeDeleted as the show-deleted
// toggle sets it, grouped into account, plan, user and system flags in React's order, each group paged in memory for its own
// list. A row opens the flag's detail page except in the system group, and the status and rollout columns follow the flag.
public sealed class FeatureFlagsListSourceTests
{
    [Theory]
    [InlineData(false, "?IncludeDeleted=false")]
    [InlineData(true, "?IncludeDeleted=true")]
    public async Task GetFeatureFlagsAsync_ShouldSendTheShowDeletedToggleAsIncludeDeleted(bool includeDeleted, string expectedQuery)
    {
        // Arrange
        var network = new RecordingNetwork("""{"flags":[]}""");
        var backOfficeClient = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await backOfficeClient.GetFeatureFlagsAsync(includeDeleted, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var request = network.Requests.Should().ContainSingle().Which;
        request.AbsolutePath.Should().Be("/api/back-office/feature-flags");
        request.Query.Should().Be(expectedQuery);
    }

    [Fact]
    public async Task GetGroups_ShouldGroupTheAccountApiResponseInReactsOrderAndLeaveOutAnEmptyGroup()
    {
        // Arrange
        var network = new RecordingNetwork("""
                                           {"flags":[
                                            {"key":"google-oauth","scope":"System","adminLevel":"SystemAdmin","description":"Google","isAbTestEligible":false,"configurableByTenant":false,
                                             "configurableByUser":false,"requiredPlan":null,"createdAt":null,"enabledAt":null,"disabledAt":null,"rolloutBucketStart":null,"rolloutBucketEnd":null,
                                             "rolloutPercentage":null,"isActive":true,"isKillSwitchEnabled":false,"isStableModule":false,"orphanedAt":null,"deletedAt":null},
                                            {"key":"experimental-ui","scope":"User","adminLevel":"SystemAdmin","description":"Experimental","isAbTestEligible":true,"configurableByTenant":false,
                                             "configurableByUser":false,"requiredPlan":null,"createdAt":"2026-09-01T10:00:00+00:00","enabledAt":null,"disabledAt":null,"rolloutBucketStart":10,
                                             "rolloutBucketEnd":51,"rolloutPercentage":42,"isActive":false,"isKillSwitchEnabled":true,"isStableModule":false,"orphanedAt":null,"deletedAt":null},
                                            {"key":"sso","scope":"Tenant","adminLevel":"SystemAdmin","description":"Single sign-on","isAbTestEligible":false,"configurableByTenant":false,
                                             "configurableByUser":false,"requiredPlan":"Premium","createdAt":null,"enabledAt":null,"disabledAt":null,"rolloutBucketStart":null,"rolloutBucketEnd":null,
                                             "rolloutPercentage":null,"isActive":true,"isKillSwitchEnabled":false,"isStableModule":false,"orphanedAt":null,"deletedAt":null}]}
                                           """
        );
        var backOfficeClient = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });

        // Act
        var result = await backOfficeClient.GetFeatureFlagsAsync(false, CancellationToken.None);

        // Assert
        var flags = result.Value!.Flags;
        FeatureFlagsListSource.GetGroups(flags).Should().Equal(FeatureFlagGroup.Plan, FeatureFlagGroup.User, FeatureFlagGroup.System);
        FeatureFlagsListSource.GetGroup(flags, FeatureFlagGroup.Plan).Should().ContainSingle().Which.Key.Should().Be("sso");
        FeatureFlagsListSource.GetGroup(flags, FeatureFlagGroup.User).Should().ContainSingle().Which.Key.Should().Be("experimental-ui");
        FeatureFlagsListSource.GetGroup(flags, FeatureFlagGroup.System).Should().ContainSingle().Which.Key.Should().Be("google-oauth");
        FeatureFlagsListSource.GetGroup(flags, FeatureFlagGroup.Account).Should().BeEmpty();
    }

    [Theory]
    [InlineData(FeatureFlagScope.Tenant, null, FeatureFlagGroup.Account)]
    [InlineData(FeatureFlagScope.Tenant, "Premium", FeatureFlagGroup.Plan)]
    [InlineData(FeatureFlagScope.User, null, FeatureFlagGroup.User)]
    [InlineData(FeatureFlagScope.System, null, FeatureFlagGroup.System)]
    public void GroupOf_ShouldPutATenantFlagWithARequiredPlanInThePlanGroup(FeatureFlagScope scope, string? requiredPlan, FeatureFlagGroup expected)
    {
        // Act
        var group = FeatureFlagsListSource.GroupOf(CreateFlag("some-flag") with { Scope = scope, RequiredPlan = requiredPlan });

        // Assert
        group.Should().Be(expected);
    }

    [Theory]
    [InlineData(FeatureFlagGroup.Account, true, true)]
    [InlineData(FeatureFlagGroup.Plan, true, false)]
    [InlineData(FeatureFlagGroup.User, true, true)]
    [InlineData(FeatureFlagGroup.System, false, false)]
    public void HasDetailAndShowsRollout_ShouldFollowReactsColumnsAndLinks(FeatureFlagGroup group, bool hasDetail, bool showsRollout)
    {
        // Act & Assert
        FeatureFlagsListSource.HasDetail(group).Should().Be(hasDetail);
        FeatureFlagsListSource.ShowsRollout(group).Should().Be(showsRollout);
    }

    [Fact]
    public void GetStatus_ShouldPreferDeletedThenRemovedThenAlwaysOnOverTheActivation()
    {
        // Arrange
        var orphanedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var active = CreateFlag("some-flag") with { IsActive = true };

        // Act & Assert
        FeatureFlagsListSource.GetStatus(active).Should().Be(FeatureFlagStatus.Active);
        FeatureFlagsListSource.GetStatus(active with { IsActive = false }).Should().Be(FeatureFlagStatus.Inactive);
        FeatureFlagsListSource.GetStatus(active with { IsStableModule = true }).Should().Be(FeatureFlagStatus.AlwaysOn);
        FeatureFlagsListSource.GetStatus(active with { IsStableModule = true, OrphanedAt = orphanedAt }).Should().Be(FeatureFlagStatus.Removed);
        FeatureFlagsListSource.GetStatus(active with { OrphanedAt = orphanedAt, DeletedAt = orphanedAt }).Should().Be(FeatureFlagStatus.Deleted);
        FeatureFlagsListSource.GetStatusLabel(FeatureFlagStatus.Removed).Should().Be(BackOfficeStrings.FlagRemoved);
    }

    [Fact]
    public void GetRolloutLabel_ShouldShowAnABTestsPercentageAndNothingForAnyOtherFlag()
    {
        // Arrange
        var abTest = CreateFlag("some-flag") with { IsAbTestEligible = true, RolloutPercentage = 42, IsActive = true };

        // Act & Assert
        FeatureFlagsListSource.GetRolloutLabel(abTest).Should().Be("42%");
        FeatureFlagsListSource.GetRolloutLabel(abTest with { RolloutPercentage = null }).Should().Be("0%");
        FeatureFlagsListSource.GetRolloutLabel(abTest with { IsAbTestEligible = false }).Should().BeNull();
        FeatureFlagsListSource.IsRolloutInEffect(abTest).Should().BeTrue();
        FeatureFlagsListSource.IsRolloutInEffect(abTest with { IsActive = false }).Should().BeFalse();
        FeatureFlagsListSource.IsRolloutInEffect(abTest with { DeletedAt = DateTimeOffset.UnixEpoch }).Should().BeFalse();
    }

    [Fact]
    public void NameOf_ShouldFallBackToTheRegistryLabelAndThenToTheKey()
    {
        // Act & Assert
        FeatureFlagsListSource.NameOf(CreateFlag("sso")).Should().Be("Single sign-on");
        FeatureFlagsListSource.NameOf(CreateFlag("removed-from-code")).Should().Be("removed-from-code");
        FeatureFlagsListSource.DescriptionOf(CreateFlag("removed-from-code") with { Description = "" }).Should().BeNull();
    }

    [Fact]
    public void ListIdAndParameterPrefix_ShouldBeDistinctPerGroupAndPerShowDeleted()
    {
        // Act
        var ids = FeatureFlagsListSource.GroupOrder.SelectMany(group => new[] { FeatureFlagsListSource.ListId(group, false), FeatureFlagsListSource.ListId(group, true) }).ToArray();
        var prefixes = FeatureFlagsListSource.GroupOrder.Select(FeatureFlagsListSource.ParameterPrefix).ToArray();

        // Assert
        ids.Should().OnlyHaveUniqueItems().And.HaveCount(8);
        prefixes.Should().Equal("accountFlags", "planFlags", "userFlags", "systemFlags");
    }

    [Fact]
    public void Page_ShouldReturnTheRequestedPageAndTheTotal()
    {
        // Arrange
        var flags = Enumerable.Range(0, 27).Select(index => CreateFlag($"flag-{index}")).ToArray();
        var request = new DataListRequest(new Dictionary<string, string>(), FeatureFlagsListSource.DefaultOrderBy, SortOrder.Ascending, 1, 25);

        // Act
        var result = FeatureFlagsListSource.Page(flags, request);

        // Assert
        result.Page!.TotalCount.Should().Be(27);
        result.Page.Items.Select(flag => flag.Key).Should().Equal("flag-25", "flag-26");
    }

    [Fact]
    public void DetailUrl_ShouldKeepTheReactBackOfficesPathBelowTheBackOffice()
    {
        // Act & Assert
        FeatureFlagsListSource.DetailUrl("experimental-ui").Should().Be("/blazor/back-office/feature-flags/experimental-ui");
        FeatureFlagsListSource.DetailUrl("a/b").Should().Be("/blazor/back-office/feature-flags/a%2Fb");
    }

    internal static FeatureFlagInfo CreateFlag(string key)
    {
        return new FeatureFlagInfo(key, FeatureFlagScope.Tenant, FeatureFlagAdminLevel.SystemAdmin, "Description", false, false, false, null, null, null, null, null, null, null, false, false, false, null, null);
    }

    private sealed class RecordingNetwork(string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
