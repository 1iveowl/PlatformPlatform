// The host page responsibilities that SharedKernel's SinglePageAppFallbackExtensions and SinglePageAppConfiguration carry
// for the React edition that concern each request: the security headers and the content security policy with its nonce,
// the antiforgery names and the request locale. The brand files are BrandAssets and the asset links AssetLinks; runtime
// configuration reaches clients through the bootstrap contract, never through the host page.

using System.Security.Claims;
using System.Security.Cryptography;
using Blazor.Client.Preferences;
using Microsoft.AspNetCore.Components.Endpoints;
using SharedKernel.Localization;

namespace Blazor.Host.Shell;

public sealed class HostShell(TrustedHosts trustedHosts)
{
    // Same literals as SharedKernel's AuthenticationTokenHttpKeys, so the React edition and this host share one antiforgery cookie
    public const string AntiforgeryCookieName = "__Host-xsrf-token";
    public const string AntiforgeryHeaderName = "x-xsrf-token";

    private const string NonceItemKey = "csp-nonce";
    private const int NonceByteCount = 16;

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
        var isBackOfficePage = context.GetEndpoint()?.Metadata.GetMetadata<BackOfficeSurfaceAttribute>() is not null;
        headers.ContentSecurityPolicy = BuildContentSecurityPolicy(nonce, isBackOfficePage);

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
    // No directive gains a source either way. A back-office page gets the same directives with the back-office origin as its
    // trusted host instead of the app's public and CDN URLs.
    public string BuildContentSecurityPolicy(string? nonce, bool isBackOfficePage = false)
    {
        var noncePart = nonce is null ? "" : $" 'nonce-{nonce}'";
        var surfaceTrustedHosts = isBackOfficePage ? trustedHosts.BackOffice : trustedHosts.App;
        var directives = new[]
        {
            $"script-src {surfaceTrustedHosts}{noncePart} 'strict-dynamic' 'wasm-unsafe-eval' https:",
            $"script-src-elem {surfaceTrustedHosts}{noncePart}",
            $"style-src {surfaceTrustedHosts}{noncePart}",
            $"style-src-elem {surfaceTrustedHosts}{noncePart}",
            $"default-src {surfaceTrustedHosts}",
            $"connect-src {surfaceTrustedHosts}",
            "frame-src 'none'",
            $"img-src {surfaceTrustedHosts} data: blob:",
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
}
