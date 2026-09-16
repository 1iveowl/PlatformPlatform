using System.Net;
using System.Text;
using Account.Client;
using Account.Features.Users.Domain;
using Account.Features.Users.Queries;
using Blazor.Client.Components.Lists;
using Blazor.Client.Users;
using FluentAssertions;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Lists;

// The users list sends the users query the React edition sends: the filter parameters in their API names, calendar dates,
// enum names, the page size on every request and PageOffset only after the first page.
public sealed class UsersListSourceTests
{
    public static TheoryData<Dictionary<string, string>, string, SortOrder, int, string> QueryCases => new()
    {
        { new Dictionary<string, string>(), "Name", SortOrder.Ascending, 0, "OrderBy=Name&SortOrder=Ascending&PageSize=25" },
        { new Dictionary<string, string>(), "Name", SortOrder.Ascending, 2, "OrderBy=Name&SortOrder=Ascending&PageSize=25&PageOffset=2" },
        {
            new Dictionary<string, string> { ["search"] = "a b&c", ["userRole"] = "Admin", ["userStatus"] = "Pending", ["startDate"] = "2026-01-31", ["endDate"] = "2026-12-01" },
            "CreatedAt", SortOrder.Descending, 1,
            "Search=a%20b%26c&UserRole=Admin&UserStatus=Pending&StartDate=2026-01-31&EndDate=2026-12-01&OrderBy=CreatedAt&SortOrder=Descending&PageSize=25&PageOffset=1"
        }
    };

    [Theory]
    [MemberData(nameof(QueryCases))]
    public async Task Fetch_ShouldSendTheUsersQuery(Dictionary<string, string> filters, string orderBy, SortOrder sortOrder, int pageOffset, string expectedQuery)
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK, """{"totalCount":0,"pageSize":25,"totalPages":0,"currentPageOffset":0,"users":[]}""");
        var usersClient = new UsersClient(new HttpClient(network) { BaseAddress = new Uri("https://app.dev.localhost:9000") });

        // Act
        var result = await UsersListSource.FetchAsync(usersClient, new DataListRequest(filters, orderBy, sortOrder, pageOffset, DataListController<UserDetails>.PageSize), CancellationToken.None);

        // Assert
        result.Page.Should().NotBeNull();
        var request = network.Requests.Should().ContainSingle().Subject;
        request.AbsolutePath.Should().Be(AccountApiRoutes.Users);
        ParseQuery(request.Query).Should().BeEquivalentTo(ParseQuery(expectedQuery));
    }

    [Fact]
    public async Task Fetch_WhenTheApiRejectsTheRequest_ShouldReturnTheStatusAndDetail()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.BadRequest, """{"title":"Bad Request","status":400,"detail":"The page offset 9 is greater than the total number of pages."}""");
        var usersClient = new UsersClient(new HttpClient(network) { BaseAddress = new Uri("https://app.dev.localhost:9000") });
        var request = new DataListRequest(new Dictionary<string, string>(), "Name", SortOrder.Ascending, 9, DataListController<UserDetails>.PageSize);

        // Act
        var result = await UsersListSource.FetchAsync(usersClient, request, CancellationToken.None);

        // Assert
        result.Page.Should().BeNull();
        result.StatusCode.Should().Be(400);
        result.ErrorMessage.Should().Be("The page offset 9 is greater than the total number of pages.");
    }

    [Fact]
    public void NormalizeFilters_WhenValuesAreMalformed_ShouldKeepOnlyValidValuesInCanonicalForm()
    {
        // Arrange
        var filters = new Dictionary<string, string>
        {
            ["search"] = "  ann  ", ["userRole"] = "admin", ["userStatus"] = "1", ["startDate"] = "31-01-2026", ["endDate"] = "2026-12-01"
        };

        // Act
        var normalized = UsersListSource.NormalizeFilters(filters);

        // Assert
        normalized.Should().BeEquivalentTo(new Dictionary<string, string> { ["search"] = "ann", ["userRole"] = nameof(UserRole.Admin), ["endDate"] = "2026-12-01" });
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
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
