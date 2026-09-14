using System.Net;
using System.Text;
using Account.Client;
using Account.Features.Users.Domain;
using Blazor.Client.Users;
using FluentAssertions;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client;

// The transitional users facade must send the same users query the spike built by hand: the filter parameters in their
// API names, calendar dates, enum names, the page size on every request and PageOffset only after the first page.
public sealed class UsersApiClientTests
{
    public static TheoryData<UsersListState, int, int, string> QueryCases => new()
    {
        { new UsersListState(), 0, UsersApiClient.VirtualFetchPageSize, "OrderBy=Name&SortOrder=Ascending&PageSize=100" },
        { new UsersListState(PageOffset: 3, Mode: UsersListMode.Paged), 2, UsersApiClient.PagedPageSize, "OrderBy=Name&SortOrder=Ascending&PageSize=25&PageOffset=2" },
        {
            new UsersListState("a b&c", UserRole.Admin, UserStatus.Pending, new DateOnly(2026, 1, 31), new DateOnly(2026, 12, 1), SortableUserProperties.CreatedAt, SortOrder.Descending, UserId: "usr_x"),
            1, UsersApiClient.VirtualFetchPageSize,
            "Search=a%20b%26c&UserRole=Admin&UserStatus=Pending&StartDate=2026-01-31&EndDate=2026-12-01&OrderBy=CreatedAt&SortOrder=Descending&PageSize=100&PageOffset=1"
        }
    };

    [Theory]
    [MemberData(nameof(QueryCases))]
    public async Task GetPage_ShouldSendTheSpikeUsersQuery(UsersListState state, int pageOffset, int pageSize, string expectedQuery)
    {
        // Arrange
        var network = new RecordingNetwork();
        var usersApiClient = new UsersApiClient(new UsersClient(new HttpClient(network) { BaseAddress = new Uri("https://app.dev.localhost:9000") }));

        // Act
        await usersApiClient.GetPageAsync(state, pageOffset, pageSize);

        // Assert
        var request = network.Requests.Should().ContainSingle().Subject;
        request.AbsolutePath.Should().Be(AccountApiRoutes.Users);
        ParseQuery(request.Query).Should().BeEquivalentTo(ParseQuery(expectedQuery));
    }

    [Fact]
    public async Task GetPage_WhenSameStateAndPageAreRequestedAgain_ShouldServeTheCachedPage()
    {
        // Arrange
        var network = new RecordingNetwork();
        var usersApiClient = new UsersApiClient(new UsersClient(new HttpClient(network) { BaseAddress = new Uri("https://app.dev.localhost:9000") }));
        var state = new UsersListState("search", Mode: UsersListMode.Paged);

        // Act
        await usersApiClient.GetPageAsync(state, 1, UsersApiClient.PagedPageSize);
        await usersApiClient.GetPageAsync(state with { PageOffset = 1, UserId = "usr_other" }, 1, UsersApiClient.PagedPageSize);
        await usersApiClient.GetPageAsync(state, 2, UsersApiClient.PagedPageSize);

        // Assert
        usersApiClient.RequestCount.Should().Be(2);
        network.Requests.Should().HaveCount(2);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]));
    }

    private sealed class RecordingNetwork : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var content = new StringContent("""{"totalCount":0,"pageSize":25,"totalPages":0,"currentPageOffset":0,"users":[]}""", Encoding.UTF8, "application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
