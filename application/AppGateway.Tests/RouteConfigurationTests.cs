using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace AppGateway.Tests;

public sealed class RouteConfigurationTests(AppGatewayApplicationFactory factory) : IClassFixture<AppGatewayApplicationFactory>
{
    [Theory]
    [InlineData("account-api")]
    [InlineData("account-federation")]
    [InlineData("account-static")]
    [InlineData("account-hmr")]
    [InlineData("main-static")]
    [InlineData("main-hmr")]
    [InlineData("blazor")]
    [InlineData("main")]
    public void AppRoutes_ShouldDeclareAppHostnameKey(string routeId)
    {
        // Act
        var route = GetRoute(routeId);

        // Assert
        route.Metadata.Should().ContainKey("HostnameKey").WhoseValue.Should().Be("App");
    }

    [Theory]
    [InlineData("favicon")]
    [InlineData("apple-touch-icon")]
    [InlineData("manifest")]
    [InlineData("robots")]
    [InlineData("tracking")]
    [InlineData("avatars")]
    [InlineData("logos")]
    [InlineData("account-legal-documents")]
    public void AssetRoutes_ShouldDeclareWildcardHostnameKey(string routeId)
    {
        // Act
        var route = GetRoute(routeId);

        // Assert
        route.Metadata.Should().ContainKey("HostnameKey").WhoseValue.Should().Be("*");
    }

    [Fact]
    public void EveryConfiguredRoute_ShouldDeclareHostnameKeyMetadata()
    {
        // Arrange
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IProxyConfigProvider>();

        // Act
        var routes = provider.GetConfig().Routes;

        // Assert
        routes.Should().NotBeEmpty();
        foreach (var route in routes)
        {
            route.Metadata.Should().NotBeNull($"route '{route.RouteId}' must declare metadata");
            route.Metadata.Should().ContainKey("HostnameKey", $"route '{route.RouteId}' must declare a HostnameKey");
        }
    }

    [Fact]
    public void NoRoute_ShouldReferenceBackOffice()
    {
        // Arrange -- AppGateway must remain unaware of back-office in both Azure and localhost.
        // Production routes back-office traffic to its own ACA container app via DNS; localhost
        // routes it directly to account-api on a dedicated Kestrel port. Either path proves
        // wrong if AppGateway grows a back-office route by mistake.
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IProxyConfigProvider>();

        // Act
        var routes = provider.GetConfig().Routes;
        var hostnameKeys = routes.Select(r => r.Metadata?.GetValueOrDefault("HostnameKey")).ToArray();

        // Assert
        routes.Should().NotContain(r => r.RouteId.Contains("back-office", StringComparison.OrdinalIgnoreCase));
        hostnameKeys.Should().NotContain("BackOffice");
    }

    private RouteConfig GetRoute(string routeId)
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IProxyConfigProvider>();
        var route = provider.GetConfig().Routes.SingleOrDefault(r => r.RouteId == routeId);
        route.Should().NotBeNull($"route '{routeId}' should exist in AppGateway configuration");
        return route;
    }
}
