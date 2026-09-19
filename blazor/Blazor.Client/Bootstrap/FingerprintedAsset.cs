// Whether a URL names a content-fingerprinted asset of this edition. The static web asset pipeline inserts a fingerprint
// segment into the route of every asset it serves (_framework/Blazor.Client.6kbltrhlw8.wasm, app.5ae972z2ll.css), and a
// deployment gives every changed asset a new one. A request for such a route that the server no longer answers therefore
// means this document belongs to a publish that is gone, not that something is missing from the current one.
//
// Only routes under this edition's path base are considered, so a 404 from the account API or from an avatar on the
// storage account never looks like a stale asset. A fingerprint segment is at least eight lowercase base36 characters with
// both a digit and a letter, which the file and folder names of the published assets never are.

using System.Text.RegularExpressions;

namespace Blazor.Client.Bootstrap;

public static partial class FingerprintedAsset
{
    private static readonly string ClientAssemblyName = typeof(FingerprintedAsset).Assembly.GetName().Name!;

    // The asset route worth watching for a deployment, out of everything a document loaded: this client's own assembly
    // first, because its route changes whenever the application's code does, then another runtime file, then any
    // fingerprinted asset. A stylesheet, a framework script or a runtime assembly that two publishes share byte for byte
    // keeps its route across a deployment, so it would never report one.
    public static string? SelectWatchable(IEnumerable<string> urls)
    {
        return urls.Where(IsFingerprinted).OrderBy(Rank).FirstOrDefault();
    }

    public static bool IsFingerprinted(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;

        var path = ReadPath(url);
        if (path is null || !path.StartsWith($"{AppUrls.PathBase}/", StringComparison.Ordinal)) return false;

        var fileName = path[(path.LastIndexOf('/') + 1)..];
        // A pre-compressed variant is served under the same route with the encoding negotiated, but a request can name it
        var withoutEncoding = fileName.EndsWith(".br", StringComparison.Ordinal) || fileName.EndsWith(".gz", StringComparison.Ordinal)
            ? fileName[..fileName.LastIndexOf('.')]
            : fileName;

        return withoutEncoding.Split('.').Any(IsFingerprintSegment);
    }

    private static int Rank(string url)
    {
        if (!url.Contains("/_framework/", StringComparison.Ordinal)) return 3;
        if (!url.EndsWith(".wasm", StringComparison.Ordinal)) return 2;

        return url[(url.LastIndexOf('/') + 1)..].StartsWith($"{ClientAssemblyName}.", StringComparison.Ordinal) ? 0 : 1;
    }

    private static bool IsFingerprintSegment(string segment)
    {
        return segment.Length >= 8 && FingerprintSegment().IsMatch(segment) && segment.Any(char.IsAsciiDigit) && segment.Any(char.IsAsciiLetterLower);
    }

    private static string? ReadPath(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)) return absolute.AbsolutePath;

        var path = url.Split('?')[0].Split('#')[0];
        return path.StartsWith('/') ? path : null;
    }

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex FingerprintSegment();
}
