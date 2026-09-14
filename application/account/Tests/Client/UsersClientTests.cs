using System.Net;
using System.Text.Json;
using Account.Client;
using Account.Features.Users.Domain;
using Account.Features.Users.Requests;
using FluentAssertions;
using SharedKernel.ApiResults;
using SharedKernel.Domain;
using SharedKernel.Persistence;
using Xunit;
using ServerCommands = Account.Features.Users.Commands;

namespace Account.Tests.Client;

public sealed class UsersClientTests
{
    [Fact]
    public async Task GetUsersAsync_WhenAllFiltersAreSet_ShouldSendPascalCaseEscapedQueryAndReadResponse()
    {
        // Arrange
        var userId = UserId.NewId();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, $$"""{"totalCount":1,"pageSize":10,"totalPages":1,"currentPageOffset":2,"users":[{{UserDetailsJson(userId)}}]}""");
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));
        var query = new GetUsersQuery("ada & bob", UserRole.Admin, UserStatus.Pending, new DateTimeOffset(2026, 1, 2, 23, 30, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero), SortableUserProperties.Email, SortOrder.Descending, 2, 10);

        // Act
        var result = await client.GetUsersAsync(query, CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be("/api/account/users?Search=ada%20%26%20bob&UserRole=Admin&UserStatus=Pending&StartDate=2026-01-02&EndDate=2026-03-04&OrderBy=Email&SortOrder=Descending&PageOffset=2&PageSize=10");
        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(1);
        result.Value.CurrentPageOffset.Should().Be(2);
        result.Value.Users.Should().ContainSingle().Which.Id.Should().Be(userId);
    }

    [Fact]
    public async Task GetUsersAsync_WhenOptionalValuesAreNull_ShouldOmitThem()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"totalCount":0,"pageSize":25,"totalPages":0,"currentPageOffset":0,"users":[]}""");
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetUsersAsync(new GetUsersQuery(), CancellationToken.None);

        // Assert
        handler.Requests.Should().ContainSingle().Which.PathAndQuery.Should().Be("/api/account/users?OrderBy=Name&SortOrder=Ascending&PageSize=25");
        result.IsSuccess.Should().BeTrue();
        result.Value!.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserAsync_WhenCalled_ShouldGetUserRouteAndReadResponse()
    {
        // Arrange
        var userId = UserId.NewId();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, UserDetailsJson(userId));
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetUserAsync(userId, CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be($"/api/account/users/{userId}");
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(userId);
        result.Value.Role.Should().Be(UserRole.Admin);
        result.Value.CreatedAt.Should().Be(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        result.Value.ModifiedAt.Should().BeNull();
        result.Value.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentUserAsync_WhenCalled_ShouldGetMeRouteAndReadResponse()
    {
        // Arrange
        var userId = UserId.NewId();
        var body = $$"""{"id":"{{userId}}","createdAt":"2026-01-02T03:04:05+00:00","modifiedAt":null,"email":"ada@example.com","role":"Owner","firstName":"Ada","lastName":"Lovelace","title":"Engineer","avatarUrl":null}""";
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetCurrentUserAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be("/api/account/users/me");
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(userId);
        result.Value.Role.Should().Be(UserRole.Owner);
        result.Value.Title.Should().Be("Engineer");
    }

    [Fact]
    public async Task UpdateCurrentUserAsync_WhenCalled_ShouldPutServerJson()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.UpdateCurrentUserAsync(new UpdateCurrentUserCommand("Ada", "Lovelace", "Engineer"), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.PathAndQuery.Should().Be("/api/account/users/me");
        request.Body.Should().Be(SerializeServerCommand(new ServerCommands.UpdateCurrentUserCommand("Ada", "Lovelace", "Engineer")));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ChangeUserRoleAsync_WhenCalled_ShouldPutServerJsonToUserRoute()
    {
        // Arrange
        var userId = UserId.NewId();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.ChangeUserRoleAsync(userId, new ChangeUserRoleCommand { UserRole = UserRole.Admin }, CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.PathAndQuery.Should().Be($"/api/account/users/{userId}/change-user-role");
        request.Body.Should().Be(SerializeServerCommand(new ServerCommands.ChangeUserRoleCommand { Id = userId, UserRole = UserRole.Admin }));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteUserAsync_WhenCalled_ShouldDeleteUserRouteWithoutBody()
    {
        // Arrange
        var userId = UserId.NewId();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.DeleteUserAsync(userId, CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Delete);
        request.PathAndQuery.Should().Be($"/api/account/users/{userId}");
        request.Body.Should().BeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task BulkDeleteUsersAsync_WhenCalled_ShouldPostServerJson()
    {
        // Arrange
        UserId[] userIds = [UserId.NewId(), UserId.NewId()];
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.BulkDeleteUsersAsync(new BulkDeleteUsersCommand(userIds), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/account/users/bulk-delete");
        request.Body.Should().Be(SerializeServerCommand(new ServerCommands.BulkDeleteUsersCommand(userIds)));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task BulkDeleteUsersAsync_WhenTransportFails_ShouldSendRequestExactlyOnce()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("Connection reset"));
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.BulkDeleteUsersAsync(new BulkDeleteUsersCommand([UserId.NewId()]), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.TransportFailure);
        handler.Requests.Should().ContainSingle();
    }

    private static string UserDetailsJson(UserId userId)
    {
        return $$"""{"id":"{{userId}}","createdAt":"2026-01-02T03:04:05+00:00","modifiedAt":null,"lastSeenAt":null,"email":"ada@example.com","role":"Admin","firstName":"Ada","lastName":"Lovelace","title":"","emailConfirmed":true,"avatarUrl":null}""";
    }

    private static string SerializeServerCommand<TCommand>(TCommand command)
    {
        return JsonSerializer.Serialize(command, ApiJsonSerializerOptions.Create());
    }
}
