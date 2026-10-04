using Blazor.Host.Shell;
using FluentAssertions;
using Microsoft.AspNetCore.Components;

namespace Blazor.Tests.Shell;

public sealed class AssetLinksTests
{
    [Fact]
    public void GetAbsoluteImportMap_ShouldMakeRelativeSpecifiersAndEveryTargetRootAbsolute()
    {
        // Arrange
        var source = new ImportMapDefinition(
            new Dictionary<string, string> { ["./js/theme.js"] = "./js/theme.abc.js", ["_framework/resource-collection.js"] = "./_framework/resource-collection.def.js" },
            new Dictionary<string, IReadOnlyDictionary<string, string>> { ["./_content/"] = new Dictionary<string, string> { ["./a.js"] = "_content/a.123.js" } },
            new Dictionary<string, string> { ["./js/theme.abc.js"] = "sha256-theme", ["/blazor/_framework/dotnet.js"] = "sha256-dotnet" }
        );

        // Act
        var importMap = new AssetLinks().GetAbsoluteImportMap(source);

        // Assert
        importMap.Imports.Should().Equal(new Dictionary<string, string>
            {
                ["/blazor/js/theme.js"] = "/blazor/js/theme.abc.js",
                ["_framework/resource-collection.js"] = "/blazor/_framework/resource-collection.def.js"
            }
        );
        importMap.Scopes.Should().ContainSingle();
        importMap.Scopes!["/blazor/_content/"].Should().Equal(new Dictionary<string, string> { ["/blazor/a.js"] = "/blazor/_content/a.123.js" });
        importMap.Integrity.Should().Equal(new Dictionary<string, string> { ["/blazor/js/theme.abc.js"] = "sha256-theme", ["/blazor/_framework/dotnet.js"] = "sha256-dotnet" });
    }

    [Fact]
    public void GetAbsoluteImportMap_ForTheSameSource_ShouldReturnTheRewriteItKeptForThatSourceOnly()
    {
        // Arrange
        var assetLinks = new AssetLinks();
        var source = new ImportMapDefinition(new Dictionary<string, string> { ["./a.js"] = "./a.1.js" }, null, null);
        var otherSource = new ImportMapDefinition(new Dictionary<string, string> { ["./a.js"] = "./a.2.js" }, null, null);

        // Act
        var first = assetLinks.GetAbsoluteImportMap(source);
        var second = assetLinks.GetAbsoluteImportMap(source);
        var other = assetLinks.GetAbsoluteImportMap(otherSource);

        // Assert
        second.Should().BeSameAs(first);
        other.Should().NotBeSameAs(first);
        other.Imports!["/blazor/a.js"].Should().Be("/blazor/a.2.js");
        new AssetLinks().GetAbsoluteImportMap(source).Should().NotBeSameAs(first);
    }

    [Fact]
    public void GetPreloadLinks_ShouldListOnlyPreloadAssetsRootAbsoluteWithTheImportMapIntegrity()
    {
        // Arrange
        var assets = new ResourceAssetCollection([
                new ResourceAsset("_framework/dotnet.js", [new ResourceAssetProperty("PreloadRel", "modulepreload"), new ResourceAssetProperty("preloadas", "script"), new ResourceAssetProperty("preloadpriority", "high"), new ResourceAssetProperty("preloadcrossorigin", "anonymous"), new ResourceAssetProperty("preloadorder", "2")]),
                new ResourceAsset("_framework/runtime.wasm", [new ResourceAssetProperty("preloadrel", "preload"), new ResourceAssetProperty("preloadas", "fetch")]),
                new ResourceAsset("app.css", [new ResourceAssetProperty("integrity", "sha256-css")]),
                new ResourceAsset("favicon.ico")
            ]
        );
        var importMap = new ImportMapDefinition(null, null, new Dictionary<string, string> { ["/blazor/_framework/dotnet.js"] = "sha256-dotnet" });

        // Act
        var preloadLinks = AssetLinks.GetPreloadLinks(assets, importMap).ToArray();

        // Assert
        preloadLinks.Should().Equal(
            new PreloadLink("/blazor/_framework/dotnet.js", "modulepreload", "script", "high", "anonymous", "sha256-dotnet", 2),
            new PreloadLink("/blazor/_framework/runtime.wasm", "preload", "fetch", null, null, null, int.MaxValue)
        );
    }
}
