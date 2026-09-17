using System.Net;
using System.Text;
using System.Text.Json;
using Account.Client;
using Account.Features.Users.Domain;
using Account.Features.Users.Requests;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
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
    public async Task UpdateAvatarAsync_WhenCalled_ShouldPostOneMultipartFileNamedFileWithItsContentType()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));
        var fileBytes = "avatar-image-bytes"u8.ToArray();

        // Act
        var result = await client.UpdateAvatarAsync(new UpdateAvatarCommand(new MemoryStream(fileBytes), "image/png"), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/account/users/me/update-avatar");
        var contentType = MediaTypeHeaderValue.Parse(request.ContentType);
        contentType.MediaType.Value.Should().Be("multipart/form-data");
        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value!;
        var reader = new MultipartReader(boundary, new MemoryStream(Encoding.UTF8.GetBytes(request.Body!)));
        var section = await reader.ReadNextSectionAsync();
        section.Should().NotBeNull();
        var disposition = section.GetContentDispositionHeader()!;
        disposition.IsFileDisposition().Should().BeTrue();
        disposition.Name.Value.Should().Be("file");
        disposition.FileName.Value.Should().Be("avatar");
        section.ContentType.Should().Be("image/png");
        using var sectionContent = new MemoryStream();
        await section.Body.CopyToAsync(sectionContent);
        sectionContent.ToArray().Should().Equal(fileBytes);
        (await reader.ReadNextSectionAsync()).Should().BeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAvatarAsync_WhenSentThroughTheHeaderHandlers_ShouldCarryTheAntiforgeryTokenAndLocale()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var chain = new AntiforgeryHeaderHandler(new FixedAntiforgeryTokenSource("antiforgery-token")) { InnerHandler = new LocaleHeaderHandler(() => "da-DK") { InnerHandler = handler } };
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(chain));

        // Act
        await client.UpdateAvatarAsync(new UpdateAvatarCommand(new MemoryStream([1, 2, 3]), "image/jpeg"), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Headers[AccountApiHeaders.AntiforgeryToken].Should().Equal("antiforgery-token");
        request.Headers[AccountApiHeaders.Locale].Should().Equal("da-DK");
    }

    [Fact]
    public async Task UpdateAvatarAsync_WhenValidationFails_ShouldReturnTheFieldErrorsAsReturned()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.BadRequest, """{"title":"Bad Request","status":400,"errors":{"fileSteam":["Image must be a valid JPEG, PNG, GIF, or WebP file."]}}""", "application/problem+json");
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.UpdateAvatarAsync(new UpdateAvatarCommand(new MemoryStream([1]), "image/png"), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.ValidationFailure);
        result.Problem!.Errors["fileSteam"].Should().Equal("Image must be a valid JPEG, PNG, GIF, or WebP file.");
    }

    [Fact]
    public async Task UpdateAvatarAsync_WhenTheRequestIsTooLargeWithoutBody_ShouldReturnAFailureWithTheStatus()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.RequestEntityTooLarge);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.UpdateAvatarAsync(new UpdateAvatarCommand(new MemoryStream([1]), "image/png"), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.Failure);
        result.Problem!.StatusCode.Should().Be(413);
    }

    [Fact]
    public async Task RemoveAvatarAsync_WhenCalled_ShouldDeleteRemoveAvatarRouteWithoutBody()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.RemoveAvatarAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Delete);
        request.PathAndQuery.Should().Be("/api/account/users/me/remove-avatar");
        request.Body.Should().BeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void UpdateAvatarCommand_ShouldHaveTheServerSizeLimit()
    {
        // Act and Assert
        UpdateAvatarCommand.MaximumFileSizeInBytes.Should().Be(ServerCommands.UpdateAvatarCommand.MaximumFileSizeInBytes);
    }

    [Fact]
    public async Task ChangeThemeAsync_WhenCalled_ShouldPutServerJsonToChangeThemeRoute()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.ChangeThemeAsync(new ChangeThemeCommand("system", "dark", "dark"), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.PathAndQuery.Should().Be("/api/account/users/me/change-theme");
        request.Body.Should().Be(SerializeServerCommand(new ServerCommands.ChangeThemeCommand("system", "dark", "dark")));
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

    [Fact]
    public async Task InviteUserAsync_WhenCalled_ShouldPostServerJson()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.InviteUserAsync(new InviteUserCommand("ada@example.com"), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/account/users/invite");
        request.Body.Should().Be(SerializeServerCommand(new ServerCommands.InviteUserCommand("ada@example.com")));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task InviteUserAsync_WhenUserAlreadyExists_ShouldReturnTheProblemDetail()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.BadRequest, """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"detail":"The user 'ada@example.com' already exists."}""", "application/problem+json");
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.InviteUserAsync(new InviteUserCommand("ada@example.com"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Problem!.Detail.Should().Be("The user 'ada@example.com' already exists.");
    }

    [Theory]
    [InlineData(null, "/api/account/users/deleted?PageSize=25")]
    [InlineData(2, "/api/account/users/deleted?PageOffset=2&PageSize=25")]
    public async Task GetDeletedUsersAsync_WhenCalled_ShouldGetDeletedRouteAndReadResponse(int? pageOffset, string expectedPathAndQuery)
    {
        // Arrange
        var userId = UserId.NewId();
        var json = $$"""{"totalCount":1,"pageSize":25,"totalPages":1,"currentPageOffset":0,"users":[{"id":"{{userId}}","createdAt":"2026-01-02T03:04:05+00:00","modifiedAt":null,"deletedAt":"2026-02-03T04:05:06+00:00","email":"ada@example.com","role":"Member","firstName":null,"lastName":null,"title":null,"emailConfirmed":true,"avatarUrl":null}]}""";
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, json);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetDeletedUsersAsync(new GetDeletedUsersQuery(pageOffset), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be(expectedPathAndQuery);
        result.IsSuccess.Should().BeTrue();
        var user = result.Value!.Users.Should().ContainSingle().Subject;
        user.Id.Should().Be(userId);
        user.DeletedAt.Should().Be(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));
        user.Role.Should().Be(UserRole.Member);
    }

    [Fact]
    public async Task RestoreUserAsync_WhenCalled_ShouldPostRestoreRouteWithoutBody()
    {
        // Arrange
        var userId = UserId.NewId();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.RestoreUserAsync(userId, CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be($"/api/account/users/{userId}/restore");
        request.Body.Should().BeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PurgeUserAsync_WhenCalled_ShouldDeletePurgeRouteWithoutBody()
    {
        // Arrange
        var userId = UserId.NewId();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.PurgeUserAsync(userId, CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Delete);
        request.PathAndQuery.Should().Be($"/api/account/users/{userId}/purge");
        request.Body.Should().BeNull();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task BulkPurgeUsersAsync_WhenCalled_ShouldPostServerJson()
    {
        // Arrange
        UserId[] userIds = [UserId.NewId(), UserId.NewId()];
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.BulkPurgeUsersAsync(new BulkPurgeUsersCommand(userIds), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/account/users/deleted/bulk-purge");
        request.Body.Should().Be(SerializeServerCommand(new ServerCommands.BulkPurgeUsersCommand(userIds)));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyRecycleBinAsync_WhenCalled_ShouldPostEmptyRouteAndReadTheCount()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "3");
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.EmptyRecycleBinAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/account/users/deleted/empty-recycle-bin");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(3);
    }

    [Fact]
    public async Task EmptyRecycleBinAsync_WhenNotOwner_ShouldReturnTheProblemDetail()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403,"detail":"Only owners can empty the deleted users recycle bin."}""", "application/problem+json");
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.EmptyRecycleBinAsync(CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Problem!.Detail.Should().Be("Only owners can empty the deleted users recycle bin.");
    }

    private static string UserDetailsJson(UserId userId)
    {
        return $$"""{"id":"{{userId}}","createdAt":"2026-01-02T03:04:05+00:00","modifiedAt":null,"lastSeenAt":null,"email":"ada@example.com","role":"Admin","firstName":"Ada","lastName":"Lovelace","title":"","emailConfirmed":true,"avatarUrl":null}""";
    }

    private static string SerializeServerCommand<TCommand>(TCommand command)
    {
        return JsonSerializer.Serialize(command, ApiJsonSerializerOptions.Create());
    }

    private sealed class FixedAntiforgeryTokenSource(string antiforgeryToken) : IAntiforgeryTokenSource
    {
        public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<string?>(antiforgeryToken);
        }
    }
}
