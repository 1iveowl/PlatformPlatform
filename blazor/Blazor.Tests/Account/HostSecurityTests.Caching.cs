using System.Net;
using System.Text.RegularExpressions;
using Blazor.Client;
using Blazor.Client.Bootstrap;
using Blazor.Host.Shell;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Blazor.Tests.Account;

// The cache boundary the release policy rests on (docs/blazor-version-policy.md and docs/blazor-recovery-runbook.md): a
// document carries per-request user information, an antiforgery token and a nonce, so it is never stored; only an asset
// whose route carries a content fingerprint may be kept for a year, because a deployment gives it a new route. The
// manifest names those routes and is revalidated. The same separation is measured on the Production publish by
// blazor/tests/release-rehearsal.mjs.
public sealed partial class HostSecurityTests
{
    [Theory]
    [InlineData("blazor/")]
    [InlineData("blazor/login")]
    [InlineData("blazor/signup")]
    [InlineData("blazor/legal")]
    public async Task Document_ShouldBeNeitherStoredNorReused(string path)
    {
        // Arrange
        var client = fixture.Client;

        // Act
        using var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertIsNeverStored(response);
        response.Headers.Pragma.Should().ContainSingle().Which.Name.Should().Be("no-cache");
    }

    [Fact]
    public async Task AuthenticatedDocument_ShouldBeNeitherStoredNorReused()
    {
        // Arrange
        var client = fixture.Client;

        // Act
        using var response = await GetAuthenticatedPageAsync(client, fixture.CreateToken("caching@example.com"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertIsNeverStored(response);
    }

    [Fact]
    public async Task Manifest_ShouldBeRevalidatedAndNeverImmutable()
    {
        // Arrange
        var client = fixture.Client;

        // Act
        using var response = await client.GetAsync($"blazor{HostShell.ManifestPath}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
        response.Headers.CacheControl.NoStore.Should().BeFalse();
        ReadCacheControl(response).Should().NotContain("immutable");
    }

    [Fact]
    public async Task BrandStylesheet_ShouldBeImmutableBecauseItsUrlCarriesItsContentVersion()
    {
        // Arrange
        var client = fixture.Client;
        var brandStylesheetUrl = fixture.HostServices.GetRequiredService<HostShell>().BrandStylesheetUrl;

        // Act
        using var response = await client.GetAsync(brandStylesheetUrl[1..]);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        brandStylesheetUrl.Should().Contain("?v=");
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
        ReadCacheControl(response).Should().Contain("immutable");
    }

    [Fact]
    public async Task StaticAssets_ShouldBeImmutableOnlyWhereTheRouteCarriesAFingerprint()
    {
        // Arrange
        var client = fixture.Client;
        var html = await (await client.GetAsync("blazor/login")).Content.ReadAsStringAsync();
        var assetPaths = StylesheetHref().Matches(html).Select(match => match.Groups[1].Value)
            .Where(href => href.StartsWith($"{AppUrls.PathBase}/", StringComparison.Ordinal) && !href.Contains("?v="))
            .Distinct()
            .ToArray();

        // Act
        var cachePolicies = await Task.WhenAll(assetPaths.Select(async path =>
                {
                    using var response = await client.GetAsync(path[1..]);
                    return (Path: path, response.StatusCode, CacheControl: ReadCacheControl(response));
                }
            )
        );

        // Assert
        cachePolicies.Should().NotBeEmpty();
        foreach (var policy in cachePolicies)
        {
            policy.StatusCode.Should().Be(HttpStatusCode.OK, policy.Path);
            if (FingerprintedAsset.IsFingerprinted(policy.Path))
            {
                policy.CacheControl.Should().Contain("immutable", policy.Path);
            }
            else
            {
                policy.CacheControl.Should().NotContain("immutable", policy.Path).And.NotContain("no-store", policy.Path);
            }
        }
    }

    private static void AssertIsNeverStored(HttpResponseMessage response)
    {
        var cacheControl = response.Headers.CacheControl!;
        cacheControl.NoStore.Should().BeTrue();
        cacheControl.NoCache.Should().BeTrue();
        cacheControl.MustRevalidate.Should().BeTrue();
    }

    private static string ReadCacheControl(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(HeaderNames.CacheControl, out var values) ? string.Join(", ", values) : string.Empty;
    }

    [GeneratedRegex("<link[^>]*rel=\"stylesheet\"[^>]*href=\"([^\"]+)\"")]
    private static partial Regex StylesheetHref();
}
