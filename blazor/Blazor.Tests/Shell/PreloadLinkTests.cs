using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Blazor.Host.Shell;
using Blazor.Tests.Account;
using FluentAssertions;

namespace Blazor.Tests.Shell;

[Collection(HostCollection.Name)]
public sealed partial class PreloadLinkTests(HostFixture fixture)
{
    // An interactive surface three segments below the path base, where a document-relative URL would resolve inside the page path
    private const string DeepInteractivePath = "blazor/development/form-errors/interactive";

    [Fact]
    public async Task DeepInteractivePage_ShouldRenderRootAbsoluteKeyedPreloadLinksWithAnAsValue()
    {
        // Act
        var html = await GetDeepInteractivePageAsync();

        // Assert
        var preloadLinks = GetLinkElements(html).Where(link => GetAttribute(link, "rel") is "preload" or "modulepreload").ToArray();
        preloadLinks.Should().NotBeEmpty();
        foreach (var link in preloadLinks)
        {
            var href = GetAttribute(link, "href");
            href.Should().StartWith("/blazor/", link);
            GetAttribute(link, "as").Should().NotBeNullOrEmpty(link);
            GetAttribute(link, "data-permanent").Should().Be(href, link);
        }
    }

    [Fact]
    public async Task DeepInteractivePage_ShouldNeverLinkTheFrameworkRuntimeAsStylesheet()
    {
        // Act
        var html = await GetDeepInteractivePageAsync();

        // Assert
        var stylesheetLinks = GetLinkElements(html).Where(link => GetAttribute(link, "rel") == "stylesheet").ToArray();
        stylesheetLinks.Should().NotBeEmpty();
        foreach (var href in stylesheetLinks.Select(link => GetAttribute(link, "href")))
        {
            href.Should().StartWith("/blazor/").And.NotContain("_framework/").And.NotContain(".js");
        }
    }

    [Fact]
    public async Task ScopedStylesheetBundle_ShouldNameOnlyRootAbsolutePreloadsThatResolve()
    {
        // Arrange
        var html = await GetDeepInteractivePageAsync();
        var bundleHref = GetLinkElements(html).Select(link => GetAttribute(link, "href")).Single(href => href != null && href.EndsWith(".styles.css"));

        // Act
        using var response = await fixture.Client.GetAsync(bundleHref![1..]);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Link", out var linkHeaders).Should().BeTrue();
        var targets = linkHeaders!.SelectMany(header => LinkTargetPattern().Matches(header)).Select(match => match.Groups[1].Value).ToArray();
        targets.Should().Contain(target => target.EndsWith(".bundle.scp.css"));
        foreach (var target in targets)
        {
            target.Should().StartWith("/blazor/_content/");
            using var preloadResponse = await fixture.Client.GetAsync(target[1..]);
            preloadResponse.StatusCode.Should().Be(HttpStatusCode.OK, target);
        }
    }

    [Theory]
    [InlineData("/blazor/Blazor.Host.styles.css", "<_content/A/A.bundle.scp.css>; rel=\"preload\"; as=\"style\", <_content/B/B.bundle.scp.css>; rel=\"preload\"; as=\"style\"", "</blazor/_content/A/A.bundle.scp.css>; rel=\"preload\"; as=\"style\", </blazor/_content/B/B.bundle.scp.css>; rel=\"preload\"; as=\"style\"")]
    [InlineData("/blazor/_content/A/css/site.css", "<../fonts/a.woff2?v=1>; rel=preload; as=font", "</blazor/_content/A/fonts/a.woff2?v=1>; rel=preload; as=font")]
    [InlineData("/blazor/app.css", "</blazor/x.css>; rel=preload, <https://cdn.example.com/y.css>; rel=preload", "</blazor/x.css>; rel=preload, <https://cdn.example.com/y.css>; rel=preload")]
    [InlineData("/blazor/app.css", "rel=preload", "rel=preload")]
    public void ToRootAbsoluteLinkHeader_WhenTargetIsRelative_ShouldResolveAgainstTheResponseUrl(string responsePath, string header, string expected)
    {
        // Act
        var rewritten = HostShell.ToRootAbsoluteLinkHeader(header, responsePath);

        // Assert
        rewritten.Should().Be(expected);
    }

    private async Task<string> GetDeepInteractivePageAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, DeepInteractivePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.CreateToken("preload@example.com"));
        using var response = await fixture.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static IEnumerable<string> GetLinkElements(string html)
    {
        return LinkElementPattern().Matches(html).Select(match => match.Value);
    }

    private static string? GetAttribute(string element, string name)
    {
        var match = Regex.Match(element, $"\\s{Regex.Escape(name)}=\"([^\"]*)\"");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    [GeneratedRegex("<link\\s[^>]*>")]
    private static partial Regex LinkElementPattern();

    [GeneratedRegex("<([^>]*)>")]
    private static partial Regex LinkTargetPattern();
}
