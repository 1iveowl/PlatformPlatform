using SharedKernel.Navigation;

namespace Blazor.Client;

// The one place that knows the path base the gateway routes to this edition. The policy's base-uri 'none' makes the
// browser ignore <base href>, so every URL the host page or a component renders is root-absolute and built here.
public static class AppUrls
{
    public const string PathBase = "/blazor";

    // The edition name the account API routes an external authentication callback back to this edition with
    public const string Edition = "Blazor";

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

    // Only a canonical local path below the path base is honoured, under the same rule the account API applies to the
    // return path of an external login, so a return path can neither redirect off site nor resolve outside this edition
    public static string SanitizeReturnPath(string? returnPath)
    {
        return LocalReturnPath.IsValid(returnPath, $"{PathBase}/") ? returnPath! : AuthenticatedHome;
    }
}
