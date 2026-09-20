// The host page responsibilities that SharedKernel's SinglePageAppFallbackExtensions and SinglePageAppConfiguration carry
// for the React edition: security headers and the content security policy, locale, brand tokens and the web app
// manifest. Runtime configuration reaches clients through the bootstrap contract, never through the host page.

using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blazor.Client;
using Blazor.Client.Preferences;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Endpoints;
using SharedKernel.Localization;

namespace Blazor.Host.Shell;

public sealed record BrandTokens(
    string ProductName,
    string ThemeColorLight,
    string ThemeColorDark,
    string BackgroundColor,
    string PrimaryColorLight,
    string PrimaryColorLightForeground,
    string PrimaryColorDark,
    string PrimaryColorDarkForeground,
    // The address the delete-account notice tells a signed-in user to write to; empty when the brand names none
    string SupportEmail,
    // The address the public footer offers to write to; empty when the brand names none
    string ContactEmail,
    // The one-line product description the public footer shows, by culture, from the brand's web tagline map
    IReadOnlyDictionary<string, string> WebTaglines
)
{
    // The tagline of the culture the page renders in, the en-US one when the brand names no tagline for it, or empty
    public string GetTagline(string locale)
    {
        return WebTaglines.GetValueOrDefault(locale) ?? WebTaglines.GetValueOrDefault(SupportedCultures.DefaultLocale) ?? "";
    }
}

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

    private const string NonceItemKey = "csp-nonce";
    private const int NonceByteCount = 16;

    private readonly ConditionalWeakTable<ImportMapDefinition, ImportMapDefinition> _absoluteImportMaps = new();
    private readonly string _trustedHosts;

    public HostShell(IWebHostEnvironment environment)
    {
        var publicUrl = Environment.GetEnvironmentVariable(PublicUrlKey) ?? string.Empty;
        var cdnUrl = Environment.GetEnvironmentVariable(CdnUrlKey) ?? string.Empty;

        _trustedHosts = $"{publicUrl} {cdnUrl}";
        if (environment.IsDevelopment() && Uri.TryCreate(publicUrl, UriKind.Absolute, out var publicUri))
        {
            _trustedHosts += $" wss://{publicUri.Host}:* https://{publicUri.Host}:*";
        }

        Brand = LoadBrandTokens();
        BrandStylesheet = BuildBrandStylesheet(Brand);
        BrandStylesheetUrl = $"{AppUrls.ToAbsolute(BrandStylesheetPath)}?v={GetContentVersion(BrandStylesheet)}";
        Manifest = BuildManifest(Brand);
        WorkerScript = OfflineShell.BuildWorkerScript();
        InternalEmailDomain = LoadInternalEmailDomain();
    }

    public BrandTokens Brand { get; }

    public string BrandStylesheet { get; }

    public string BrandStylesheetUrl { get; }

    public string Manifest { get; }

    // The offline shell's service worker, with the values OfflineShell decides substituted into it
    public string WorkerScript { get; }

    // The email suffix that marks a user as internal, from the same settings file UserInfo reads
    public string InternalEmailDomain { get; }

    public static string GetNonce(HttpContext context)
    {
        return FindNonce(context)!;
    }

    // Null on the offline shell document, which is the one page rendered without a nonce because it is stored and replayed
    public static string? FindNonce(HttpContext context)
    {
        return context.Items.TryGetValue(NonceItemKey, out var nonce) ? (string?)nonce : null;
    }

    // Runs for Razor component page endpoints only, mirroring SetResponseHttpHeaders on the React shell response
    public Task ApplyPageHeadersAsync(HttpContext context, RequestDelegate next)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is null)
        {
            return next(context);
        }

        // The offline shell is the one document a cache may keep, so it is rendered without a nonce and without a user:
        // a per-request secret replayed from a cache would govern nothing, and the document is served to whoever launches
        // the installed application next. Every other document stays no-store with a nonce of its own.
        var isOfflineShell = context.GetEndpoint()?.Metadata.GetMetadata<OfflineShellPageAttribute>() is not null;
        var nonce = isOfflineShell ? null : Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceByteCount));
        if (nonce is not null) context.Items[NonceItemKey] = nonce;

        var headers = context.Response.Headers;
        headers.CacheControl = "no-cache, no-store, must-revalidate";
        headers.Pragma = "no-cache";
        if (isOfflineShell)
        {
            // The component endpoint adds no-store of its own, and the antiforgery middleware issues a token cookie on every
            // component document, both after this middleware has run. The shell is stored and replayed, so its directives are
            // written once more as the response starts and it is made to set no cookie at all: it has no form, so nothing
            // needs a token from it, and a token in a cache is a token too many. The browser's own cookie is untouched.
            context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = "no-cache, must-revalidate";
                    context.Response.Headers.Remove("Pragma");
                    context.Response.Headers.Remove("Set-Cookie");
                    return Task.CompletedTask;
                }
            );
        }

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
    // A null nonce is the offline shell document, which is stored and replayed: it renders no inline element, so it needs no
    // nonce source, and one frozen into a cached document would name a request that is long over. Its scripts and stylesheets
    // are the same same-origin files every page loads, allowed by the trusted host list on script-src-elem and style-src-elem.
    // No directive gains a source either way.
    public string BuildContentSecurityPolicy(string? nonce)
    {
        var noncePart = nonce is null ? "" : $" 'nonce-{nonce}'";
        var directives = new[]
        {
            $"script-src {_trustedHosts}{noncePart} 'strict-dynamic' 'wasm-unsafe-eval' https:",
            $"script-src-elem {_trustedHosts}{noncePart}",
            $"style-src {_trustedHosts}{noncePart}",
            $"style-src-elem {_trustedHosts}{noncePart}",
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

    // The locale claim for a signed-in user; otherwise the language chosen on this device (the preferred-locale cookie, an
    // untrusted hint used only when it names a supported culture exactly); otherwise the best supported Accept-Language
    // entry; otherwise en-US. Request localization sets the request culture from this value, so rendering and the API calls
    // of the request, the signup's locale included, use the same one.
    public static string GetLocale(HttpContext context)
    {
        var acceptLanguages = context.Request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(language => language.Quality ?? 1)
            .Select(language => language.Value.ToString());
        var preferredLocale = context.Request.Cookies[LocalePreference.CookieName];
        return SupportedCultures.SelectLocale(context.User.FindFirstValue("locale"), preferredLocale, acceptLanguages);
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

    // Static asset responses name further preloads in a Link header with targets relative to the asset: the scoped stylesheet
    // bundle lists "_content/<package>/<package>.bundle.scp.css". Chromium resolves such a target against the response URL, as
    // RFC 8288 specifies, but WebKit and Firefox resolve it against the document URL, which requests a path below the page and
    // returns 404 on any page deeper than one segment. Rewriting each relative target root-absolute against the response URL
    // makes every browser request the file Chromium already does.
    public static Task RewriteLinkHeadersAsync(HttpContext context, RequestDelegate next)
    {
        var responsePath = $"{context.Request.PathBase}{context.Request.Path}";
        context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                if (headers.Link.Count > 0) headers.Link = ToRootAbsoluteLinkHeader(headers.Link.ToString(), responsePath);
                return Task.CompletedTask;
            }
        );

        return next(context);
    }

    public static string ToRootAbsoluteLinkHeader(string header, string responsePath)
    {
        if (!Uri.TryCreate($"http://host{responsePath}", UriKind.Absolute, out var responseUrl)) return header;

        var rewritten = new StringBuilder(header.Length);
        var position = 0;
        while (true)
        {
            var start = header.IndexOf('<', position);
            var end = start < 0 ? -1 : header.IndexOf('>', start + 1);
            if (end < 0) break;

            rewritten.Append(header, position, start + 1 - position);
            rewritten.Append(ToRootAbsoluteLinkTarget(header[(start + 1)..end], responseUrl));
            rewritten.Append('>');
            position = end + 1;
        }

        rewritten.Append(header, position, header.Length - position);
        return rewritten.ToString();
    }

    private static string ToRootAbsoluteLinkTarget(string target, Uri responseUrl)
    {
        if (target.StartsWith('/') || target.StartsWith("http://", StringComparison.Ordinal) || target.StartsWith("https://", StringComparison.Ordinal)) return target;

        var resolved = new Uri(responseUrl, target);
        return $"{resolved.AbsolutePath}{resolved.Query}{resolved.Fragment}";
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
    // from the new document with a nonce the governing header does not carry, which the policy then blocks. The dark set
    // applies under data-theme="dark", which wwwroot/js/theme.js sets on <html> before first paint.
    private static string BuildBrandStylesheet(BrandTokens brand)
    {
        return $$"""
                 :root, :root[data-theme="light"] {
                     --brand-primary: {{brand.PrimaryColorLight}};
                     --brand-primary-foreground: {{brand.PrimaryColorLightForeground}};
                 }

                 :root[data-theme="dark"] {
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
        using var document = LoadPlatformSettings();
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
            primaryColor.GetProperty("darkForeground").GetString()!,
            branding.GetProperty("supportEmail").GetString() ?? "",
            branding.GetProperty("contactEmail").GetString() ?? "",
            branding.GetProperty("tagline").GetProperty("web").EnumerateObject().ToDictionary(tagline => tagline.Name, tagline => tagline.Value.GetString() ?? "")
        );
    }

    private static string LoadInternalEmailDomain()
    {
        using var document = LoadPlatformSettings();
        return document.RootElement.GetProperty("identity").GetProperty("internalEmailDomain").GetString()!;
    }

    private static JsonDocument LoadPlatformSettings()
    {
        using var stream = typeof(HostShell).Assembly.GetManifestResourceStream("platform-settings.jsonc")
                           ?? throw new InvalidOperationException("Embedded resource 'platform-settings.jsonc' not found.");
        return JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
    }
}
