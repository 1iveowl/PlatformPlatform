using System.Net;
using System.Net.Http.Headers;
using Blazor.Client;
using Blazor.Host.Shell;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.Tests.Account;

// The offline shell and its service worker through the real host. Two properties carry the whole design: the one document a
// cache may keep carries nothing that names a request or a user, and the worker's navigation allowlist is exactly the
// authenticated surface. The browser side is blazor/tests/offline-shell.mjs.
public sealed partial class HostSecurityTests
{
    private const string OfflineShellPath = "blazor/app/offline";

    [Fact]
    public async Task OfflineShell_WhenRequested_ShouldBeStorableAndCarryNoNonceOrAntiforgeryToken()
    {
        // Arrange
        var client = fixture.Client;

        // Act
        using var response = await client.GetAsync(OfflineShellPath);
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Storable, so the worker may keep it, and revalidated, so a deployment's shell replaces it
        response.Headers.CacheControl!.NoStore.Should().BeFalse();
        response.Headers.CacheControl.NoCache.Should().BeTrue();
        response.Headers.CacheControl.MustRevalidate.Should().BeTrue();
        response.Headers.Contains("Pragma").Should().BeFalse();

        // A per-request secret frozen into a stored document would name a request that is over
        html.Should().NotContain("nonce=");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().NotContain("'nonce-");
        html.Should().NotContain("__RequestVerificationToken").And.NotContain("<script type=\"importmap\"");
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeFalse($"the shell set {string.Join(", ", cookies ?? [])}");
    }

    [Fact]
    public async Task OfflineShell_WhenRequestedByASignedInUser_ShouldRenderNoUserOrTenantValue()
    {
        // Arrange
        const string email = "offline@example.com";
        using var request = new HttpRequestMessage(HttpMethod.Get, OfflineShellPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken(email, firstName: "Offline"));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("data-testid=\"offline-page\"").And.Contain("You are offline");
        html.Should().NotContain(email).And.NotContain("Offline User").And.NotContain($"tenant-of-{email}").And.NotContain(HostFixture.TenantIdClaimValue);
        html.Should().NotContain(ShellMarker);
    }

    [Theory]
    [InlineData("en-US", "You are offline")]
    [InlineData("da-DK", "Du er offline")]
    public async Task OfflineShell_WhenRequested_ShouldRenderTheCultureOfTheRequest(string culture, string heading)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, OfflineShellPath);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain($"<h1>{heading}</h1>");
    }

    [Fact]
    public async Task ServiceWorker_WhenRequested_ShouldBeRevalidatedAndScopedToThePathBase()
    {
        // Arrange
        var client = fixture.Client;

        // Act
        using var response = await client.GetAsync($"blazor{OfflineShell.WorkerPath}");
        var script = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
        response.Headers.GetValues("Service-Worker-Allowed").Single().Should().Be($"{AppUrls.PathBase}/");
        // A worker under its own policy could not carry a page's nonce, so it is served without one
        response.Headers.Contains("Content-Security-Policy").Should().BeFalse();

        // The four values the host decides are substituted, so no placeholder reaches a browser
        script.Should().NotContain("__PATH_BASE__").And.NotContain("__SHELL_DOCUMENT__").And.NotContain("__APP_SEGMENTS__").And.NotContain("__CACHE_VERSION__");
        script.Should().Contain($"\"{AppUrls.ToAbsolute(OfflineShell.ShellDocumentPath)}\"");
    }

    [Fact]
    public async Task ServiceWorker_WhenServedByTheStaticAssetPipeline_ShouldNotBeReachableAtASecondAddress()
    {
        // Arrange
        var client = fixture.Client;

        // Act
        using var response = await client.GetAsync("blazor/wwwroot/service-worker.js");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StaticPublicPage_WhenRendered_ShouldNeitherMarkAnInteractiveSurfaceNorNameTheRegistrationModule()
    {
        // Arrange
        var client = fixture.Client;

        // Act
        var documents = await Task.WhenAll(new[] { "blazor/", "blazor/login", "blazor/signup", "blazor/legal", OfflineShellPath }
            .Select(async path => (Path: path, Html: await client.GetStringAsync(path)))
        );

        // Assert
        foreach (var document in documents)
        {
            // The marker is what makes the host's JavaScript initializer import the registration module at all
            document.Html.Should().NotContain("data-interactive-surface", document.Path);
            // The import map maps every static module of the edition, on every page; what must not appear is a script element
            document.Html.Should().NotContain("src=\"/blazor/js/service-worker", document.Path).And.NotContain("serviceWorker", document.Path);
        }
    }

    [Fact]
    public void AppNavigationAllowlist_ShouldCoverEveryInteractiveSurfaceRouteAndNoPublicRoute()
    {
        // Arrange
        var endpoints = fixture.HostServices.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ComponentTypeMetadata>() is not null)
            .Where(endpoint => endpoint.Metadata.GetMetadata<DevelopmentOnlyAttribute>() is null)
            .Select(endpoint => (Path: AppUrls.ToAbsolute(endpoint.RoutePattern.RawText!), IsInteractiveSurface: endpoint.Metadata.GetMetadata<InteractiveSurfaceAttribute>() is not null))
            .ToArray();

        // Act
        var covered = endpoints.Where(endpoint => OfflineShell.IsAppNavigation(endpoint.Path)).Select(endpoint => endpoint.Path).ToHashSet(StringComparer.Ordinal);

        // Assert
        endpoints.Should().NotBeEmpty();
        var interactiveSurfaces = endpoints.Where(endpoint => endpoint.IsInteractiveSurface).Select(endpoint => endpoint.Path).ToArray();
        interactiveSurfaces.Should().NotBeEmpty().And.OnlyContain(path => covered.Contains(path));
        // The public surface goes to the network, always
        string[] publicRoutes = ["/", "login", "login/verify", "signup", "signup/verify", "legal", "legal/terms", "error", "not-found"];
        covered.Should().NotIntersectWith(publicRoutes.Select(AppUrls.ToAbsolute));
        // The shell document itself is reached the same way the pages it stands in for are
        OfflineShell.IsAppNavigation(AppUrls.ToAbsolute(OfflineShell.ShellDocumentPath)).Should().BeTrue();
    }
}
