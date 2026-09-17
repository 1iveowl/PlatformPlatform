// The letters shown in place of an account logo, the React edition's getTenantInitials: one word gives its first two
// letters, several give the first letter of the first and of the last, and a blank name gives a question mark. Upper case
// is taken in the invariant culture, because the letters are shown identically in every UI culture.

using System.Globalization;

namespace Blazor.Client.Settings;

public static class TenantInitials
{
    private const string Unknown = "?";

    public static string Of(string? tenantName)
    {
        var parts = (tenantName ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => Unknown,
            1 => (parts[0].Length == 1 ? parts[0] : parts[0][..2]).ToUpper(CultureInfo.InvariantCulture),
            _ => $"{parts[0][0]}{parts[^1][0]}".ToUpper(CultureInfo.InvariantCulture)
        };
    }
}
