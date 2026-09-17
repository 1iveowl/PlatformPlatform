using Account.Features.ExternalAuthentication.Domain;
using SharedKernel.Navigation;

namespace Account.Features.ExternalAuthentication.Shared;

/// <summary>
///     Where a flow's callback sends the browser, fixed when the flow starts and carried in the data protected flow
///     cookie, so neither the callback's query string, the Referer, the Host nor a cookie the client can write selects it.
///     Each edition owns a path space: its success and error pages sit below it, and a return path outside it is dropped
///     for the edition's home. When the callback cannot tie a cookie to the flow in its state, the edition is unknown and
///     <see cref="Fallback" /> is used, which is the React edition's error page on the same origin.
/// </summary>
public sealed record ExternalLoginDestination
{
    // Mirrors AppUrls.PathBase in the Blazor edition, which this project cannot reference
    private const string BlazorPathBase = "/blazor/";

    public static readonly ExternalLoginDestination Fallback = new(ExternalLoginEdition.React, null);

    private ExternalLoginDestination(ExternalLoginEdition edition, string? returnPath)
    {
        Edition = edition;
        ReturnPath = returnPath;
    }

    public ExternalLoginEdition Edition { get; }

    public string? ReturnPath { get; }

    /// <summary>A return path that is not a canonical local path inside the edition's path space is dropped, never kept.</summary>
    public static ExternalLoginDestination Create(ExternalLoginEdition edition, string? returnPath)
    {
        return new ExternalLoginDestination(edition, IsValidReturnPath(edition, returnPath) ? returnPath : null);
    }

    public static bool IsValidReturnPath(ExternalLoginEdition edition, string? returnPath)
    {
        return LocalReturnPath.IsValid(returnPath, GetPathSpace(edition));
    }

    public static bool TryParseEdition(string? value, out ExternalLoginEdition edition)
    {
        edition = ExternalLoginEdition.React;
        if (string.IsNullOrEmpty(value)) return true;

        // Enum.TryParse also accepts numbers and comma separated names, so only an exact defined name is taken
        var match = Enum.GetNames<ExternalLoginEdition>().FirstOrDefault(name => name.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (match is null) return false;

        edition = Enum.Parse<ExternalLoginEdition>(match);
        return true;
    }

    public string GetSuccessUrl()
    {
        return ReturnPath ?? Edition switch
        {
            ExternalLoginEdition.React => "/",
            ExternalLoginEdition.Blazor => $"{BlazorPathBase}app",
            _ => throw new UnreachableException()
        };
    }

    public string GetErrorUrl(string error, string? referenceId)
    {
        var errorPage = Edition switch
        {
            ExternalLoginEdition.React => "/error",
            ExternalLoginEdition.Blazor => $"{BlazorPathBase}error",
            _ => throw new UnreachableException()
        };

        return $"{errorPage}?error={Uri.EscapeDataString(error)}&id={Uri.EscapeDataString(referenceId ?? string.Empty)}";
    }

    private static string GetPathSpace(ExternalLoginEdition edition)
    {
        return edition switch
        {
            ExternalLoginEdition.React => "/",
            ExternalLoginEdition.Blazor => BlazorPathBase,
            _ => throw new UnreachableException()
        };
    }
}
