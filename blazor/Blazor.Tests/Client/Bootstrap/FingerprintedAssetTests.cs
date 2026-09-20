using System.Text.Json;
using Blazor.Client;
using Blazor.Client.Bootstrap;
using FluentAssertions;

namespace Blazor.Tests.Client.Bootstrap;

// The routes are real ones from a trimmed Release publish's Blazor.Host.staticwebassets.endpoints.json, so the predicate is
// measured against the fingerprints the static web asset pipeline actually produces. The last two cases go further and hold
// the predicate against this build's own endpoint manifest, route by route, so the shape is read from the pipeline rather
// than guessed from a sample: a fingerprint that the predicate rejects would leave the asset unwatched by StaleAssetProbe
// and unrecognized by the host's cache test.
public sealed class FingerprintedAssetTests
{
    private const string EndpointManifestFileName = "Blazor.Host.staticwebassets.endpoints.json";

    [Theory]
    [InlineData("/blazor/_framework/Blazor.Client.6kbltrhlw8.wasm")]
    [InlineData("/blazor/_framework/Account.Contracts.4aysphzjbn.wasm")]
    [InlineData("/blazor/_framework/Microsoft.AspNetCore.Components.Forms.kk65edyqj9.wasm")]
    [InlineData("/blazor/Blazor.Host.43ydukkefa.modules.json")]
    [InlineData("/blazor/Blazor.Host.5ae972z2ll.styles.css")]
    [InlineData("/blazor/_content/Microsoft.AspNetCore.Components.QuickGrid/Microsoft.AspNetCore.Components.QuickGrid.i2w4e4ntkp.bundle.scp.css")]
    [InlineData("/blazor/_framework/Blazor.Client.6kbltrhlw8.wasm.br")]
    [InlineData("https://app.dev.localhost:9000/blazor/_framework/Blazor.Client.6kbltrhlw8.wasm")]
    [InlineData("/blazor/js/shell.9ab3cd12fe.js?v=1")]
    // A fingerprint of ten letters and no digit: 17 of the 271 distinct fingerprints of this build carry no digit
    [InlineData("/blazor/_framework/Microsoft.AspNetCore.Authorization.zsiysggeka.wasm")]
    [InlineData("/blazor/_framework/dotnet.native.daihnlzyds.wasm")]
    [InlineData("/blazor/js/document-base-uri.xtnnvhffqm.js")]
    [InlineData("/blazor/images/mitid-logo-white.apddeezbwc.svg")]
    public void IsFingerprinted_WhenRouteCarriesAFingerprint_ShouldBeTrue(string url)
    {
        // Act
        var isFingerprinted = FingerprintedAsset.IsFingerprinted(url);

        // Assert
        isFingerprinted.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/blazor/_framework/blazor.boot.json")]
    [InlineData("/blazor/_framework/blazor.web.js")]
    [InlineData("/blazor/js/unsaved-changes.js")]
    [InlineData("/blazor/js/preferred-tenant.js")]
    [InlineData("/blazor/Blazor.Host.styles.css")]
    [InlineData("/blazor/_content/Microsoft.FluentUI.AspNetCore.Components/Components/DataGrid/FluentDataGrid.razor.js")]
    [InlineData("/blazor/app.css")]
    [InlineData("/blazor/app/users")]
    // Nine characters, and eleven: only the length the pipeline emits is a fingerprint
    [InlineData("/blazor/_framework/Blazor.Client.6kbltrhlw.wasm")]
    [InlineData("/blazor/_framework/Blazor.Client.6kbltrhlw8x.wasm")]
    // The API and an avatar on the storage account are outside this edition's asset set, whatever their file names look
    // like; the observer in stale-assets.js has already dropped anything from another origin
    [InlineData("/api/account/users/usr_01jz8q4n6v3k2m7p9r5t0w1xyz")]
    [InlineData("/avatars/usr_01jz8q4n6v3k2m7p9r5t0w1xyz/a1b2c3d4e5.png")]
    public void IsFingerprinted_WhenRouteCarriesNoFingerprintOfThisEdition_ShouldBeFalse(string? url)
    {
        // Act
        var isFingerprinted = FingerprintedAsset.IsFingerprinted(url);

        // Assert
        isFingerprinted.Should().BeFalse();
    }

    [Fact]
    public void SelectWatchable_ShouldPreferTheClientAssemblyOverAssetsTwoPublishesCanShare()
    {
        // Arrange
        var urls = new[]
        {
            "https://app.dev.localhost:9000/blazor/app.5ae972z2ll.css",
            "https://app.dev.localhost:9000/blazor/_framework/blazor.web.4dnn1s8ipq.js",
            "https://app.dev.localhost:9000/blazor/_framework/System.Private.CoreLib.7hs2vkz9qq.wasm",
            "https://app.dev.localhost:9000/blazor/_framework/Blazor.Client.6kbltrhlw8.wasm",
            "https://app.dev.localhost:9000/blazor/js/unsaved-changes.js"
        };

        // Act
        var watchable = FingerprintedAsset.SelectWatchable(urls);

        // Assert
        watchable.Should().Be("https://app.dev.localhost:9000/blazor/_framework/Blazor.Client.6kbltrhlw8.wasm");
    }

    [Fact]
    public void SelectWatchable_WhenNoUrlCarriesAFingerprint_ShouldBeNull()
    {
        // Act
        var watchable = FingerprintedAsset.SelectWatchable(["/blazor/app.css", "/blazor/js/shell.js", "/api/account/bootstrap"]);

        // Assert
        watchable.Should().BeNull();
    }

    [Fact]
    public void IsFingerprinted_AgainstEveryRouteOfTheBuildsEndpointManifest_ShouldAgreeWithThePipeline()
    {
        // Arrange
        var endpoints = ReadEndpointManifest();

        // Act
        var disagreements = endpoints
            .Where(endpoint => FingerprintedAsset.IsFingerprinted($"{AppUrls.PathBase}/{endpoint.Route}") != endpoint.IsFingerprinted)
            .Select(endpoint => $"{endpoint.Route} carries {(endpoint.IsFingerprinted ? "a fingerprint" : "none")}")
            .Distinct()
            .ToArray();

        // Assert
        endpoints.Should().Contain(endpoint => endpoint.IsFingerprinted).And.Contain(endpoint => !endpoint.IsFingerprinted);
        disagreements.Should().BeEmpty();
    }

    [Fact]
    public void EveryFingerprintOfTheBuildsEndpointManifest_ShouldHaveTheOneShapeThePredicateExpects()
    {
        // Arrange
        var fingerprints = ReadEndpointManifest().Where(endpoint => endpoint.IsFingerprinted).Select(endpoint => endpoint.Fingerprint!).Distinct().ToArray();

        // Act
        var lengths = fingerprints.Select(fingerprint => fingerprint.Length).Distinct().ToArray();

        // Assert
        fingerprints.Should().NotBeEmpty();
        lengths.Should().Equal(10);
        fingerprints.Should().OnlyContain(fingerprint => fingerprint.All(character => char.IsAsciiDigit(character) || char.IsAsciiLetterLower(character)));
    }

    // The endpoint manifest of the host this test project references, which names each route's fingerprint where the
    // pipeline gave it one
    private static PublishedEndpoint[] ReadEndpointManifest()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, EndpointManifestFileName);
        File.Exists(manifestPath).Should().BeTrue(manifestPath);

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return manifest.RootElement.GetProperty("Endpoints").EnumerateArray()
            .Select(endpoint => new PublishedEndpoint(
                    endpoint.GetProperty("Route").GetString()!,
                    endpoint.GetProperty("EndpointProperties").EnumerateArray()
                        .Where(property => property.GetProperty("Name").GetString() == "fingerprint")
                        .Select(property => property.GetProperty("Value").GetString())
                        .FirstOrDefault()
                )
            )
            .ToArray();
    }

    private sealed record PublishedEndpoint(string Route, string? Fingerprint)
    {
        public bool IsFingerprinted => Fingerprint is not null;
    }
}
