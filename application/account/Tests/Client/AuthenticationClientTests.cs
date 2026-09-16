using System.Net;
using System.Text.Json;
using Account.Client;
using Account.Features.Authentication.Requests;
using FluentAssertions;
using SharedKernel.ApiResults;
using SharedKernel.Domain;
using Xunit;
using ServerCommands = Account.Features.Authentication.Commands;

namespace Account.Tests.Client;

public sealed class AuthenticationClientTests
{
    [Fact]
    public async Task GetBootstrapAsync_WhenAuthenticated_ShouldGetBootstrapAndReadResponse()
    {
        // Arrange
        var userId = UserId.NewId();
        var body = $$"""
                     {
                       "isAuthenticated": true,
                       "user": {
                         "id": "{{userId}}", "tenantId": "42", "role": "Owner", "email": "owner@example.com", "firstName": "Ada",
                         "lastName": "Lovelace", "title": null, "avatarUrl": null, "tenantName": "Acme", "tenantLogoUrl": null,
                         "subscriptionPlan": "Free", "isInternalUser": false, "featureFlags": ["compact-view"]
                       },
                       "locale": "da-DK",
                       "runtimeConfiguration": { "PUBLIC_URL": "https://localhost:9000" },
                       "systemFeatureFlags": { "google-oauth": true },
                       "antiforgeryToken": "token"
                     }
                     """;
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = new AuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetBootstrapAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be("/api/account/bootstrap");
        request.Body.Should().BeNull();
        result.IsSuccess.Should().BeTrue();
        result.Value!.User!.Id.Should().Be(userId);
        result.Value.User.TenantId.Should().Be(new TenantId(42));
        result.Value.User.FeatureFlags.Should().Equal("compact-view");
        result.Value.Locale.Should().Be("da-DK");
        result.Value.SystemFeatureFlags.Should().ContainKey("google-oauth").WhoseValue.Should().BeTrue();
        result.Value.AntiforgeryToken.Should().Be("token");
    }

    [Fact]
    public async Task LogoutAsync_WhenResponseIsEmpty_ShouldPostWithoutBodyAndSucceed()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new AuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.LogoutAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/account/authentication/logout");
        request.Body.Should().BeNull();
        result.IsSuccess.Should().BeTrue();
        result.Problem.Should().BeNull();
    }

    [Fact]
    public async Task SwitchTenantAsync_WhenCalled_ShouldPostServerJson()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
        var client = new AuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.SwitchTenantAsync(new SwitchTenantCommand(new TenantId(42)), CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/api/account/authentication/switch-tenant");
        request.Body.Should().Be(JsonSerializer.Serialize(new ServerCommands.SwitchTenantCommand(new TenantId(42)), ApiJsonSerializerOptions.Create()));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SwitchTenantAsync_WhenTransportFails_ShouldSendRequestExactlyOnce()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("Connection reset"));
        var client = new AuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.SwitchTenantAsync(new SwitchTenantCommand(new TenantId(42)), CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.TransportFailure);
        handler.Requests.Should().ContainSingle();
    }
}
