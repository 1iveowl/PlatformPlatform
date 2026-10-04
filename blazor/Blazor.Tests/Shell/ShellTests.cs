using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blazor.Host.Shell;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Blazor.Tests.Shell;

public sealed class ShellTests
{
    private const string PublicUrl = "https://app.example.com";
    private const string CdnUrl = "https://cdn.example.com";
    private const string BackOfficeUrl = "https://back-office.example.com";

    private static readonly TrustedHosts ProductionTrustedHosts = TrustedHosts.Create(PublicUrl, CdnUrl, BackOfficeUrl, false);

    [Fact]
    public void BuildContentSecurityPolicy_ShouldHaveNoUnsafeSourceAndTheWorkerDirective()
    {
        // Arrange
        var hostShell = new HostShell(ProductionTrustedHosts);

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
        var hostShell = new HostShell(ProductionTrustedHosts);

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
        var brand = PlatformSettings.Load().Brand;

        // Act
        using var manifest = JsonDocument.Parse(new BrandAssets(brand).Manifest);

        // Assert
        var root = manifest.RootElement;
        root.GetProperty("name").GetString().Should().Be(brand.ProductName);
        root.GetProperty("short_name").GetString().Should().Be(brand.ProductName);
        root.GetProperty("theme_color").GetString().Should().Be(brand.ThemeColorLight);
        root.GetProperty("background_color").GetString().Should().Be(brand.BackgroundColor);
        root.GetProperty("display").GetString().Should().Be("standalone");
        root.GetProperty("start_url").GetString().Should().Be("/blazor/app");
        root.GetProperty("scope").GetString().Should().Be("/blazor/");
        var icons = root.GetProperty("icons").EnumerateArray().Select(icon => $"{icon.GetProperty("sizes").GetString()} {icon.GetProperty("purpose").GetString()} {icon.GetProperty("src").GetString()}");
        icons.Should().Equal("192x192 any /blazor/icons/icon-192.png", "512x512 any /blazor/icons/icon-512.png", "512x512 maskable /blazor/icons/icon-maskable-512.png");
    }

    [Fact]
    public void BrandStylesheet_ShouldCarryBrandTokensAndBeVersionedUnderPathBase()
    {
        // Arrange
        var brand = PlatformSettings.Load().Brand;

        // Act
        var brandAssets = new BrandAssets(brand);

        // Assert
        brandAssets.Stylesheet.Should().Contain($"--brand-primary: {brand.PrimaryColorLight};").And.Contain($"--brand-primary: {brand.PrimaryColorDark};");
        brandAssets.Stylesheet.Should().Contain($"--brand-primary-foreground: {brand.PrimaryColorLightForeground};").And.Contain($"--brand-primary-foreground: {brand.PrimaryColorDarkForeground};");
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(brandAssets.Stylesheet)));
        brandAssets.StylesheetUrl.Should().Be($"/blazor/brand.css?v={contentHash[..16]}");
    }

    [Fact]
    public void BrandAssets_WhenBuiltTwiceFromTheSameTokens_ShouldBeIdentical()
    {
        // Arrange
        var brand = PlatformSettings.Load().Brand;

        // Act
        var first = new BrandAssets(brand);
        var second = new BrandAssets(brand);

        // Assert
        second.StylesheetUrl.Should().Be(first.StylesheetUrl);
        second.Manifest.Should().Be(first.Manifest);
    }

    [Fact]
    public void TrustedHostsCreate_InProduction_ShouldTrustEachSurfacesOwnOriginsOnly()
    {
        // Act
        var trustedHosts = TrustedHosts.Create(PublicUrl, CdnUrl, BackOfficeUrl, false);

        // Assert
        trustedHosts.App.Should().Be($"{PublicUrl} {CdnUrl}");
        trustedHosts.BackOffice.Should().Be(BackOfficeUrl);
    }

    [Fact]
    public void TrustedHostsCreate_InDevelopment_ShouldAddAnyPortOfEachSurfacesOwnHostOnly()
    {
        // Act
        var trustedHosts = TrustedHosts.Create(PublicUrl, CdnUrl, BackOfficeUrl, true);

        // Assert
        trustedHosts.App.Should().Be($"{PublicUrl} {CdnUrl} wss://app.example.com:* https://app.example.com:*");
        trustedHosts.BackOffice.Should().Be($"{BackOfficeUrl} wss://back-office.example.com:* https://back-office.example.com:*");
    }

    [Fact]
    public void TrustedHostsCreate_WhenAnOriginIsNotAnAbsoluteUrl_ShouldAddNoDevelopmentSource()
    {
        // Act
        var trustedHosts = TrustedHosts.Create("", "", "", true);

        // Assert
        trustedHosts.App.Should().Be(" ");
        trustedHosts.BackOffice.Should().BeEmpty();
    }

    [Fact]
    public void BuildContentSecurityPolicy_ForABackOfficePage_ShouldSwapOnlyTheTrustedHosts()
    {
        // Arrange
        var hostShell = new HostShell(ProductionTrustedHosts);

        // Act
        var appPolicy = hostShell.BuildContentSecurityPolicy("test-nonce");
        var backOfficePolicy = hostShell.BuildContentSecurityPolicy("test-nonce", true);

        // Assert
        backOfficePolicy.Should().Be(appPolicy.Replace($"{PublicUrl} {CdnUrl}", BackOfficeUrl));
        backOfficePolicy.Should().NotContain(PublicUrl).And.NotContain(CdnUrl);
        appPolicy.Should().NotContain(BackOfficeUrl);
    }

    [Fact]
    public async Task ApplyPageHeaders_WhenEndpointIsNotAComponentPage_ShouldOnlyCallNext()
    {
        // Arrange
        var context = CreateContextWithEndpoint();
        var nextCalled = false;

        // Act
        await new HostShell(ProductionTrustedHosts).ApplyPageHeadersAsync(context, _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }
        );

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.Headers.Should().BeEmpty();
        HostShell.FindNonce(context).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyPageHeaders_WhenComponentPage_ShouldSendNoStoreAndThePolicyOfItsSurfaceWithAFreshNonce(bool isBackOfficePage)
    {
        // Arrange
        var hostShell = new HostShell(ProductionTrustedHosts);
        object[] metadata = isBackOfficePage ? [new ComponentTypeMetadata(typeof(object)), new BackOfficeSurfaceAttribute()] : [new ComponentTypeMetadata(typeof(object))];
        var first = CreateContextWithEndpoint(metadata);
        var second = CreateContextWithEndpoint(metadata);

        // Act
        await hostShell.ApplyPageHeadersAsync(first, _ => Task.CompletedTask);
        await hostShell.ApplyPageHeadersAsync(second, _ => Task.CompletedTask);

        // Assert
        var nonce = HostShell.GetNonce(first);
        Convert.FromBase64String(nonce).Should().HaveCount(16);
        HostShell.GetNonce(second).Should().NotBe(nonce);
        var headers = first.Response.Headers;
        headers.CacheControl.ToString().Should().Be("no-cache, no-store, must-revalidate");
        headers.Pragma.ToString().Should().Be("no-cache");
        headers.XContentTypeOptions.ToString().Should().Be("nosniff");
        headers.XFrameOptions.ToString().Should().Be("DENY");
        headers.ContentSecurityPolicy.ToString().Should().Be(hostShell.BuildContentSecurityPolicy(nonce, isBackOfficePage));
    }

    [Fact]
    public async Task ApplyPageHeaders_WhenOfflineShellPage_ShouldSendThePolicyWithoutANonce()
    {
        // Arrange
        var hostShell = new HostShell(ProductionTrustedHosts);
        var context = CreateContextWithEndpoint(new ComponentTypeMetadata(typeof(object)), new OfflineShellPageAttribute());

        // Act
        await hostShell.ApplyPageHeadersAsync(context, _ => Task.CompletedTask);

        // Assert
        HostShell.FindNonce(context).Should().BeNull();
        context.Response.Headers.ContentSecurityPolicy.ToString().Should().Be(hostShell.BuildContentSecurityPolicy(null));
    }

    [Theory]
    // The locale claim decides, whatever the cookie and the browser ask for
    [InlineData("da-DK", "en-US", "en-US", "da-DK")]
    // Without a claim, a cookie naming a supported culture exactly, then the best Accept-Language entry, then en-US
    [InlineData(null, "da-DK", "en-US", "da-DK")]
    [InlineData(null, "da", "da-DK", "da-DK")]
    [InlineData(null, null, "fr-FR, da;q=0.8", "da-DK")]
    [InlineData(null, null, "fr-FR", "en-US")]
    [InlineData(null, null, null, "en-US")]
    public void GetLocale_ShouldPreferTheClaimThenTheCookieThenAcceptLanguage(string? claimLocale, string? cookieLocale, string? acceptLanguage, string expected)
    {
        // Arrange
        var context = new DefaultHttpContext();
        if (claimLocale is not null) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("locale", claimLocale)], "Test"));
        if (cookieLocale is not null) context.Request.Headers.Cookie = $"preferred-locale={cookieLocale}";
        if (acceptLanguage is not null) context.Request.Headers.AcceptLanguage = acceptLanguage;

        // Act
        var locale = HostShell.GetLocale(context);

        // Assert
        locale.Should().Be(expected);
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
