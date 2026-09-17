using System.Net;
using System.Text;
using Account.Client;
using Account.Features.Users.Queries;
using Blazor.Client.Components.Lists;
using Blazor.Client.Users;
using FluentAssertions;
using SharedKernel.Persistence;

namespace Blazor.Tests.Client.Users;

// The recycle bin sends the deleted users query the React edition sends: the page size on every request, PageOffset only
// after the first page, and no filter or sort
public sealed class DeletedUsersListSourceTests
{
    [Theory]
    [InlineData(0, "?PageSize=25")]
    [InlineData(2, "?PageOffset=2&PageSize=25")]
    public async Task Fetch_ShouldSendTheDeletedUsersQuery(int pageOffset, string expectedQuery)
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.OK,
            """{"totalCount":1,"pageSize":25,"totalPages":1,"currentPageOffset":0,"users":[{"id":"usr_01KC0OTHER000000000000000A","createdAt":"2026-01-02T03:04:05+00:00","modifiedAt":null,"deletedAt":"2026-02-03T04:05:06+00:00","email":"ada@example.com","role":"Member","firstName":"Ada","lastName":null,"title":null,"emailConfirmed":true,"avatarUrl":null}]}"""
        );
        var usersClient = new UsersClient(new HttpClient(network) { BaseAddress = new Uri("https://app.dev.localhost:9000") });
        var request = new DataListRequest(new Dictionary<string, string> { ["search"] = "ignored" }, DeletedUsersListSource.DefaultOrderBy, SortOrder.Descending, pageOffset,
            DataListController<DeletedUserDetails>.PageSize
        );

        // Act
        var result = await DeletedUsersListSource.FetchAsync(usersClient, request, CancellationToken.None);

        // Assert
        var requestUri = network.Requests.Should().ContainSingle().Subject;
        requestUri.AbsolutePath.Should().Be(AccountApiRoutes.DeletedUsers);
        requestUri.Query.Should().Be(expectedQuery);
        result.Page!.TotalCount.Should().Be(1);
        result.Page.Items.Should().ContainSingle().Which.DisplayNameOrEmail.Should().Be("Ada");
    }

    [Fact]
    public async Task Fetch_WhenTheApiRefuses_ShouldReturnTheStatusAndDetailAsReturned()
    {
        // Arrange
        var network = new RecordingNetwork(HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403,"detail":"Only owners and admins can view deleted users."}""");
        var usersClient = new UsersClient(new HttpClient(network) { BaseAddress = new Uri("https://app.dev.localhost:9000") });
        var request = new DataListRequest(new Dictionary<string, string>(), DeletedUsersListSource.DefaultOrderBy, SortOrder.Ascending, 0, DataListController<DeletedUserDetails>.PageSize);

        // Act
        var result = await DeletedUsersListSource.FetchAsync(usersClient, request, CancellationToken.None);

        // Assert
        result.Page.Should().BeNull();
        result.StatusCode.Should().Be(403);
        result.ErrorMessage.Should().Be("Only owners and admins can view deleted users.");
    }

    [Fact]
    public void Options_WhenTheListHasNoSortableColumns_ShouldIgnoreAnOrderByInTheUrl()
    {
        // Arrange
        var options = new DataListUrlOptions(DeletedUsersListSource.DefaultOrderBy, []);

        // Act
        var state = DataListState.Parse("https://app.dev.localhost:9000/blazor/account/users/recycle-bin?orderBy=Email&pageOffset=1", options);

        // Assert
        state.OrderBy.Should().Be(DeletedUsersListSource.DefaultOrderBy);
        state.ToUri("https://app.dev.localhost:9000/blazor/account/users/recycle-bin", options).Should().EndWith("recycle-bin?pageOffset=1");
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
