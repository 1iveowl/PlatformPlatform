using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Blazor.Tests.Account;

// The legal index and the three documents through the real host: the policy header and the nonce on every script and
// stylesheet, the English document text inside chrome of the request's culture, and nothing served for a path that is not
// one of the three documents. Part of HostSecurityTests so it shares the one host the fixture starts.
public sealed partial class HostSecurityTests
{
    [Theory]
    [InlineData("blazor/legal", "en-US", "Legal and Compliance")]
    // The documents link to "/legal/", which the renderer rewrites under the path base, so the trailing slash resolves too
    [InlineData("blazor/legal/", "en-US", "Legal and Compliance")]
    [InlineData("blazor/legal", "da-DK", "Juridisk og compliance")]
    [InlineData("blazor/legal/terms", "en-US", "Terms of Service")]
    [InlineData("blazor/legal/privacy", "da-DK", "Privacy Policy")]
    [InlineData("blazor/legal/dpa", "da-DK", "Data Processing Agreement")]
    public async Task LegalPage_WhenAnonymous_ShouldRenderWithThePolicyHeaderAndANoncedDocument(string path, string culture, string heading)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle(policy => policy.Contains("'nonce-") && !policy.Contains("unsafe-inline"));
        html.Should().Contain(PublicNavigationMarker).And.Contain($">{heading}</h").And.Contain($"<html lang=\"{culture}\"");
        NoncelessElements(html).Should().BeEmpty();
    }

    [Theory]
    [InlineData("blazor/legal/terms")]
    [InlineData("blazor/legal/privacy")]
    [InlineData("blazor/legal/dpa")]
    public async Task LegalDocument_WhenChromeIsDanish_ShouldMarkTheEnglishTextWithItsOwnLanguage(string path)
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("da-DK"));

        // Act
        using var response = await fixture.Client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("<html lang=\"da-DK\"").And.Contain("<article class=\"legal-document\" lang=\"en-US\">");
        // The chrome around the document is Danish: the footer's legal links are the Danish ones
        WebUtility.HtmlDecode(html).Should().Contain(">Vilkår<").And.Contain(">Privatliv<");
        html.Should().Contain("<h1>").And.NotContain("<script>").And.NotContain("style=\"");
    }

    [Theory]
    [InlineData("blazor/legal/unknown")]
    [InlineData("blazor/legal/terms.en-US")]
    [InlineData("blazor/legal/cross-references.internal")]
    [InlineData("blazor/legal/legitimate-interest-assessment.internal.en-US")]
    [InlineData("blazor/legal/documents/terms.md")]
    [InlineData("blazor/legal/terms/extra")]
    public async Task LegalDocument_WhenPathNamesNoPublishedDocument_ShouldNotBeFound(string path)
    {
        // Act
        using var response = await fixture.Client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        html.Should().NotContain("Terms of Service").And.NotContain("Cross-reference");
    }

    // Every script and stylesheet element of the document, minus the ones that carry the request nonce
    private static string[] NoncelessElements(string html)
    {
        return Regex.Matches(html, "<(script|link)\\b[^>]*>")
            .Select(match => match.Value)
            .Where(element => !element.Contains("nonce=\"") && !element.Contains("rel=\"manifest\"") && !element.Contains("rel=\"modulepreload\"") && !element.Contains("rel=\"preload\""))
            .ToArray();
    }
}
