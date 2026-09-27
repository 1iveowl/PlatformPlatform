using System.Net;
using System.Text;
using Account.Client;
using Account.Features.Tenants.BackOffice.Queries;
using Account.Features.Users.Domain;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

// The account detail's users list: its parameters carry the users prefix so it can share the page with the feature flag lists,
// the roles are a JSON array of role names in the accounts list's form, and the query is the one GET
// /api/back-office/tenants/{id}/users binds. The list id carries the tenant id, so one account's cached pages never answer for
// another's.
public sealed class AccountUsersListSourceTests
{
    public static TheoryData<Dictionary<string, string>, int, string[]> QueryCases => new()
    {
        { new Dictionary<string, string>(), 0, ["PageSize=25"] },
        {
            new Dictionary<string, string> { ["usersSearch"] = "ann b", ["usersRoles"] = """["Owner","Member"]""" }, 2,
            ["Search=ann%20b", "Roles=Owner", "Roles=Member", "PageOffset=2", "PageSize=25"]
        }
    };

    [Theory]
    [MemberData(nameof(QueryCases))]
    public async Task Fetch_ShouldSendTheTenantUsersQuery(Dictionary<string, string> filters, int pageOffset, string[] expectedParameters)
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """
                                                              {"totalCount":1,"pageSize":25,"totalPages":1,"currentPageOffset":0,"users":[{"id":"usr_01JMVAW4T4320KJ3A7EJMCG8R0","email":"ann@example.com",
                                                               "firstName":"Ann","lastName":null,"title":null,"role":"Owner","emailConfirmed":false,"createdAt":"2026-01-01T00:00:00+00:00","lastSeenAt":null,"avatarUrl":null}]}
                                                              """
        );
        var backOfficeClient = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });
        var request = new DataListRequest(filters, AccountUsersListSource.DefaultOrderBy, SortOrder.Ascending, pageOffset, DataListController<TenantUserSummary>.PageSize);

        // Act
        var result = await AccountUsersListSource.FetchAsync(backOfficeClient, new TenantId(42), request, CancellationToken.None);

        // Assert
        result.Page!.Items.Should().ContainSingle().Which.Role.Should().Be(UserRole.Owner);
        var sent = network.Requests.Should().ContainSingle().Subject;
        sent.AbsolutePath.Should().Be("/api/back-office/tenants/42/users");
        sent.Query.TrimStart('?').Split('&').Should().BeEquivalentTo(expectedParameters);
    }

    [Fact]
    public void NormalizeFilters_WhenValuesAreMalformed_ShouldKeepOnlyValidValuesInCanonicalForm()
    {
        // Arrange
        var filters = new Dictionary<string, string> { ["usersSearch"] = "  ann  ", ["usersRoles"] = """[ "member" , "Unknown", "Owner", "Owner" ]""" };

        // Act
        var normalized = AccountUsersListSource.NormalizeFilters(filters);

        // Assert
        normalized.Should().BeEquivalentTo(new Dictionary<string, string> { ["usersSearch"] = "ann", ["usersRoles"] = """["Owner","Member"]""" });
    }

    [Fact]
    public void ToggleRole_ShouldAddAndRemoveTheRoleAndLeaveTheParameterOutWhenNoneRemains()
    {
        // Arrange
        var filters = new Dictionary<string, string> { ["usersRoles"] = """["Admin"]""" };

        // Act
        var added = AccountUsersListSource.ToggleRole(filters, UserRole.Owner);
        var removed = AccountUsersListSource.ToggleRole(filters, UserRole.Admin);

        // Assert
        added["usersRoles"].Should().Be("""["Owner","Admin"]""");
        removed["usersRoles"].Should().BeNull();
    }

    [Fact]
    public void ListId_ShouldBeDistinctPerAccount()
    {
        // Act & Assert
        AccountUsersListSource.ListId(new TenantId(42)).Should().NotBe(AccountUsersListSource.ListId(new TenantId(43)));
    }

    [Fact]
    public void Options_WithTheUsersPrefix_ShouldOwnPrefixedParametersAndKeepTheTab()
    {
        // Arrange
        var options = new DataListUrlOptions(AccountUsersListSource.DefaultOrderBy, [], AccountUsersListSource.FilterParameters,
            parameterPrefix: AccountUsersListSource.ParameterPrefix, normalizeFilters: AccountUsersListSource.NormalizeFilters
        );
        const string uri = "https://back-office.dev.localhost:9001/blazor/back-office/accounts/42?tab=users&usersSearch=ann&pageOffset=3";

        // Act
        var state = DataListState.Parse(uri, options) with { PageOffset = 1 };

        // Assert
        state.ToUri(uri, options).Should().Be(
            "https://back-office.dev.localhost:9001/blazor/back-office/accounts/42?tab=users&pageOffset=3&usersSearch=ann&usersPageOffset=1"
        );
    }

    private sealed class RecordingNetwork(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
