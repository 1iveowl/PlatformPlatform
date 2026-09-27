using Account.Features.BackOffice.Queries;

namespace Blazor.Client.BackOffice.Shell;

// How the back office names its signed-in identity from GET /api/back-office/me, as the React back office's avatar menu
// does: the display name, and initials from its first two words, "PP" while there is no name.
public static class BackOfficeUser
{
    private const string FallbackInitials = "PP";

    public static string GetInitials(MeResponse? me)
    {
        var words = (me?.DisplayName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return FallbackInitials;

        return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    public static string GetDisplayName(MeResponse? me)
    {
        return string.IsNullOrWhiteSpace(me?.DisplayName) ? BackOfficeStrings.BackOffice : me.DisplayName;
    }
}
