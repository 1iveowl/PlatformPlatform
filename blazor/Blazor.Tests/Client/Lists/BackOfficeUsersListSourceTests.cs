using System.Net;
using System.Text;
using Account.Client;
using Account.Features.Users.BackOffice.Queries;
using Account.Features.Users.Domain;
using Blazor.Client.BackOffice.Users;
using Blazor.Client.Components.Lists;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

// The back office's users list: its URL keeps the React back office's names and value forms (search as text, roles as a JSON
// array of role names, activity as one enum name), malformed values are dropped, and the query is the one
// GET /api/back-office/users binds, in the server's order by last seen, newest first.
public sealed class BackOfficeUsersListSourceTests
{
    public static TheoryData<Dictionary<string, string>, int, string[]> QueryCases => new()
    {
        { new Dictionary<string, string>(), 0, ["OrderBy=LastSeenAt", "SortOrder=Descending", "PageSize=25"] },
        {
            new Dictionary<string, string> { ["search"] = "ann b", ["roles"] = """["Owner","Member"]""", ["activity"] = "InactiveOver30Days" }, 2,
            ["Search=ann%20b", "Roles=Owner", "Roles=Member", "Activity=InactiveOver30Days", "OrderBy=LastSeenAt", "SortOrder=Descending", "PageOffset=2", "PageSize=25"]
        }
    };

    [Theory]
    [MemberData(nameof(QueryCases))]
    public async Task Fetch_ShouldSendTheUsersQuery(Dictionary<string, string> filters, int pageOffset, string[] expectedParameters)
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """
                                                              {"totalCount":1,"pageSize":25,"totalPages":1,"currentPageOffset":0,"users":[{"id":"usr_01JMVAW4T4320KJ3A7EJMCG8R0","tenantId":"42",
                                                               "tenantName":"Acme","tenantPlan":"Basis","tenantPlannedChange":null,"tenantHasEverSubscribed":false,"email":"ann@example.com",
                                                               "firstName":"Ann","lastName":null,"title":null,"role":"Owner","emailConfirmed":true,"createdAt":"2026-01-01T00:00:00+00:00",
                                                               "lastSeenAt":null,"avatarUrl":null}]}
                                                              """
        );
        var backOfficeClient = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });
        var request = new DataListRequest(filters, BackOfficeUsersListSource.DefaultOrderBy, SortOrder.Ascending, pageOffset, DataListController<BackOfficeUserSummary>.PageSize);

        // Act
        var result = await BackOfficeUsersListSource.FetchAsync(backOfficeClient, request, CancellationToken.None);

        // Assert
        result.Page!.Items.Should().ContainSingle().Which.TenantName.Should().Be("Acme");
        var sent = network.Requests.Should().ContainSingle().Subject;
        sent.AbsolutePath.Should().Be("/api/back-office/users");
        sent.Query.TrimStart('?').Split('&').Should().BeEquivalentTo(expectedParameters);
    }

    [Fact]
    public async Task Fetch_WhenTheAccountApiFails_ShouldReportTheFailureWithItsStatus()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.BadRequest, """{"title":"Bad Request","status":400,"detail":"Search must be at most 100 characters."}""");
        var backOfficeClient = new BackOfficeClient(new HttpClient(network) { BaseAddress = new Uri("https://back-office.dev.localhost:9001/blazor/") });
        var request = new DataListRequest(new Dictionary<string, string>(), BackOfficeUsersListSource.DefaultOrderBy, SortOrder.Ascending, 0, 25);

        // Act
        var result = await BackOfficeUsersListSource.FetchAsync(backOfficeClient, request, CancellationToken.None);

        // Assert
        result.Page.Should().BeNull();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public void NormalizeFilters_WhenValuesAreMalformed_ShouldKeepOnlyValidValuesInCanonicalForm()
    {
        // Arrange
        var filters = new Dictionary<string, string>
        {
            ["search"] = "  ann  ", ["roles"] = """[ "member" , "Unknown", "Owner", "Owner" ]""", ["activity"] = "activelast7days", ["pageOffset"] = "3"
        };

        // Act
        var normalized = BackOfficeUsersListSource.NormalizeFilters(filters);

        // Assert
        normalized.Should().BeEquivalentTo(new Dictionary<string, string> { ["search"] = "ann", ["roles"] = """["Owner","Member"]""" });
    }

    [Theory]
    [InlineData("ActiveLast24Hours", UserActivityFilter.ActiveLast24Hours)]
    [InlineData(" InactiveOver30Days ", UserActivityFilter.InactiveOver30Days)]
    [InlineData("1", null)]
    [InlineData("Everyone", null)]
    public void GetActivity_ShouldAcceptExactEnumNamesOnly(string value, UserActivityFilter? expected)
    {
        // Act
        var activity = BackOfficeUsersListSource.GetActivity(new Dictionary<string, string> { ["activity"] = value });

        // Assert
        activity.Should().Be(expected);
    }

    [Fact]
    public void ToggleRole_ShouldAddAndRemoveTheRoleInCanonicalOrder()
    {
        // Arrange
        var filters = new Dictionary<string, string> { ["roles"] = """["Member"]""" };

        // Act
        var added = BackOfficeUsersListSource.ToggleRole(filters, UserRole.Owner);
        var removed = BackOfficeUsersListSource.ToggleRole(filters, UserRole.Member);

        // Assert
        added.Should().BeEquivalentTo(new Dictionary<string, string?> { ["roles"] = """["Owner","Member"]""" });
        removed.Should().BeEquivalentTo(new Dictionary<string, string?> { ["roles"] = null });
    }

    [Fact]
    public void ToggleActivity_ShouldReplaceAnotherValueAndClearTheSelectedOne()
    {
        // Arrange
        var filters = new Dictionary<string, string> { ["activity"] = "ActiveLast7Days" };

        // Act
        var replaced = BackOfficeUsersListSource.ToggleActivity(filters, UserActivityFilter.ActiveLast30Days);
        var cleared = BackOfficeUsersListSource.ToggleActivity(filters, UserActivityFilter.ActiveLast7Days);

        // Assert
        replaced.Should().BeEquivalentTo(new Dictionary<string, string?> { ["activity"] = "ActiveLast30Days" });
        cleared.Should().BeEquivalentTo(new Dictionary<string, string?> { ["activity"] = null });
    }

    [Fact]
    public void Filters_WhenWrittenAndReadBack_ShouldRoundTripThroughTheUrlForm()
    {
        // Arrange
        var written = new Dictionary<string, string>();
        foreach (var change in BackOfficeUsersListSource.ToggleRole(written, UserRole.Admin))
        {
            written[change.Key] = change.Value!;
        }

        foreach (var change in BackOfficeUsersListSource.ToggleActivity(written, UserActivityFilter.ActiveLast24Hours))
        {
            written[change.Key] = change.Value!;
        }

        written["search"] = "acme";

        // Act
        var query = BackOfficeUsersListSource.ToQuery(new DataListRequest(BackOfficeUsersListSource.NormalizeFilters(written), BackOfficeUsersListSource.DefaultOrderBy, SortOrder.Ascending, 0, 25));

        // Assert
        written.Should().BeEquivalentTo(new Dictionary<string, string> { ["roles"] = """["Admin"]""", ["activity"] = "ActiveLast24Hours", ["search"] = "acme" });
        query.Search.Should().Be("acme");
        query.Roles.Should().Equal(UserRole.Admin);
        query.Activity.Should().Be(UserActivityFilter.ActiveLast24Hours);
        query.OrderBy.Should().Be(SortableBackOfficeUserProperties.LastSeenAt);
        query.SortOrder.Should().Be(SortOrder.Descending);
    }

    [Fact]
    public void ClearAllFilters_ShouldRemoveEveryFilterParameter()
    {
        // Act
        var changes = BackOfficeUsersListSource.ClearAllFilters();

        // Assert
        changes.Should().BeEquivalentTo(new Dictionary<string, string?> { ["search"] = null, ["roles"] = null, ["activity"] = null });
    }

    [Theory]
    [InlineData("usr_01JMVAW4T4320KJ3A7EJMCG8R0", true)]
    [InlineData("01JMVAW4T4320KJ3A7EJMCG8R0", false)]
    [InlineData("usr_not-a-ulid", false)]
    [InlineData("", false)]
    public void ParseUserId_ShouldAcceptUserIdsOnly(string value, bool isUserId)
    {
        // Act
        var userId = BackOfficeUsersListSource.ParseUserId(value);

        // Assert
        (userId is not null).Should().Be(isUserId);
    }

    [Fact]
    public void UserUrl_ShouldBeTheUserPageBelowTheBackOffice()
    {
        // Act
        var url = BackOfficeUsersListSource.UserUrl(new UserId("usr_01JMVAW4T4320KJ3A7EJMCG8R0"));

        // Assert
        url.Should().EndWith("/back-office/users/usr_01JMVAW4T4320KJ3A7EJMCG8R0");
    }

    private sealed class RecordingNetwork(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var mediaType = statusCode == HttpStatusCode.OK ? "application/json" : "application/problem+json";
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, mediaType) });
        }
    }
}
