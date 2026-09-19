// The supported client and server version window, decided in one place. The server's version is the one the bootstrap
// carries on the authenticated channel (BootstrapConfiguration.ApplicationVersionKey), never a header or a query value;
// the client's version is the informational version of this assembly, which a deployment replaces. A client whose major
// and minor match the server's is supported; any other client is unsupported and must not send a mutation.
//
// A version neither side states in a form this can parse leaves the window unknown, which never blocks a write: a client
// that cannot tell whether it is current is not evidence that it is stale, and refusing every write would take the
// application down on a build that lost its version metadata. The release rehearsal is what proves the trimmed publish
// keeps that metadata, because only an unsupported client shows the prompt. See docs/blazor-version-policy.md.

using System.Globalization;
using System.Reflection;
using Account.Features.Authentication.Queries;

namespace Blazor.Client.Bootstrap;

public enum ClientVersionSupport
{
    // No bootstrap has been applied yet, or one of the two versions could not be parsed
    Unknown,

    Supported,

    Unsupported
}

public static class ClientVersionWindow
{
    // The informational version of the WebAssembly client assembly, read through this type so a test host's entry
    // assembly cannot stand in for it
    public static string CurrentClientVersion { get; } = ReadVersionOf(typeof(ClientVersionWindow).Assembly);

    public static ClientVersionSupport Evaluate(string? clientVersion, string? serverVersion)
    {
        var client = ParseMajorMinor(clientVersion);
        var server = ParseMajorMinor(serverVersion);
        if (client is null || server is null) return ClientVersionSupport.Unknown;

        return client == server ? ClientVersionSupport.Supported : ClientVersionSupport.Unsupported;
    }

    public static string? ReadServerVersion(BootstrapResponse? bootstrap)
    {
        return bootstrap?.RuntimeConfiguration.GetValueOrDefault(BootstrapConfiguration.ApplicationVersionKey);
    }

    // Build metadata and a prerelease label name one build of a version, not a different contract, so 1.2.3+9fd2c1 and
    // 1.2.3-rc.1 are both in the 1.2 window
    internal static (int Major, int Minor)? ParseMajorMinor(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;

        var withoutBuildMetadata = version.Split('+')[0].Split('-')[0];
        var parts = withoutBuildMetadata.Split('.');
        if (parts.Length < 2) return null;

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)) return null;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)) return null;

        return (major, minor);
    }

    private static string ReadVersionOf(Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? string.Empty;
    }
}
