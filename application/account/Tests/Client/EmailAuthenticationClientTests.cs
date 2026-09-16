using System.Net;
using System.Text.Json;
using Account.Client;
using Account.Features.EmailAuthentication.Domain;
using Account.Features.EmailAuthentication.Requests;
using FluentAssertions;
using SharedKernel.ApiResults;
using SharedKernel.Domain;
using Xunit;
using ServerCommands = Account.Features.EmailAuthentication.Commands;

namespace Account.Tests.Client;

public sealed class EmailAuthenticationClientTests
{
    [Fact]
    public async Task StartLoginAsync_WhenCalled_ShouldPostServerJsonAndReadResponse()
    {
        // Arrange
        var emailLoginId = EmailLoginId.NewId();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, $$"""{"emailLoginId":"{{emailLoginId}}","validForSeconds":300}""");
        var client = new EmailAuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.StartLoginAsync(new StartEmailLoginCommand("user@example.com"), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/account/authentication/email/login/start");
        request.Body.Should().Be(SerializeServerCommand(new ServerCommands.StartEmailLoginCommand("user@example.com")));
        result.IsSuccess.Should().BeTrue();
        result.Value!.EmailLoginId.Should().Be(emailLoginId);
        result.Value.ValidForSeconds.Should().Be(300);
    }

    [Fact]
    public async Task CompleteLoginAsync_WhenCalled_ShouldPostServerJsonToLoginRoute()
    {
        // Arrange
        var emailLoginId = EmailLoginId.NewId();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new EmailAuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.CompleteLoginAsync(emailLoginId, new CompleteEmailLoginCommand("123456", new TenantId(42)), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be($"/api/account/authentication/email/login/{emailLoginId}/complete");
        request.Body.Should().Be(SerializeServerCommand(new ServerCommands.CompleteEmailLoginCommand("123456", new TenantId(42)) { Id = emailLoginId }));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task StartSignupAsync_WhenCalled_ShouldPostServerJsonAndReadResponse()
    {
        // Arrange
        var emailLoginId = EmailLoginId.NewId();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, $$"""{"emailLoginId":"{{emailLoginId}}","validForSeconds":300}""");
        var client = new EmailAuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.StartSignupAsync(new StartEmailSignupCommand("user@example.com"), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/account/authentication/email/signup/start");
        request.Body.Should().Be(SerializeServerCommand(new ServerCommands.StartEmailSignupCommand("user@example.com")));
        result.IsSuccess.Should().BeTrue();
        result.Value!.EmailLoginId.Should().Be(emailLoginId);
    }

    [Fact]
    public async Task CompleteSignupAsync_WhenCalled_ShouldPostServerJsonToSignupRoute()
    {
        // Arrange
        var emailLoginId = EmailLoginId.NewId();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new EmailAuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.CompleteSignupAsync(emailLoginId, new CompleteEmailSignupCommand("123456", "da-DK"), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be($"/api/account/authentication/email/signup/{emailLoginId}/complete");
        request.Body.Should().Be(SerializeServerCommand(new ServerCommands.CompleteEmailSignupCommand("123456", "da-DK") { EmailLoginId = emailLoginId }));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteLoginAsync_WhenTransportFails_ShouldSendRequestExactlyOnce()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("Connection refused"));
        var client = new EmailAuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.CompleteLoginAsync(EmailLoginId.NewId(), new CompleteEmailLoginCommand("123456"), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.TransportFailure);
        handler.Requests.Should().ContainSingle();
    }

    private static string SerializeServerCommand<TCommand>(TCommand command)
    {
        return JsonSerializer.Serialize(command, ApiJsonSerializerOptions.Create());
    }

    [Theory]
    [InlineData(false, "/api/account/authentication/email/login/emlog_01JMVAW4T4320KJ3A7EJMCG8R0/resend-code")]
    [InlineData(true, "/api/account/authentication/email/signup/emlog_01JMVAW4T4320KJ3A7EJMCG8R0/resend-code")]
    public async Task ResendCodeAsync_WhenCalled_ShouldPostWithoutBodyAndReadValidity(bool isSignup, string expectedPath)
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"validForSeconds":300}""");
        var client = new EmailAuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));
        var emailLoginId = new EmailLoginId("emlog_01JMVAW4T4320KJ3A7EJMCG8R0");

        // Act
        var result = isSignup
            ? await client.ResendSignupCodeAsync(emailLoginId, CancellationToken.None)
            : await client.ResendLoginCodeAsync(emailLoginId, CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be(expectedPath);
        result.IsSuccess.Should().BeTrue();
        result.Value!.ValidForSeconds.Should().Be(300);
    }
}
