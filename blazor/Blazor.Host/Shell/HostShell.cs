// Spike code (Blazor edition, stage B1): ports the React shell responsibilities of SharedKernel's
// SinglePageAppFallbackExtensions and SinglePageAppConfiguration into the Blazor host. Not production code.

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Endpoints;

namespace Blazor.Host.Shell;

public sealed record BrandTokens(
    string ProductName,
    string ThemeColorLight,
    string ThemeColorDark,
    string PrimaryColorLight,
    string PrimaryColorLightForeground,
    string PrimaryColorDark,
    string PrimaryColorDarkForeground,
    string InternalEmailDomain
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

    // Same literals as SharedKernel's AuthenticationTokenHttpKeys, so the React edition and this host share one antiforgery cookie
    public const string AntiforgeryCookieName = "__Host-xsrf-token";
    public const string AntiforgeryHeaderName = "x-xsrf-token";

    private const string PublicKeyPrefix = "PUBLIC_";
    private const string ApplicationVersionKey = "APPLICATION_VERSION";
    private const string DefaultLocale = "en-US";
    private const string NonceItemKey = "csp-nonce";
    private const string CspVariantQueryKey = "csp-variant";
    private static readonly string[] SupportedLocalizations = ["en-US", "da-DK"];

    private static readonly JsonSerializerOptions JsonHtmlEncodingOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ConditionalWeakTable<ImportMapDefinition, ImportMapDefinition> _absoluteImportMaps = new();
    private readonly string _trustedHosts;

    public HostShell(IWebHostEnvironment environment)
    {
        var publicUrl = Environment.GetEnvironmentVariable(PublicUrlKey) ?? string.Empty;
        var cdnUrl = Environment.GetEnvironmentVariable(CdnUrlKey) ?? string.Empty;
        var applicationVersion =
            Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()!.GetName().Version!.ToString();

        // Only PUBLIC_* keys plus the two keys the React edition allow-lists ever reach the page
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

        RuntimeEnvironmentJson = JsonSerializer.Serialize(runtimeEnvironment, JsonHtmlEncodingOptions);

        _trustedHosts = $"{publicUrl} {cdnUrl}";
        if (environment.IsDevelopment() && Uri.TryCreate(publicUrl, UriKind.Absolute, out var publicUri))
        {
            _trustedHosts += $" wss://{publicUri.Host}:* https://{publicUri.Host}:*";
        }

        Brand = LoadBrandTokens();
    }

    public string RuntimeEnvironmentJson { get; }

    public BrandTokens Brand { get; }

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

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
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
        headers.ContentSecurityPolicy = BuildContentSecurityPolicy(nonce, context.Request.Query[CspVariantQueryKey].ToString());

        return next(context);
    }

    // Spike only: the csp-variant query value exists for the B1 negative policy tests and must never be ported
    private string BuildContentSecurityPolicy(string nonce, string variant)
    {
        var wasmUnsafeEval = variant == "no-wasm-eval" ? "" : " 'wasm-unsafe-eval'";
        var strictDynamic = variant == "no-strict-dynamic" ? "" : " 'strict-dynamic'";

        var directives = new[]
        {
            $"script-src {_trustedHosts} 'nonce-{nonce}'{strictDynamic}{wasmUnsafeEval} https:",
            $"script-src-elem {_trustedHosts} 'nonce-{nonce}'",
            $"style-src {_trustedHosts} 'nonce-{nonce}'",
            $"style-src-elem {_trustedHosts} 'nonce-{nonce}'",
            $"default-src {_trustedHosts}",
            $"connect-src {_trustedHosts}",
            "frame-src 'none'",
            $"img-src {_trustedHosts} data: blob:",
            "object-src 'none'",
            "base-uri 'none'"
        };

        return string.Join(";", directives);
    }

    // Mirrors the claim reads in SharedKernel's UserInfo.Create; strongly typed ids serialize as strings there (StronglyTypedIdJsonConverter)
    public string GetUserInfoJson(HttpContext context)
    {
        var user = context.User;
        var zoomLevel = context.Request.Headers["x-zoom-level"].ToString();
        var theme = context.Request.Headers["x-theme"].ToString();
        var email = user.FindFirstValue(ClaimTypes.Email);
        var tenantId = user.FindFirstValue("tenant_id");
        var featureFlags = user.FindFirstValue("feature_flags");
        var tenantRolloutBucket = user.FindFirstValue("tenant_rollout_bucket");
        var userRolloutBucket = user.FindFirstValue("user_rollout_bucket");

        var userInfo = new
        {
            IsAuthenticated = user.Identity?.IsAuthenticated == true,
            Locale = GetLocale(user),
            Id = user.FindFirstValue(ClaimTypes.NameIdentifier),
            TenantId = tenantId,
            Role = user.FindFirstValue(ClaimTypes.Role),
            Email = email,
            FirstName = user.FindFirstValue(ClaimTypes.GivenName),
            LastName = user.FindFirstValue(ClaimTypes.Surname),
            Title = user.FindFirstValue("title"),
            AvatarUrl = user.FindFirstValue("avatar_url"),
            TenantName = user.FindFirstValue("tenant_name"),
            TenantLogoUrl = user.FindFirstValue("tenant_logo_url"),
            SubscriptionPlan = user.FindFirstValue("subscription_plan"),
            ZoomLevel = string.IsNullOrEmpty(zoomLevel) ? null : zoomLevel,
            Theme = string.IsNullOrEmpty(theme) ? null : theme,
            SessionId = user.FindFirstValue("session_id"),
            IsInternalUser = email?.EndsWith(Brand.InternalEmailDomain, StringComparison.OrdinalIgnoreCase) == true,
            FeatureFlags = string.IsNullOrEmpty(featureFlags) ? Array.Empty<string>() : featureFlags.Split(',', StringSplitOptions.RemoveEmptyEntries),
            TenantRolloutBucket = string.IsNullOrEmpty(tenantRolloutBucket) ? 0 : int.Parse(tenantRolloutBucket),
            UserRolloutBucket = string.IsNullOrEmpty(userRolloutBucket) ? (int?)null : int.Parse(userRolloutBucket)
        };

        return JsonSerializer.Serialize(userInfo, JsonHtmlEncodingOptions);
    }

    public static string GetLocale(ClaimsPrincipal user)
    {
        var locale = user.FindFirstValue("locale");
        if (string.IsNullOrEmpty(locale)) return DefaultLocale;
        if (SupportedLocalizations.Contains(locale, StringComparer.OrdinalIgnoreCase)) return locale;

        var baseLanguageCode = locale[..2];
        return SupportedLocalizations.FirstOrDefault(l => l.StartsWith(baseLanguageCode, StringComparison.OrdinalIgnoreCase)) ?? DefaultLocale;
    }

    // Under base-uri 'none' the browser ignores <base href>, so every URL the host page renders is made absolute under the
    // path base; otherwise relative URLs resolve against the document URL and break at /blazor and on deeper routes
    public static string ToAbsoluteUrl(PathString pathBase, string url)
    {
        if (url.StartsWith('/') || Uri.IsWellFormedUriString(url, UriKind.Absolute)) return url;

        return $"{pathBase}/{(url.StartsWith("./", StringComparison.Ordinal) ? url[2..] : url)}";
    }

    // Relative ("./") specifiers and targets become absolute; bare specifiers such as "_framework/resource-collection.js"
    // are matched literally by the browser and stay unchanged
    public ImportMapDefinition GetAbsoluteImportMap(ImportMapDefinition source, PathString pathBase)
    {
        return _absoluteImportMaps.GetValue(source, definition =>
            {
                return new ImportMapDefinition(
                    RewriteEntries(definition.Imports, pathBase),
                    definition.Scopes?.ToDictionary(scope => ToAbsoluteSpecifier(pathBase, scope.Key), scope => RewriteEntries(scope.Value, pathBase)!),
                    definition.Integrity?.ToDictionary(entry => ToAbsoluteSpecifier(pathBase, entry.Key), entry => entry.Value)
                );
            }
        );
    }

    // The link elements <ResourcePreloader/> would render, with absolute hrefs; it renders relative hrefs and takes no parameters.
    // A preload is only reused when its integrity matches the later module request, which takes it from the import map.
    public static IEnumerable<PreloadLink> GetPreloadLinks(ResourceAssetCollection assets, ImportMapDefinition importMap, PathString pathBase)
    {
        foreach (var asset in assets)
        {
            var properties = asset.Properties?.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
            if (properties is null || !properties.TryGetValue("preloadrel", out var rel)) continue;

            properties.TryGetValue("preloadorder", out var order);
            var href = ToAbsoluteUrl(pathBase, asset.Url);
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

    private static IReadOnlyDictionary<string, string>? RewriteEntries(IReadOnlyDictionary<string, string>? entries, PathString pathBase)
    {
        return entries?.ToDictionary(entry => ToAbsoluteSpecifier(pathBase, entry.Key), entry => ToAbsoluteUrl(pathBase, entry.Value));
    }

    private static string ToAbsoluteSpecifier(PathString pathBase, string specifier)
    {
        return specifier.StartsWith("./", StringComparison.Ordinal) ? ToAbsoluteUrl(pathBase, specifier) : specifier;
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
            primaryColor.GetProperty("light").GetString()!,
            primaryColor.GetProperty("lightForeground").GetString()!,
            primaryColor.GetProperty("dark").GetString()!,
            primaryColor.GetProperty("darkForeground").GetString()!,
            document.RootElement.GetProperty("identity").GetProperty("internalEmailDomain").GetString()!
        );
    }
}
