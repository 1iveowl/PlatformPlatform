// The host page responsibilities that SharedKernel's SinglePageAppFallbackExtensions and SinglePageAppConfiguration carry
// for the React edition: security headers and the content security policy, runtime configuration, locale, brand tokens
// and the web app manifest.

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blazor.Client;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Endpoints;

namespace Blazor.Host.Shell;

public sealed record BrandTokens(
    string ProductName,
    string ThemeColorLight,
    string ThemeColorDark,
    string BackgroundColor,
    string PrimaryColorLight,
    string PrimaryColorLightForeground,
    string PrimaryColorDark,
    string PrimaryColorDarkForeground
);

public sealed record PreloadLink(
    string Href,
    string Rel,
    string? As,
    string? FetchPriority,
    string? CrossOrigin,
    string? Integrity,
    int Order
);

public sealed class HostShell
{
    public const string PublicUrlKey = "PUBLIC_URL";
    public const string CdnUrlKey = "CDN_URL";
    public const string BrandStylesheetPath = "/brand.css";
    public const string ManifestPath = "/manifest.webmanifest";

    // Same literals as SharedKernel's AuthenticationTokenHttpKeys, so the React edition and this host share one antiforgery cookie
    public const string AntiforgeryCookieName = "__Host-xsrf-token";
    public const string AntiforgeryHeaderName = "x-xsrf-token";

    private const string PublicKeyPrefix = "PUBLIC_";
    private const string ApplicationVersionKey = "APPLICATION_VERSION";
    private const string DefaultLocale = "en-US";
    private const string NonceItemKey = "csp-nonce";
    private const int NonceByteCount = 16;
    private static readonly string[] SupportedLocalizations = ["en-US", "da-DK"];

    private readonly ConditionalWeakTable<ImportMapDefinition, ImportMapDefinition> _absoluteImportMaps = new();
    private readonly string _trustedHosts;

    public HostShell(IWebHostEnvironment environment)
    {
        var publicUrl = Environment.GetEnvironmentVariable(PublicUrlKey) ?? string.Empty;
        var cdnUrl = Environment.GetEnvironmentVariable(CdnUrlKey) ?? string.Empty;
        var applicationVersion =
            Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()!.GetName().Version!.ToString();

        // Only PUBLIC_* keys plus the two keys the React edition allow-lists ever reach the client
        var runtimeEnvironment = new Dictionary<string, string>
        {
            { PublicUrlKey, publicUrl },
            { CdnUrlKey, cdnUrl },
            { ApplicationVersionKey, applicationVersion }
        };
        foreach (var entry in Environment.GetEnvironmentVariables().Cast<DictionaryEntry>())
        {
            var key = (string)entry.Key;
            if (key.StartsWith(PublicKeyPrefix, StringComparison.Ordinal) && !runtimeEnvironment.ContainsKey(key))
            {
                runtimeEnvironment[key] = (string?)entry.Value ?? string.Empty;
            }
        }

        RuntimeEnvironment = runtimeEnvironment;

        _trustedHosts = $"{publicUrl} {cdnUrl}";
        if (environment.IsDevelopment() && Uri.TryCreate(publicUrl, UriKind.Absolute, out var publicUri))
        {
            _trustedHosts += $" wss://{publicUri.Host}:* https://{publicUri.Host}:*";
        }

        Brand = LoadBrandTokens();
        BrandStylesheet = BuildBrandStylesheet(Brand);
        BrandStylesheetUrl = $"{AppUrls.ToAbsolute(BrandStylesheetPath)}?v={GetContentVersion(BrandStylesheet)}";
        Manifest = BuildManifest(Brand);
    }

    public IReadOnlyDictionary<string, string> RuntimeEnvironment { get; }

    public BrandTokens Brand { get; }

    public string BrandStylesheet { get; }

    public string BrandStylesheetUrl { get; }

    public string Manifest { get; }

    public static string GetNonce(HttpContext context)
    {
        return (string)context.Items[NonceItemKey]!;
    }

    // Runs for Razor component page endpoints only, mirroring SetResponseHttpHeaders on the React shell response
    public Task ApplyPageHeadersAsync(HttpContext context, RequestDelegate next)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is null)
        {
            return next(context);
        }

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceByteCount));
        context.Items[NonceItemKey] = nonce;

        var headers = context.Response.Headers;
        headers.CacheControl = "no-cache, no-store, must-revalidate";
        headers.Pragma = "no-cache";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers.XXSSProtection = "1; mode=block";
        headers["Referrer-Policy"] = "no-referrer, strict-origin-when-cross-origin";
        headers["Permissions-Policy"] =
            "geolocation=(), microphone=(), camera=(), picture-in-picture=(), display-capture=(), fullscreen=(self), web-share=(), identity-credentials-get=()";
        headers.ContentSecurityPolicy = BuildContentSecurityPolicy(nonce);

        return next(context);
    }

    // The React edition's policy with 'wasm-unsafe-eval' added for the WebAssembly runtime and the Stripe hosts left out.
    // No form-action: Chromium and WebKit apply it to the redirects after a form submission, and an external login start
    // redirects to the identity provider's origin, so form-action 'self' blocks the start.
    // worker-src 'self': the offline shell's service worker must be same-origin, and stating it explicitly avoids inheriting
    // script-src, where 'strict-dynamic' makes the browser ignore host sources and 'self'.
    public string BuildContentSecurityPolicy(string nonce)
    {
        var directives = new[]
        {
            $"script-src {_trustedHosts} 'nonce-{nonce}' 'strict-dynamic' 'wasm-unsafe-eval' https:",
            $"script-src-elem {_trustedHosts} 'nonce-{nonce}'",
            $"style-src {_trustedHosts} 'nonce-{nonce}'",
            $"style-src-elem {_trustedHosts} 'nonce-{nonce}'",
            $"default-src {_trustedHosts}",
            $"connect-src {_trustedHosts}",
            "frame-src 'none'",
            $"img-src {_trustedHosts} data: blob:",
            "object-src 'none'",
            "base-uri 'none'",
            "worker-src 'self'"
        };

        return string.Join(";", directives);
    }

    // The locale claim for a signed-in user; for an anonymous visitor the best supported Accept-Language entry
    public static string GetLocale(HttpContext context)
    {
        var claimLocale = context.User.FindFirstValue("locale");
        if (!string.IsNullOrEmpty(claimLocale)) return ToSupportedLocale(claimLocale) ?? DefaultLocale;

        var acceptLanguages = context.Request.GetTypedHeaders().AcceptLanguage.OrderByDescending(language => language.Quality ?? 1);
        return acceptLanguages.Select(language => ToSupportedLocale(language.Value.ToString())).FirstOrDefault(locale => locale is not null) ?? DefaultLocale;
    }

    private static string? ToSupportedLocale(string locale)
    {
        if (locale.Length < 2) return null;
        if (SupportedLocalizations.Contains(locale, StringComparer.OrdinalIgnoreCase)) return SupportedLocalizations.First(l => l.Equals(locale, StringComparison.OrdinalIgnoreCase));

        var baseLanguageCode = locale[..2];
        return SupportedLocalizations.FirstOrDefault(l => l.StartsWith(baseLanguageCode, StringComparison.OrdinalIgnoreCase));
    }

    // Relative ("./") specifiers and targets become root-absolute; bare specifiers such as "_framework/resource-collection.js"
    // are matched literally by the browser and stay unchanged
    public ImportMapDefinition GetAbsoluteImportMap(ImportMapDefinition source)
    {
        return _absoluteImportMaps.GetValue(source, definition =>
            {
                return new ImportMapDefinition(
                    RewriteEntries(definition.Imports),
                    definition.Scopes?.ToDictionary(scope => ToAbsoluteSpecifier(scope.Key), scope => RewriteEntries(scope.Value)!),
                    definition.Integrity?.ToDictionary(entry => ToAbsoluteSpecifier(entry.Key), entry => entry.Value)
                );
            }
        );
    }

    // The link elements <ResourcePreloader/> would render, with root-absolute hrefs; it renders relative hrefs and takes no
    // parameters. A preload is only reused when its integrity matches the later module request, which takes it from the import map.
    public static IEnumerable<PreloadLink> GetPreloadLinks(ResourceAssetCollection assets, ImportMapDefinition importMap)
    {
        foreach (var asset in assets)
        {
            var properties = asset.Properties?.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
            if (properties is null || !properties.TryGetValue("preloadrel", out var rel)) continue;

            properties.TryGetValue("preloadorder", out var order);
            var href = AppUrls.ToAbsolute(asset.Url);
            yield return new PreloadLink(
                href,
                rel,
                properties.GetValueOrDefault("preloadas"),
                properties.GetValueOrDefault("preloadpriority"),
                properties.GetValueOrDefault("preloadcrossorigin"),
                importMap.Integrity?.GetValueOrDefault(href),
                int.TryParse(order, out var parsedOrder) ? parsedOrder : int.MaxValue
            );
        }
    }

    private static IReadOnlyDictionary<string, string>? RewriteEntries(IReadOnlyDictionary<string, string>? entries)
    {
        return entries?.ToDictionary(entry => ToAbsoluteSpecifier(entry.Key), entry => AppUrls.ToAbsolute(entry.Value));
    }

    private static string ToAbsoluteSpecifier(string specifier)
    {
        return specifier.StartsWith("./", StringComparison.Ordinal) ? AppUrls.ToAbsolute(specifier) : specifier;
    }

    // Served as an external stylesheet rather than an inline <style>: enhanced navigation re-inserts inline head elements
    // from the new document with a nonce the governing header does not carry, which the policy then blocks
    private static string BuildBrandStylesheet(BrandTokens brand)
    {
        return $$"""
                 :root, .light {
                     --brand-primary: {{brand.PrimaryColorLight}};
                     --brand-primary-foreground: {{brand.PrimaryColorLightForeground}};
                 }

                 .dark {
                     --brand-primary: {{brand.PrimaryColorDark}};
                     --brand-primary-foreground: {{brand.PrimaryColorDarkForeground}};
                 }

                 """;
    }

    private static string BuildManifest(BrandTokens brand)
    {
        var manifest = new
        {
            name = brand.ProductName,
            short_name = brand.ProductName,
            start_url = AppUrls.AuthenticatedHome,
            scope = $"{AppUrls.PathBase}/",
            display = "standalone",
            theme_color = brand.ThemeColorLight,
            background_color = brand.BackgroundColor,
            icons = new object[]
            {
                new { src = AppUrls.ToAbsolute("icons/icon-192.png"), sizes = "192x192", type = "image/png", purpose = "any" },
                new { src = AppUrls.ToAbsolute("icons/icon-512.png"), sizes = "512x512", type = "image/png", purpose = "any" },
                new { src = AppUrls.ToAbsolute("icons/icon-maskable-512.png"), sizes = "512x512", type = "image/png", purpose = "maskable" }
            }
        };

        return JsonSerializer.Serialize(manifest);
    }

    private static string GetContentVersion(string content)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..16];
    }

    public static BrandTokens LoadBrandTokens()
    {
        using var stream = typeof(HostShell).Assembly.GetManifestResourceStream("platform-settings.jsonc")
                           ?? throw new InvalidOperationException("Embedded resource 'platform-settings.jsonc' not found.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var branding = document.RootElement.GetProperty("branding");
        var themeColor = branding.GetProperty("themeColor");
        var primaryColor = branding.GetProperty("primaryColor");

        return new BrandTokens(
            branding.GetProperty("productName").GetString()!,
            themeColor.GetProperty("light").GetString()!,
            themeColor.GetProperty("dark").GetString()!,
            branding.GetProperty("backgroundColor").GetString()!,
            primaryColor.GetProperty("light").GetString()!,
            primaryColor.GetProperty("lightForeground").GetString()!,
            primaryColor.GetProperty("dark").GetString()!,
            primaryColor.GetProperty("darkForeground").GetString()!
        );
    }
}
