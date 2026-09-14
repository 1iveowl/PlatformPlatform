using System.Net;
using Account.Client;
using Account.Features.Authentication.Requests;
using Account.Features.Tenants.Requests;
using Account.Features.Users.Requests;
using FluentAssertions;
using SharedKernel.Authentication;
using SharedKernel.Domain;
using Xunit;

namespace Account.Tests.Client;

// Outcomes every typed client shares, exercised through the public clients over a stub handler
public sealed class AccountApiTransportTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, "")]
    [InlineData(HttpStatusCode.NoContent, null)]
    public async Task UpdateCurrentTenantAsync_WhenSuccessBodyIsEmpty_ShouldReturnSuccess(HttpStatusCode statusCode, string? body)
    {
        // Arrange
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(StubHttpMessageHandler.Returning(statusCode, body)));

        // Act
        var result = await client.UpdateCurrentTenantAsync(new UpdateCurrentTenantCommand { Name = "Acme" }, CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.Success);
        result.IsSuccess.Should().BeTrue();
        result.Problem.Should().BeNull();
    }

    [Theory]
    [InlineData("", "application/json")]
    [InlineData("<html>ok</html>", "text/html")]
    [InlineData("""{"id":"42","name":""", "application/json")]
    [InlineData("null", "application/json")]
    public async Task GetCurrentTenantAsync_WhenSuccessBodyIsNotReadable_ShouldReturnInvalidResponse(string body, string contentType)
    {
        // Arrange
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(StubHttpMessageHandler.Returning(HttpStatusCode.OK, body, contentType)));

        // Act
        var result = await client.GetCurrentTenantAsync(CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.InvalidResponse);
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Problem!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task UpdateCurrentUserAsync_WhenValidationFails_ShouldPreserveFieldErrorsAsReturned()
    {
        // Arrange
        var body = """
                   {
                     "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                     "title": "Bad Request",
                     "status": 400,
                     "detail": "One or more validation errors occurred.",
                     "errors": { "firstName": ["First name must be no longer than 30 characters."], "lastName": ["Last name is required.", "Last name is too short."] },
                     "traceId": "trace"
                   }
                   """;
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(StubHttpMessageHandler.Returning(HttpStatusCode.BadRequest, body, "application/problem+json")));

        // Act
        var result = await client.UpdateCurrentUserAsync(new UpdateCurrentUserCommand("Ada", "", "Engineer"), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.ValidationFailure);
        result.Problem!.StatusCode.Should().Be(400);
        result.Problem.Title.Should().Be("Bad Request");
        result.Problem.Detail.Should().Be("One or more validation errors occurred.");
        result.Problem.Errors.Keys.Should().BeEquivalentTo("firstName", "lastName");
        result.Problem.Errors["firstName"].Should().Equal("First name must be no longer than 30 characters.");
        result.Problem.Errors["lastName"].Should().Equal("Last name is required.", "Last name is too short.");
        result.Problem.UnauthorizedReason.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentTenantAsync_WhenValidationFails_ShouldReturnValidationFailureWithoutValue()
    {
        // Arrange
        var body = """{"title":"Bad Request","status":400,"errors":{"name":["Name is required."]}}""";
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(StubHttpMessageHandler.Returning(HttpStatusCode.BadRequest, body)));

        // Act
        var result = await client.GetCurrentTenantAsync(CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.ValidationFailure);
        result.Value.Should().BeNull();
        result.Problem!.Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task DeleteUserAsync_WhenProblemHasNoErrors_ShouldReturnFailureWithStatusTitleAndDetail()
    {
        // Arrange
        var body = """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.4","title":"Forbidden","status":403,"detail":"Only owners are allowed to delete other users.","errors":{}}""";
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(StubHttpMessageHandler.Returning(HttpStatusCode.Forbidden, body, "application/problem+json")));

        // Act
        var result = await client.DeleteUserAsync(UserId.NewId(), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.Failure);
        result.Problem!.StatusCode.Should().Be(403);
        result.Problem.Title.Should().Be("Forbidden");
        result.Problem.Detail.Should().Be("Only owners are allowed to delete other users.");
        result.Problem.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"title":"Internal""", "application/problem+json", "Internal Server Error")]
    [InlineData(HttpStatusCode.BadGateway, "<html><body>Bad gateway</body></html>", "text/html", "Bad Gateway")]
    [InlineData(HttpStatusCode.NotFound, "", "application/json", "Not Found")]
    [InlineData(HttpStatusCode.Conflict, """["not","a","problem"]""", "application/json", "Conflict")]
    public async Task GetUserAsync_WhenErrorBodyIsNotProblemDetails_ShouldReturnFailureWithStatusAndReasonPhrase(HttpStatusCode statusCode, string body, string contentType, string expectedTitle)
    {
        // Arrange
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(StubHttpMessageHandler.Returning(statusCode, body, contentType)));

        // Act
        var result = await client.GetUserAsync(UserId.NewId(), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.Failure);
        result.Value.Should().BeNull();
        result.Problem!.StatusCode.Should().Be((int)statusCode);
        result.Problem.Title.Should().Be(expectedTitle);
        result.Problem.Detail.Should().BeNull();
        result.Problem.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(nameof(UnauthorizedReason.Revoked))]
    [InlineData(nameof(UnauthorizedReason.SessionNotFound))]
    [InlineData(nameof(UnauthorizedReason.TenantDeleted))]
    [InlineData(nameof(UnauthorizedReason.ReplayAttackDetected))]
    public async Task GetCurrentUserAsync_WhenUnauthorizedWithReason_ShouldReturnUnauthorizedWithReason(string unauthorizedReason)
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) =>
            {
                var response = StubHttpMessageHandler.CreateResponse(HttpStatusCode.Unauthorized);
                response.Headers.Add(AuthenticationTokenHttpKeys.UnauthorizedReasonHeaderKey, unauthorizedReason);
                return Task.FromResult(response);
            }
        );
        var client = new UsersClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetCurrentUserAsync(CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.Unauthorized);
        result.Value.Should().BeNull();
        result.Problem!.StatusCode.Should().Be(401);
        result.Problem.Title.Should().Be("Unauthorized");
        result.Problem.UnauthorizedReason.Should().Be(unauthorizedReason);
    }

    [Fact]
    public async Task LogoutAsync_WhenUnauthorizedWithoutReason_ShouldReturnUnauthorizedWithoutReason()
    {
        // Arrange
        var body = """{"title":"Unauthorized","status":401,"errors":{"session":["ignored"]}}""";
        var client = new AuthenticationClient(StubHttpMessageHandler.CreateHttpClient(StubHttpMessageHandler.Returning(HttpStatusCode.Unauthorized, body)));

        // Act
        var result = await client.LogoutAsync(CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.Unauthorized);
        result.Problem!.StatusCode.Should().Be(401);
        result.Problem.UnauthorizedReason.Should().BeNull();
        result.Problem.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTenantsAsync_WhenTransportFails_ShouldReturnTransportFailureWithoutStatus()
    {
        // Arrange
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(StubHttpMessageHandler.Throwing(new HttpRequestException("Connection refused"))));

        // Act
        var result = await client.GetTenantsAsync(CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.TransportFailure);
        result.Value.Should().BeNull();
        result.Problem!.StatusCode.Should().BeNull();
        result.Problem.Detail.Should().Be("Connection refused");
    }

    [Fact]
    public async Task UpdateCurrentTenantAsync_WhenRequestTimesOut_ShouldReturnTransportFailureAndSendOnce()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            }
        );
        var httpClient = StubHttpMessageHandler.CreateHttpClient(handler);
        httpClient.Timeout = TimeSpan.FromMilliseconds(50);
        var client = new TenantsClient(httpClient);

        // Act
        var result = await client.UpdateCurrentTenantAsync(new UpdateCurrentTenantCommand { Name = "Acme" }, CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.TransportFailure);
        result.Problem!.StatusCode.Should().BeNull();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task GetTenantsAsync_WhenCallerCancels_ShouldForwardCancellationAndThrowOperationCanceledException()
    {
        // Arrange
        var cancellationTokenSource = new CancellationTokenSource();
        var handlerObservedCancellation = false;
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
            {
                await cancellationTokenSource.CancelAsync();
                handlerObservedCancellation = cancellationToken.IsCancellationRequested;
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            }
        );
        var client = new TenantsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var act = () => client.GetTenantsAsync(cancellationTokenSource.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        handlerObservedCancellation.Should().BeTrue();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SwitchTenantAsync_WhenCallerCancelsBeforeResponse_ShouldThrowOperationCanceledExceptionAndSendOnce()
    {
        // Arrange
        var cancellationTokenSource = new CancellationTokenSource();
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
            {
                await cancellationTokenSource.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
                throw new UnreachableException();
            }
        );
        var client = new AuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var act = () => client.SwitchTenantAsync(new SwitchTenantCommand(new TenantId(42)), cancellationTokenSource.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().ContainSingle();
    }
}
