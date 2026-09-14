namespace Blazor.Client;

// The one place that knows the path base the gateway routes to this edition. The policy's base-uri 'none' makes the
// browser ignore <base href>, so every URL the host page or a component renders is root-absolute and built here.
public static class AppUrls
{
    public const string PathBase = "/blazor";

    public static readonly string AuthenticatedHome = ToAbsolute("app");

    // Accepts "app", "/app", "./app" or "app?x=1#y" and returns the same path under the path base, keeping the query and
    // fragment. A URL that is already absolute (scheme or path base) is returned unchanged, so the prefix is applied once.
    public static string ToAbsolute(string url)
    {
        if (IsUnderPathBase(url) || url.StartsWith("https://", StringComparison.Ordinal) || url.StartsWith("http://", StringComparison.Ordinal)) return url;

        var relative = url.StartsWith("./", StringComparison.Ordinal) ? url[2..] : url.TrimStart('/');
        return $"{PathBase}/{relative}";
    }

    public static bool IsUnderPathBase(string url)
    {
        return url == PathBase || url.StartsWith($"{PathBase}/", StringComparison.Ordinal) || url.StartsWith($"{PathBase}?", StringComparison.Ordinal);
    }

    // Only a local path below the path base is honoured, so a return path can never redirect off site
    public static string SanitizeReturnPath(string? returnPath)
    {
        if (string.IsNullOrEmpty(returnPath)) return AuthenticatedHome;
        if (!returnPath.StartsWith($"{PathBase}/", StringComparison.Ordinal) || returnPath.Contains("//") || returnPath.Contains('\\')) return AuthenticatedHome;
        return returnPath;
    }
}
