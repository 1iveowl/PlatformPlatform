// Spike code (Blazor edition, stage B2): form models and the return path guard for the static SSR public surface.

using System.ComponentModel.DataAnnotations;

namespace Blazor.Host.Components.Pages.Public;

public sealed class EmailForm
{
    [Required]
    [EmailAddress]
    [StringLength(100)]
    public string Email { get; set; } = "";
}

public sealed class OneTimePasswordForm
{
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string OneTimePassword { get; set; } = "";
}

public static class ReturnPaths
{
    public const string AuthenticatedHome = "/blazor/app";

    // Only a local path below the edition's path base is honoured, so the return path cannot redirect off site
    public static string Sanitize(string? returnPath)
    {
        if (string.IsNullOrEmpty(returnPath)) return AuthenticatedHome;
        if (!returnPath.StartsWith("/blazor/", StringComparison.Ordinal) || returnPath.Contains("//") || returnPath.Contains('\\')) return AuthenticatedHome;
        return returnPath;
    }
}
