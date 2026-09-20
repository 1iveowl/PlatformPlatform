using System.Text.Json;
using Blazor.Host.Shell;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Blazor.Tests.Shell;

public sealed class ShellTests
{
    [Fact]
    public void BuildContentSecurityPolicy_ShouldHaveNoUnsafeSourceAndTheWorkerDirective()
    {
        // Arrange
        var hostShell = new HostShell(new TestWebHostEnvironment(Environments.Production));

        // Act
        var policy = hostShell.BuildContentSecurityPolicy("test-nonce");

        // Assert
        var directives = policy.Split(';');
        directives.Should().Contain(["base-uri 'none'", "object-src 'none'", "frame-src 'none'", "worker-src 'self'"]);
        directives.Should().NotContain(directive => directive.StartsWith("form-action"));
        directives.Should().ContainSingle(directive => directive.StartsWith("script-src ") && directive.Contains("'nonce-test-nonce'") && directive.Contains("'wasm-unsafe-eval'"));
        policy.Should().NotContain("'unsafe-inline'").And.NotContain("'unsafe-eval'").And.NotContain("style-src-attr");
    }

    [Fact]
    public void BuildContentSecurityPolicy_WithoutANonce_ShouldDropOnlyTheNonceSource()
    {
        // Arrange
        var hostShell = new HostShell(new TestWebHostEnvironment(Environments.Production));

        // Act
        var withNonce = hostShell.BuildContentSecurityPolicy("test-nonce");
        var withoutNonce = hostShell.BuildContentSecurityPolicy(null);

        // Assert
        withoutNonce.Should().NotContain("'nonce-");
        withoutNonce.Should().Be(withNonce.Replace(" 'nonce-test-nonce'", ""));
        withoutNonce.Split(';').Should().Contain(["base-uri 'none'", "object-src 'none'", "frame-src 'none'", "worker-src 'self'"]);
    }

    [Fact]
    public void BuildWorkerScript_ShouldSubstituteEveryValueTheHostDecides()
    {
        // Act
        var script = OfflineShell.BuildWorkerScript();

        // Assert
        script.Should().NotContain("__PATH_BASE__").And.NotContain("__SHELL_DOCUMENT__").And.NotContain("__APP_SEGMENTS__").And.NotContain("__CACHE_VERSION__");
        script.Should().Contain("const pathBase = \"/blazor/\"").And.Contain("const shellDocument = \"/blazor/app/offline\"");
        script.Should().Contain($"const appSegments = \"{string.Join(",", OfflineShell.AppNavigationSegments)}\".split(\",\")");
        script.Should().MatchRegex("const cacheVersion = \"[^\"]+\";");
    }

    [Theory]
    // Every first path segment of the authenticated surface, the shell document included
    [InlineData("/blazor/app", true)]
    [InlineData("/blazor/app/details", true)]
    [InlineData("/blazor/app/offline", true)]
    [InlineData("/blazor/account/users", true)]
    [InlineData("/blazor/user/profile", true)]
    [InlineData("/blazor/welcome", true)]
    // The public surface, the status pages and anything outside the path base
    [InlineData("/blazor/", false)]
    [InlineData("/blazor/login", false)]
    [InlineData("/blazor/signup/verify", false)]
    [InlineData("/blazor/legal/terms", false)]
    [InlineData("/blazor/not-found", false)]
    [InlineData("/blazor/apples", false)]
    [InlineData("/api/account/authentication/login/external/google/callback", false)]
    [InlineData("/blazorapp", false)]
    public void IsAppNavigation_ShouldAnswerForThePathAlone(string path, bool expected)
    {
        // Act
        var isAppNavigation = OfflineShell.IsAppNavigation(path);

        // Assert
        isAppNavigation.Should().Be(expected);
    }

    [Fact]
    public void Manifest_ShouldStartAtAuthenticatedHomeWithInstallableIcons()
    {
        // Arrange
        var hostShell = new HostShell(new TestWebHostEnvironment(Environments.Production));

        // Act
        using var manifest = JsonDocument.Parse(hostShell.Manifest);

        // Assert
        var root = manifest.RootElement;
        root.GetProperty("name").GetString().Should().Be(hostShell.Brand.ProductName);
        root.GetProperty("theme_color").GetString().Should().Be(hostShell.Brand.ThemeColorLight);
        root.GetProperty("display").GetString().Should().Be("standalone");
        root.GetProperty("start_url").GetString().Should().Be("/blazor/app");
        var icons = root.GetProperty("icons").EnumerateArray().Select(icon => $"{icon.GetProperty("sizes").GetString()} {icon.GetProperty("purpose").GetString()} {icon.GetProperty("src").GetString()}");
        icons.Should().Equal("192x192 any /blazor/icons/icon-192.png", "512x512 any /blazor/icons/icon-512.png", "512x512 maskable /blazor/icons/icon-maskable-512.png");
    }

    [Fact]
    public void BrandStylesheet_ShouldCarryBrandTokensAndBeVersionedUnderPathBase()
    {
        // Arrange
        var hostShell = new HostShell(new TestWebHostEnvironment(Environments.Production));

        // Assert
        hostShell.BrandStylesheet.Should().Contain($"--brand-primary: {hostShell.Brand.PrimaryColorLight};").And.Contain($"--brand-primary: {hostShell.Brand.PrimaryColorDark};");
        hostShell.BrandStylesheetUrl.Should().MatchRegex("^/blazor/brand\\.css\\?v=[0-9a-f]{16}$");
    }

    [Fact]
    public async Task RejectOutsideDevelopment_WhenDevelopmentOnlyPageInProduction_ShouldReturnNotFound()
    {
        // Arrange
        var context = CreateContextWithEndpoint(new DevelopmentOnlyAttribute());
        var nextCalled = false;

        // Act
        await DevelopmentOnlyPages.RejectOutsideDevelopmentAsync(context, _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }, new TestWebHostEnvironment(Environments.Production)
        );

        // Assert
        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RejectOutsideDevelopment_WhenDevelopmentOnlyPageInDevelopment_ShouldCallNext()
    {
        // Arrange
        var context = CreateContextWithEndpoint(new DevelopmentOnlyAttribute());
        var nextCalled = false;

        // Act
        await DevelopmentOnlyPages.RejectOutsideDevelopmentAsync(context, _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }, new TestWebHostEnvironment(Environments.Development)
        );

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task RejectOutsideDevelopment_WhenOrdinaryPageInProduction_ShouldCallNext()
    {
        // Arrange
        var context = CreateContextWithEndpoint();
        var nextCalled = false;

        // Act
        await DevelopmentOnlyPages.RejectOutsideDevelopmentAsync(context, _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }, new TestWebHostEnvironment(Environments.Production)
        );

        // Assert
        nextCalled.Should().BeTrue();
    }

    private static DefaultHttpContext CreateContextWithEndpoint(params object[] metadata)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "page"));
        return context;
    }

    private sealed class TestWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Blazor.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
