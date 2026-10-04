// The host sources each surface's content security policy trusts. An app page trusts the public and CDN URLs; a back-office
// page is served on its own origin, so its policy trusts that origin in place of the app's, and neither list gains the
// other's origin. In Development each list also trusts any port of its own host over https and wss.

namespace Blazor.Host.Shell;

public sealed record TrustedHosts(string App, string BackOffice)
{
    public const string PublicUrlKey = "PUBLIC_URL";
    public const string CdnUrlKey = "CDN_URL";

    public static TrustedHosts FromEnvironment(bool isDevelopment)
    {
        return Create(ReadVariable(PublicUrlKey), ReadVariable(CdnUrlKey), ReadVariable(BackOfficeOrigin.PublicUrlKey), isDevelopment);
    }

    public static TrustedHosts Create(string publicUrl, string cdnUrl, string backOfficeUrl, bool isDevelopment)
    {
        return new TrustedHosts(
            $"{publicUrl} {cdnUrl}{GetDevelopmentSources(publicUrl, isDevelopment)}",
            $"{backOfficeUrl}{GetDevelopmentSources(backOfficeUrl, isDevelopment)}"
        );
    }

    private static string GetDevelopmentSources(string url, bool isDevelopment)
    {
        if (!isDevelopment || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";

        return $" wss://{uri.Host}:* https://{uri.Host}:*";
    }

    private static string ReadVariable(string key)
    {
        return Environment.GetEnvironmentVariable(key) ?? string.Empty;
    }
}
