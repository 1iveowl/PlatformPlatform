// The two files the host generates from the brand tokens once at startup: the brand stylesheet, served at a URL versioned
// by its content, and the web app manifest.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blazor.Client;

namespace Blazor.Host.Shell;

public sealed class BrandAssets
{
    public const string StylesheetPath = "/brand.css";
    public const string ManifestPath = "/manifest.webmanifest";

    private const int ContentVersionLength = 16;

    public BrandAssets(BrandTokens brand)
    {
        Stylesheet = BuildStylesheet(brand);
        StylesheetUrl = $"{AppUrls.ToAbsolute(StylesheetPath)}?v={GetContentVersion(Stylesheet)}";
        Manifest = BuildManifest(brand);
    }

    public string Stylesheet { get; }

    public string StylesheetUrl { get; }

    public string Manifest { get; }

    // Served as an external stylesheet rather than an inline <style>: enhanced navigation re-inserts inline head elements
    // from the new document with a nonce the governing header does not carry, which the policy then blocks. The dark set
    // applies under data-theme="dark", which wwwroot/js/theme.js sets on <html> before first paint.
    private static string BuildStylesheet(BrandTokens brand)
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
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..ContentVersionLength];
    }
}
