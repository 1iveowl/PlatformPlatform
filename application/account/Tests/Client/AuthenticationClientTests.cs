using System.Net;
using System.Text.Json;
using Account.Client;
using Account.Features.Authentication.Domain;
using Account.Features.Authentication.Requests;
using FluentAssertions;
using SharedKernel.ApiResults;
using SharedKernel.Authentication.TokenGeneration;
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

    [Fact]
    public async Task GetSessionsAsync_WhenCalled_ShouldGetSessionsAndReadEnumsAsNames()
    {
        // Arrange
        var sessionId = SessionId.NewId();
        var body = $$"""
                     {
                       "sessions": [{
                         "id": "{{sessionId}}", "createdAt": "2026-09-01T10:00:00+00:00", "loginMethod": "MitId", "deviceType": "Tablet",
                         "userAgent": "Mozilla/5.0", "ipAddress": "10.0.0.1", "lastActivityAt": "2026-09-17T08:00:00+00:00", "isCurrent": true,
                         "tenantName": "Acme"
                       }]
                     }
                     """;
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = new AuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetSessionsAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be("/api/account/authentication/sessions");
        result.IsSuccess.Should().BeTrue();
        var session = result.Value!.Sessions.Should().ContainSingle().Subject;
        session.Id.Should().Be(sessionId);
        session.LoginMethod.Should().Be(LoginMethod.MitId);
        session.DeviceType.Should().Be(DeviceType.Tablet);
        session.IsCurrent.Should().BeTrue();
        session.TenantName.Should().Be("Acme");
    }

    [Fact]
    public async Task RevokeSessionAsync_WhenCalled_ShouldDeleteSessionRouteWithoutBody()
    {
        // Arrange
        var sessionId = SessionId.NewId();
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new AuthenticationClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.RevokeSessionAsync(sessionId, CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Delete);
        request.PathAndQuery.Should().Be($"/api/account/authentication/sessions/{sessionId}");
        request.Body.Should().BeNull();
        result.IsSuccess.Should().BeTrue();
    }
}
