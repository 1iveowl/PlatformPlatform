using Blazor.Client.Bootstrap;
using FluentAssertions;

namespace Blazor.Tests.Client.Bootstrap;

// The routes are real ones from a trimmed Release publish's Blazor.Host.staticwebassets.endpoints.json, so the predicate is
// measured against the fingerprints the static web asset pipeline actually produces
public sealed class FingerprintedAssetTests
{
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
}
