using System.Net;
using System.Text.Json;
using Account.Client;
using Account.Features.Authentication.Queries;
using Account.Features.FeatureFlags.Requests;
using FluentAssertions;
using SharedKernel.ApiResults;
using SharedKernel.Domain;
using Xunit;
using FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags;
using ServerCommands = Account.Features.FeatureFlags.Commands;

namespace Account.Tests.Client;

public sealed class FeatureFlagsClientTests
{
    [Fact]
    public async Task GetTenantConfigurableAsync_WhenCalled_ShouldGetTheTenantConfigurableRouteAndReadTheFlags()
    {
        // Arrange
        var body = """{"flags":[{"flagKey":"account-overview","enabled":true}]}""";
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = new FeatureFlagsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetTenantConfigurableAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be("/api/account/feature-flags/tenant-configurable");
        result.IsSuccess.Should().BeTrue();
        var flag = result.Value!.Flags.Should().ContainSingle().Subject;
        flag.FlagKey.Should().Be("account-overview");
        flag.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetUserConfigurableAsync_WhenCalled_ShouldGetTheUserConfigurableRouteAndReadTheFlags()
    {
        // Arrange
        var body = """{"flags":[{"flagKey":"compact-view","enabled":false}]}""";
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, body);
        var client = new FeatureFlagsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetUserConfigurableAsync(CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.PathAndQuery.Should().Be("/api/account/feature-flags/user-configurable");
        result.IsSuccess.Should().BeTrue();
        var flag = result.Value!.Flags.Should().ContainSingle().Subject;
        flag.FlagKey.Should().Be("compact-view");
        flag.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetTenantConfigurableAsync_WhenTheListIsEmpty_ShouldSucceedWithNoFlags()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"flags":[]}""");
        var client = new FeatureFlagsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.GetTenantConfigurableAsync(CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Flags.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetTenantOverrideAsync_WhenCalled_ShouldPutTheServerJsonOnTheFlagsTenantOverrideRoute(bool enabled)
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new FeatureFlagsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.SetTenantOverrideAsync("account-overview", new SetTenantFeatureFlagOwnerCommand { Enabled = enabled }, CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.PathAndQuery.Should().Be("/api/account/feature-flags/account-overview/tenant-override");
        request.Body.Should().Be(JsonSerializer.Serialize(new ServerCommands.SetTenantFeatureFlagOwnerCommand { Enabled = enabled }, ApiJsonSerializerOptions.Create()));
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetUserOverrideAsync_WhenCalled_ShouldPutTheServerJsonOnTheFlagsUserOverrideRoute(bool enabled)
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new FeatureFlagsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.SetUserOverrideAsync("compact-view", new SetUserFeatureFlagCommand { Enabled = enabled }, CancellationToken.None);

        // Assert
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.PathAndQuery.Should().Be("/api/account/feature-flags/compact-view/user-override");
        request.Body.Should().Be(JsonSerializer.Serialize(new ServerCommands.SetUserFeatureFlagCommand { Enabled = enabled }, ApiJsonSerializerOptions.Create()));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SetTenantOverrideAsync_WhenTheFlagKeyNeedsEscaping_ShouldEscapeItInTheRoute()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NoContent);
        var client = new FeatureFlagsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        await client.SetTenantOverrideAsync("a b/c", new SetTenantFeatureFlagOwnerCommand { Enabled = true }, CancellationToken.None);

        // Assert
        handler.Requests.Should().ContainSingle().Which.PathAndQuery.Should().Be("/api/account/feature-flags/a%20b%2Fc/tenant-override");
    }

    [Fact]
    public async Task SetTenantOverrideAsync_WhenTheCallerIsNotTheOwner_ShouldReturnTheApiMessageAsReturned()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403,"detail":"Only owners are allowed to configure tenant feature flags."}""", "application/problem+json"
        );
        var client = new FeatureFlagsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.SetTenantOverrideAsync("account-overview", new SetTenantFeatureFlagOwnerCommand { Enabled = true }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Outcome.Should().Be(ApiCallOutcome.Failure);
        result.Problem!.StatusCode.Should().Be(403);
        result.Problem.Detail.Should().Be("Only owners are allowed to configure tenant feature flags.");
    }

    [Fact]
    public async Task SetUserOverrideAsync_WhenTheFlagIsNotUserConfigurable_ShouldReturnTheApiMessageAsReturned()
    {
        // Arrange
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403,"detail":"Feature flag 'beta-features' is not configurable by users."}""", "application/problem+json"
        );
        var client = new FeatureFlagsClient(StubHttpMessageHandler.CreateHttpClient(handler));

        // Act
        var result = await client.SetUserOverrideAsync("beta-features", new SetUserFeatureFlagCommand { Enabled = true }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Problem!.Detail.Should().Be("Feature flag 'beta-features' is not configurable by users.");
    }

    [Fact]
    public async Task SetUserOverrideAsync_WhenTheResponseCarriesTheFeatureFlagsHeader_ShouldApplyItToTheFeatureFlagState()
    {
        // Arrange
        var featureFlagState = new FeatureFlagState();
        var handler = new StubHttpMessageHandler((_, _) =>
            {
                var response = StubHttpMessageHandler.CreateResponse(HttpStatusCode.NoContent);
                response.Headers.Add(AccountApiHeaders.UserFeatureFlags, "compact-view");
                return Task.FromResult(response);
            }
        );
        var chain = new FeatureFlagsHeaderHandler(featureFlagState) { InnerHandler = handler };
        var client = new FeatureFlagsClient(StubHttpMessageHandler.CreateHttpClient(chain));
        featureFlagState.Initialize(CreateAuthenticatedBootstrap([]));

        // Act
        await client.SetUserOverrideAsync("compact-view", new SetUserFeatureFlagCommand { Enabled = true }, CancellationToken.None);

        // Assert
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeTrue();
    }

    private static BootstrapResponse CreateAuthenticatedBootstrap(string[] featureFlags)
    {
        var user = new BootstrapUser(UserId.NewId(), new TenantId(1), "Owner", "owner@example.com", "Ada", "Lovelace", null, null, "Acme", null, "Free", false, featureFlags);
        return new BootstrapResponse(true, user, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool>(), "antiforgery-token");
    }
}
