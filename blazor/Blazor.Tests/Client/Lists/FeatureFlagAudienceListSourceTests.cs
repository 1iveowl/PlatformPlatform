using System.Net;
using System.Text;
using Account.Client;
using Account.Features.BackOffice.Queries;
using Account.Features.FeatureFlags.Domain;
using Account.Features.FeatureFlags.Queries;
using Blazor.Client.BackOffice.FeatureFlags;
using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

// The flag detail's two override lists: the tenants list under the tenants prefix and the users list under the users prefix,
// both with React's parameter names, each sending the query its endpoint binds, and neither touching the other's parameters,
// so the two filter and page independently and a reload restores both. The switch and the offer follow React's rules.
public sealed class FeatureFlagAudienceListSourceTests
{
    private const string TenantsBody = """
                                       {"totalCount":1,"pageSize":25,"totalPages":1,"currentPageOffset":0,"enabledCount":1,"disabledCount":0,"overrideCount":1,"tenants":[{"id":"42",
                                        "name":"Acme","logoUrl":null,"plan":"Standard","monthlyRecurringRevenue":null,"scheduledPriceAmount":null,"currency":null,"renewalDate":null,
                                        "plannedChange":null,"hasEverSubscribed":false,"country":null,"createdAt":"2026-01-01T00:00:00+00:00","owner":null,"rolloutBucket":12,
                                        "isEnabled":true,"source":"manual_override","inclusionThresholdPercentage":null,"defaultEnabled":false,"overrideEnabledAt":null,
                                        "overrideDisabledAt":null,"tenantAbInclusionPin":null}]}
                                       """;

    private const string UsersBody = """
                                     {"totalCount":1,"pageSize":25,"totalPages":1,"currentPageOffset":0,"enabledCount":0,"disabledCount":1,"overrideCount":0,"users":[{
                                      "id":"usr_01JMVAW4T4320KJ3A7EJMCG8R0","tenantId":"42","email":"ann@example.com","firstName":"Ann","lastName":null,"avatarUrl":null,"role":"Owner",
                                      "lastSeenAt":null,"createdAt":"2026-01-01T00:00:00+00:00","tenantName":"Acme","tenantPlan":"Basis","rolloutBucket":3,"isEnabled":false,
                                      "source":"ab_rollout","inclusionThresholdPercentage":40,"defaultEnabled":false,"overrideEnabledAt":null,"overrideDisabledAt":null,
                                      "userAbInclusionPin":null}]}
                                     """;

    private static readonly FeatureFlagInfo AccountFlag = FeatureFlagsListSourceTests.CreateFlag("account-overview") with { IsKillSwitchEnabled = true };

    private static readonly FeatureFlagInfo PlanFlag = FeatureFlagsListSourceTests.CreateFlag("sso") with { RequiredPlan = "Standard" };

    private static readonly FeatureFlagInfo UserAbTest = FeatureFlagsListSourceTests.CreateFlag("experimental-ui") with
    {
        Scope = FeatureFlagScope.User, IsAbTestEligible = true, IsKillSwitchEnabled = true
    };

    private static readonly MeResponse Admin = new("Admin", "admin@example.com", true, ["admins"]);

    private static readonly MeResponse User = new("User", "user@example.com", false, ["users"]);

    public static TheoryData<Dictionary<string, string>, string, SortOrder, int, string[]> TenantQueryCases => new()
    {
        { new Dictionary<string, string>(), "Name", SortOrder.Ascending, 0, ["State=Enabled", "OrderBy=Name", "SortOrder=Ascending", "PageSize=25"] },
        {
            new Dictionary<string, string>
            {
                ["tenantsSearch"] = "acme", ["tenantsPlans"] = """["Premium","Basis"]""", ["tenantsState"] = "All", ["tenantsHasOverride"] = "true"
            },
            "Plan", SortOrder.Descending, 2,
            ["Search=acme", "Plans=Premium", "Plans=Basis", "HasOverride=true", "OrderBy=Plan", "SortOrder=Descending", "PageOffset=2", "PageSize=25"]
        },
        {
            new Dictionary<string, string> { ["tenantsState"] = "Disabled" }, "Name", SortOrder.Ascending, 0,
            ["State=Disabled", "OrderBy=Name", "SortOrder=Ascending", "PageSize=25"]
        }
    };

    [Theory]
    [MemberData(nameof(TenantQueryCases))]
    public async Task FetchTenants_ShouldSendTheFlagTenantsQuery(Dictionary<string, string> filters, string orderBy, SortOrder sortOrder, int pageOffset, string[] expectedParameters)
    {
        // Arrange
        var network = new RecordingNetwork(TenantsBody);
        var request = new DataListRequest(filters, orderBy, sortOrder, pageOffset, DataListController<FeatureFlagTenantInfo>.PageSize);

        // Act
        var result = await FeatureFlagTenantsListSource.FetchAsync(CreateClient(network), AccountFlag, request, CancellationToken.None);

        // Assert
        var tenant = result.Page!.Items.Should().ContainSingle().Subject;
        tenant.Source.Should().Be(FeatureFlagSource.Manual);
        tenant.Id.Should().Be(new TenantId(42));
        var sent = network.Requests.Should().ContainSingle().Subject;
        sent.AbsolutePath.Should().Be("/api/back-office/feature-flags/account-overview/tenants");
        sent.Query.TrimStart('?').Split('&').Should().BeEquivalentTo(expectedParameters);
    }

    [Fact]
    public async Task FetchTenants_WhenThePlanManagedFlagHasNoPlanChosen_ShouldAskForTheRequiredPlanAndEveryPlanAbove()
    {
        // Arrange
        var network = new RecordingNetwork(TenantsBody);
        var filters = new Dictionary<string, string> { ["tenantsState"] = "Disabled", ["tenantsHasOverride"] = "true" };
        var request = new DataListRequest(filters, FeatureFlagTenantsListSource.PlanManagedOrderBy, SortOrder.Ascending, 0, 25);

        // Act
        await FeatureFlagTenantsListSource.FetchAsync(CreateClient(network), PlanFlag, request, CancellationToken.None);

        // Assert
        network.Requests.Should().ContainSingle().Which.Query.TrimStart('?').Split('&').Should().BeEquivalentTo(
            "Plans=Standard", "Plans=Premium", "OrderBy=Name", "SortOrder=Ascending", "PageSize=25"
        );
    }

    [Fact]
    public async Task FetchUsers_ShouldSendTheFlagUsersQuery()
    {
        // Arrange
        var network = new RecordingNetwork(UsersBody);
        var filters = new Dictionary<string, string> { ["usersSearch"] = "ann", ["usersRoles"] = """["Owner"]""", ["usersState"] = "All" };
        var request = new DataListRequest(filters, "InclusionThresholdPercentage", SortOrder.Ascending, 1, 25);

        // Act
        var result = await FeatureFlagUsersListSource.FetchAsync(CreateClient(network), UserAbTest.Key, request, CancellationToken.None);

        // Assert
        var user = result.Page!.Items.Should().ContainSingle().Subject;
        user.Source.Should().Be(FeatureFlagSource.AbRollout);
        user.TenantId.Should().Be(new TenantId(42));
        var sent = network.Requests.Should().ContainSingle().Subject;
        sent.AbsolutePath.Should().Be("/api/back-office/feature-flags/experimental-ui/users");
        sent.Query.TrimStart('?').Split('&').Should().BeEquivalentTo(
            "Search=ann", "Roles=Owner", "OrderBy=InclusionThresholdPercentage", "SortOrder=Ascending", "PageOffset=1", "PageSize=25"
        );
    }

    [Fact]
    public void NormalizeFilters_WhenValuesAreMalformed_ShouldKeepOnlyValidValuesInCanonicalForm()
    {
        // Arrange
        var tenantFilters = new Dictionary<string, string>
        {
            ["tenantsSearch"] = "  acme ", ["tenantsPlans"] = """["basis","Gold","Premium"]""", ["tenantsState"] = "enabled", ["tenantsHasOverride"] = "yes"
        };
        var userFilters = new Dictionary<string, string> { ["usersRoles"] = """["member","Owner"]""", ["usersState"] = "disabled", ["usersHasOverride"] = "TRUE" };

        // Act
        var tenants = FeatureFlagTenantsListSource.NormalizeFilters(tenantFilters);
        var users = FeatureFlagUsersListSource.NormalizeFilters(userFilters);

        // Assert
        tenants.Should().BeEquivalentTo(new Dictionary<string, string> { ["tenantsSearch"] = "acme", ["tenantsPlans"] = """["Premium","Basis"]""" });
        users.Should().BeEquivalentTo(new Dictionary<string, string> { ["usersRoles"] = """["Owner","Member"]""", ["usersState"] = "Disabled", ["usersHasOverride"] = "true" });
    }

    [Fact]
    public void Options_WithBothPrefixes_ShouldEachWriteOnlyTheirOwnParameters()
    {
        // Arrange
        var tenantOptions = new DataListUrlOptions(FeatureFlagTenantsListSource.DefaultOrderBy(AccountFlag), ["Name", "Plan", "OverrideUpdatedAt", "IsEnabled"],
            FeatureFlagTenantsListSource.FilterParameters, parameterPrefix: FeatureFlagTenantsListSource.ParameterPrefix, normalizeFilters: FeatureFlagTenantsListSource.NormalizeFilters
        );
        var userOptions = new DataListUrlOptions(FeatureFlagUsersListSource.DefaultOrderBy(UserAbTest), ["Name", "InclusionThresholdPercentage", "IsEnabled"],
            FeatureFlagUsersListSource.FilterParameters, parameterPrefix: FeatureFlagUsersListSource.ParameterPrefix, normalizeFilters: FeatureFlagUsersListSource.NormalizeFilters
        );
        const string uri = "https://back-office.dev.localhost:9001/blazor/back-office/feature-flags/x?tenantsSearch=acme&tenantsPageOffset=1&usersState=All&usersPageOffset=2";

        // Act
        var tenants = DataListState.Parse(uri, tenantOptions);
        var users = DataListState.Parse(uri, userOptions);
        var tenantsPaged = (tenants with { PageOffset = 3 }).ToUri(uri, tenantOptions);
        var usersSorted = (users with { OrderBy = "Name" }).ToUri(uri, userOptions);

        // Assert
        tenants.Filters.Should().BeEquivalentTo(new Dictionary<string, string> { ["tenantsSearch"] = "acme" });
        tenants.PageOffset.Should().Be(1);
        users.Filters.Should().BeEquivalentTo(new Dictionary<string, string> { ["usersState"] = "All" });
        users.PageOffset.Should().Be(2);
        tenantsPaged.Should().Contain("tenantsPageOffset=3").And.Contain("usersState=All").And.Contain("usersPageOffset=2");
        usersSorted.Should().Contain("usersOrderBy=Name").And.Contain("tenantsSearch=acme").And.Contain("tenantsPageOffset=1");
    }

    [Theory]
    [InlineData(true, false, "InclusionThresholdPercentage")]
    [InlineData(false, false, "Name")]
    [InlineData(false, true, FeatureFlagTenantsListSource.PlanManagedOrderBy)]
    public void DefaultOrderBy_ShouldFollowTheFlagsKind(bool isAbTest, bool isPlanManaged, string expected)
    {
        // Arrange
        var flag = AccountFlag with { IsAbTestEligible = isAbTest, RequiredPlan = isPlanManaged ? "Premium" : null };

        // Act & Assert
        FeatureFlagTenantsListSource.DefaultOrderBy(flag).Should().Be(expected);
    }

    [Fact]
    public void ListIds_ShouldBeDistinctPerFlagAndPerList()
    {
        // Act & Assert
        FeatureFlagTenantsListSource.ListId("a").Should().NotBe(FeatureFlagTenantsListSource.ListId("b"));
        FeatureFlagTenantsListSource.ListId("a").Should().NotBe(FeatureFlagUsersListSource.ListId("a"));
    }

    [Theory]
    [InlineData(null, FeatureFlagStateFilter.Enabled)]
    [InlineData("All", FeatureFlagStateFilter.All)]
    [InlineData("disabled", FeatureFlagStateFilter.Disabled)]
    [InlineData("Unknown", FeatureFlagStateFilter.Enabled)]
    public void ParseState_ShouldFallBackToEnabled(string? value, FeatureFlagStateFilter expected)
    {
        // Act & Assert
        FeatureFlagOverrides.ParseState(value).Should().Be(expected);
    }

    [Fact]
    public void SetState_ShouldLeaveTheDefaultOutOfTheUrl()
    {
        // Act & Assert
        FeatureFlagTenantsListSource.SetState(FeatureFlagStateFilter.Enabled)["tenantsState"].Should().BeNull();
        FeatureFlagUsersListSource.SetState(FeatureFlagStateFilter.All)["usersState"].Should().Be("All");
    }

    [Theory]
    [InlineData(true, FeatureFlagSource.Manual, false, false, FeatureFlagOverrideChange.Remove)]
    [InlineData(true, FeatureFlagSource.Manual, true, false, FeatureFlagOverrideChange.Disable)]
    [InlineData(true, FeatureFlagSource.AbRollout, true, true, FeatureFlagOverrideChange.Disable)]
    [InlineData(false, FeatureFlagSource.Manual, false, false, FeatureFlagOverrideChange.Enable)]
    [InlineData(false, FeatureFlagSource.Manual, true, false, FeatureFlagOverrideChange.Disable)]
    [InlineData(false, FeatureFlagSource.Default, false, false, FeatureFlagOverrideChange.Enable)]
    public void GetSwitchChange_ShouldFollowReactsCycle(bool isAbTest, FeatureFlagSource source, bool isEnabled, bool defaultEnabled, FeatureFlagOverrideChange expected)
    {
        // Act & Assert
        FeatureFlagOverrides.GetSwitchChange(isAbTest, source, isEnabled, defaultEnabled).Should().Be(expected);
    }

    [Fact]
    public void CanSetOverrides_ShouldOfferWritesOnlyToAdminsOnADeclaredFlagThatIsNotPlanManaged()
    {
        // Act & Assert
        FeatureFlagOverrides.CanSetOverrides(Admin, AccountFlag).Should().BeTrue();
        FeatureFlagOverrides.CanSetOverrides(Admin, UserAbTest).Should().BeTrue();
        FeatureFlagOverrides.CanSetOverrides(User, AccountFlag).Should().BeFalse();
        FeatureFlagOverrides.CanSetOverrides(null, AccountFlag).Should().BeFalse();
        FeatureFlagOverrides.CanSetOverrides(Admin, PlanFlag).Should().BeFalse();
        FeatureFlagOverrides.CanSetOverrides(Admin, AccountFlag with { OrphanedAt = DateTimeOffset.UnixEpoch }).Should().BeFalse();
        FeatureFlagOverrides.CanSetOverrides(Admin, AccountFlag with { DeletedAt = DateTimeOffset.UnixEpoch }).Should().BeFalse();
    }

    [Fact]
    public void OverrideRoutes_ShouldNameTheFlagAndTheRowAsTheEndpointsBindThem()
    {
        // Act & Assert
        AccountApiRoutes.BackOfficeTenantFeatureFlagOverride("account-overview").Should().Be("/api/back-office/feature-flags/account-overview/tenant-override");
        AccountApiRoutes.RemoveBackOfficeTenantFeatureFlagOverride("account-overview", new TenantId(42))
            .Should().Be("/api/back-office/feature-flags/account-overview/tenant-override?tenantId=42");
        AccountApiRoutes.RemoveBackOfficeUserFeatureFlagOverride("compact-view", new UserId("usr_01JMVAW4T4320KJ3A7EJMCG8R0"), new TenantId(42))
            .Should().Be("/api/back-office/feature-flags/compact-view/user-override?userId=usr_01JMVAW4T4320KJ3A7EJMCG8R0&tenantId=42");
    }

    private static BackOfficeClient CreateClient(HttpMessageHandler network)
    {
        return new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });
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
