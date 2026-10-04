using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blazor.Tests.Account;
using FluentAssertions;

namespace Blazor.Tests.Shell;

// The document headers through the real host in Development, as HostFixture configures it: the full policy of an app page
// and of a back-office page with the trusted hosts each surface names, the per-request nonce the header and the document
// share, and the root-absolute import map every nonced page renders.
[Collection(HostCollection.Name)]
public sealed partial class PagePolicyTests(HostFixture fixture)
{
    private const string BackOfficePage = "blazor/back-office";

    [Fact]
    public async Task AppPage_WhenServed_ShouldCarryThePolicyOfThePublicAndCdnUrlsWithItsNonce()
    {
        // Act
        using var response = await fixture.Client.GetAsync("blazor/login");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        var nonce = NonceSource().Match(policy).Groups[1].Value;
        var cdnUrl = Environment.GetEnvironmentVariable("CDN_URL") ?? "";
        var trustedHosts = $"https://{HostFixture.PublicHost} {cdnUrl} wss://app.dev.localhost:* https://app.dev.localhost:*";
        policy.Should().Be(ExpectedPolicy(trustedHosts, nonce));
        policy.Should().NotContain("back-office");
    }

    [Fact]
    public async Task BackOfficePage_WhenServed_ShouldCarryThePolicyOfTheBackOfficeOriginAloneWithItsNonce()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest(BackOfficePage, fixture.ProtectBackOfficeIdentity("Admin", true));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        var nonce = NonceSource().Match(policy).Groups[1].Value;
        var trustedHosts = $"https://{HostFixture.BackOfficeHost} wss://back-office.dev.localhost:* https://back-office.dev.localhost:*";
        policy.Should().Be(ExpectedPolicy(trustedHosts, nonce));
        policy.Should().NotContain(HostFixture.PublicHost).And.NotContain("app.dev.localhost");
    }

    [Fact]
    public async Task Page_WhenServedTwice_ShouldCarryAFreshSixteenByteNonceThatEveryNoncedElementShares()
    {
        // Act
        var first = await GetLoginPageAsync();
        var second = await GetLoginPageAsync();

        // Assert
        first.Nonce.Should().NotBe(second.Nonce);
        foreach (var (nonce, html) in new[] { first, second })
        {
            Convert.FromBase64String(nonce).Should().HaveCount(16);
            var elementNonces = NonceAttribute().Matches(html).Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)).ToArray();
            elementNonces.Should().NotBeEmpty().And.OnlyContain(elementNonce => elementNonce == nonce);
        }
    }

    [Fact]
    public async Task Page_WhenServed_ShouldRenderAnImportMapWithRootAbsoluteRelativeSpecifiersOnly()
    {
        // Act
        var (_, html) = await GetLoginPageAsync();

        // Assert
        using var importMap = JsonDocument.Parse(ImportMapScript().Match(html).Groups[1].Value);
        var imports = importMap.RootElement.GetProperty("imports").EnumerateObject().ToArray();
        imports.Should().NotBeEmpty();
        foreach (var entry in imports)
        {
            entry.Name.Should().NotStartWith("./");
            entry.Value.GetString().Should().StartWith("/blazor/", entry.Name);
        }

        importMap.RootElement.GetProperty("integrity").EnumerateObject().Select(entry => entry.Name).Should().OnlyContain(name => name.StartsWith("/blazor/"));
    }

    private static string ExpectedPolicy(string trustedHosts, string nonce)
    {
        return string.Join(";",
            $"script-src {trustedHosts} 'nonce-{nonce}' 'strict-dynamic' 'wasm-unsafe-eval' https:",
            $"script-src-elem {trustedHosts} 'nonce-{nonce}'",
            $"style-src {trustedHosts} 'nonce-{nonce}'",
            $"style-src-elem {trustedHosts} 'nonce-{nonce}'",
            $"default-src {trustedHosts}",
            $"connect-src {trustedHosts}",
            "frame-src 'none'",
            $"img-src {trustedHosts} data: blob:",
            "object-src 'none'",
            "base-uri 'none'",
            "worker-src 'self'"
        );
    }

    private async Task<(string Nonce, string Html)> GetLoginPageAsync()
    {
        using var response = await fixture.Client.GetAsync("blazor/login");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        return (NonceSource().Match(policy).Groups[1].Value, await response.Content.ReadAsStringAsync());
    }

    [GeneratedRegex("'nonce-([^']+)'")]
    private static partial Regex NonceSource();

    [GeneratedRegex("\\snonce=\"([^\"]*)\"")]
    private static partial Regex NonceAttribute();

    [GeneratedRegex("<script type=\"importmap\"[^>]*>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex ImportMapScript();
}
