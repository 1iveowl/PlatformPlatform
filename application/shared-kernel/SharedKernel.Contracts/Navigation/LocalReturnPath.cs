namespace SharedKernel.Navigation;

/// <summary>
///     The one rule for a return path that the server and every client apply before redirecting to it: a same-origin,
///     root-absolute path that a browser resolves to exactly the path written, below a required prefix. The rule is
///     deliberately stricter than a URL parser. A browser strips tabs and line breaks, treats a backslash as a slash and
///     collapses dot segments (literal or percent-encoded) before it requests a URL, so a string check alone can be
///     steered to a different path or a different origin. Anything the rule does not recognise is rejected, and the
///     caller falls back to its own default destination.
/// </summary>
public static class LocalReturnPath
{
    public const int MaximumLength = 1024;

    private static readonly Uri ResolutionOrigin = new("https://return-path.invalid/");

    /// <summary>
    ///     The requiredPathPrefix must start and end with a slash, such as "/" or "/blazor/"; it is matched against the
    ///     path only, so the query string and fragment never satisfy it.
    /// </summary>
    public static bool IsValid(string? returnPath, string requiredPathPrefix = "/")
    {
        if (string.IsNullOrEmpty(returnPath) || returnPath.Length > MaximumLength) return false;
        if (!returnPath.StartsWith('/')) return false;
        if (!HasOnlyPrintableAsciiWithoutBackslash(returnPath) || !HasWellFormedEscapes(returnPath)) return false;

        var queryOrFragmentStart = returnPath.IndexOfAny(['?', '#']);
        var path = queryOrFragmentStart < 0 ? returnPath : returnPath[..queryOrFragmentStart];
        if (!IsCanonicalPath(path) || !path.StartsWith(requiredPathPrefix, StringComparison.Ordinal)) return false;

        return ResolvesToTheSamePath(returnPath, path, requiredPathPrefix);
    }

    private static bool HasOnlyPrintableAsciiWithoutBackslash(string returnPath)
    {
        return returnPath.All(character => character is > ' ' and < '' and not '\\');
    }

    private static bool HasWellFormedEscapes(string returnPath)
    {
        for (var index = returnPath.IndexOf('%'); index >= 0; index = returnPath.IndexOf('%', index + 1))
        {
            if (index + 2 >= returnPath.Length || !Uri.IsHexDigit(returnPath[index + 1]) || !Uri.IsHexDigit(returnPath[index + 2])) return false;
        }

        return true;
    }

    // Every segment but the last must be non-empty, and no segment may be a dot segment or hide a separator, a dot or a
    // control character behind an escape, since browsers and proxies decode those inconsistently
    private static bool IsCanonicalPath(string path)
    {
        var segments = path[1..].Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length == 0 && index < segments.Length - 1) return false;
            if (segment is "." or "..") return false;
            if (HasForbiddenEscape(segment)) return false;
        }

        return true;
    }

    private static bool HasForbiddenEscape(string segment)
    {
        for (var index = segment.IndexOf('%'); index >= 0; index = segment.IndexOf('%', index + 1))
        {
            var decoded = Convert.ToInt32(segment.Substring(index + 1, 2), 16);
            if (decoded is < 0x20 or 0x7F or '/' or '\\' or '.') return true;
        }

        return false;
    }

    private static bool ResolvesToTheSamePath(string returnPath, string path, string requiredPathPrefix)
    {
        if (!Uri.TryCreate(ResolutionOrigin, returnPath, out var resolved)) return false;
        if (resolved.Scheme != ResolutionOrigin.Scheme || resolved.Authority != ResolutionOrigin.Authority) return false;

        return resolved.AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase) && resolved.AbsolutePath.StartsWith(requiredPathPrefix, StringComparison.Ordinal);
    }
}
