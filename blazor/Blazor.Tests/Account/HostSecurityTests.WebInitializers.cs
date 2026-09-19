using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The JavaScript initializers the host names in every document. blazor.web.js imports each of them on every page, public
// static pages included, so the component library's browser bundle must not be among them: it is 445 KB on disk and about
// 105 KB compressed, and no page outside the authenticated WebAssembly surface uses a library component. The two targets in
// Blazor.Host.csproj remove it from the JS module manifest; these tests fail if a future SDK renames the items they filter.
// Part of HostSecurityTests so it shares the one host the fixture starts.
public sealed partial class HostSecurityTests
{
    private const string ComponentLibraryInitializerPath =
        "_content/Microsoft.FluentUI.AspNetCore.Components/Microsoft.FluentUI.AspNetCore.Components.lib.module.js";

    [Theory]
    [InlineData("blazor/")]
    [InlineData("blazor/login")]
    [InlineData("blazor/legal/terms")]
    public async Task StaticPublicPage_WhenRendered_ShouldNameNoComponentLibraryWebInitializer(string path)
    {
        // Act
        using var response = await fixture.Client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var initializers = ReadWebInitializers(html);
        // The host's own initializer proves the list was read, so the assertion below cannot pass on an unparsed document
        initializers.Should().ContainSingle(initializer => initializer.Contains("Blazor.Host", StringComparison.Ordinal) && initializer.EndsWith(".lib.module.js", StringComparison.Ordinal));
        initializers.Should().NotContain(initializer => initializer.Contains("FluentUI", StringComparison.Ordinal));
        html.Should().NotContain(ComponentLibraryInitializerPath);
    }

    [Fact]
    public async Task ComponentLibraryInitializer_WhenRequested_ShouldStillBeServedForTheInteractiveSurface()
    {
        // wwwroot/js/Blazor.Host.lib.module.js imports this file with a path relative to itself, on an interactive surface
        // and on a WebAssembly runtime start, so the authenticated surface still gets the library

        // Act
        using var response = await fixture.Client.GetAsync($"blazor/{ComponentLibraryInitializerPath}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("blazor/", false)]
    [InlineData("blazor/legal/terms", false)]
    [InlineData("blazor/app", true)]
    [InlineData("blazor/account/users", true)]
    public async Task HostPage_WhenRendered_ShouldMarkOnlyAnInteractiveSurfaceForTheComponentLibrary(string path, bool isInteractiveSurface)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (isInteractiveSurface) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("surface@example.com"));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Contains("data-interactive-surface", StringComparison.Ordinal).Should().Be(isInteractiveSurface);
    }

    // The list blazor.web.js reads from the document: an HTML comment holding the base64 of the host's JS module manifest.
    // An absent comment means the host names no web initializer at all, which is the empty list.
    private static string[] ReadWebInitializers(string html)
    {
        var match = Regex.Match(html, "<!--\\s*Blazor-Web-Initializers:(?<initializers>[a-zA-Z0-9+/=]+)-->");
        if (!match.Success) return [];

        var json = Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups["initializers"].Value));
        return JsonSerializer.Deserialize<string[]>(json) ?? [];
    }
}
