// The offline shell: the one document the service worker is allowed to keep, and the paths its worker may answer from the
// cache. Everything the worker decides is stated here, in C#, and substituted into the worker source the host serves, so a
// test can assert the allowlist against the endpoints of the running host instead of parsing JavaScript.
//
// The shell document is anonymous and carries no user, tenant, nonce or antiforgery value, because a cached document is
// replayed to whoever launches the installed application next. It is the only document that is not no-store, which is what
// lets it be stored at all; HostShell.ApplyPageHeadersAsync makes that exception for the page carrying OfflineShellPageAttribute
// and for no other.
//
// The cache name carries the client version the release policy compares (docs/blazor-version-policy.md), so a deployment
// that changes that version changes the worker source, the browser installs the new worker and its activation drops every
// cache of the previous version, the stored shell document included.

using System.Reflection;
using Blazor.Client;
using Blazor.Client.Bootstrap;

namespace Blazor.Host.Shell;

// Marks the one page the service worker stores and serves when the network is unavailable. It is rendered without a nonce
// and served storable rather than no-store, so it must never show a user, a tenant or a token.
[AttributeUsage(AttributeTargets.Class)]
public sealed class OfflineShellPageAttribute : Attribute;

public static class OfflineShell
{
    // Served by HostApplication rather than by the static asset pipeline, so it answers no-cache under the path base and
    // names its own scope; a fingerprinted route could not be registered as a worker at a stable address
    public const string WorkerPath = "/service-worker.js";

    public const string ShellDocumentPath = "/app/offline";

    // The first path segment of every route that hosts the authenticated interactive surface. A navigation to a path that
    // does not start with one of these is never answered from the cache: the landing page, login, signup, the verification
    // pages, the legal documents, the status pages and every external authentication callback go to the network.
    // HostSecurityTests.OfflineShell asserts this against the running host's endpoints, in both directions.
    public static readonly string[] AppNavigationSegments = ["app", "account", "user", "welcome"];

    public static bool IsAppNavigation(string path)
    {
        var prefix = $"{AppUrls.PathBase}/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return false;

        var rest = path[prefix.Length..];
        var segment = rest.Split('/')[0].Split('?')[0];
        return AppNavigationSegments.Contains(segment, StringComparer.Ordinal);
    }

    // The worker source with the four values the host decides substituted into it. The template is an embedded resource
    // rather than a static web asset, because the static asset pipeline would also serve it at a fingerprinted route and
    // at a second, cacheable one, and a worker needs one stable address with headers of its own.
    public static string BuildWorkerScript()
    {
        using var stream = typeof(OfflineShell).Assembly.GetManifestResourceStream("service-worker.js")
                           ?? throw new InvalidOperationException("Embedded resource 'service-worker.js' not found.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd()
            .Replace("__PATH_BASE__", $"{AppUrls.PathBase}/", StringComparison.Ordinal)
            .Replace("__SHELL_DOCUMENT__", AppUrls.ToAbsolute(ShellDocumentPath), StringComparison.Ordinal)
            .Replace("__APP_SEGMENTS__", string.Join(",", AppNavigationSegments), StringComparison.Ordinal)
            .Replace("__CACHE_VERSION__", GetCacheVersion(), StringComparison.Ordinal);
    }

    // The client assembly's informational version, the same value the version window compares with the server's, so a
    // release that moves clients out of the window also drops what the previous release cached. A build without that
    // metadata falls back to the host assembly's, and a build with neither leaves one cache for every such build.
    private static string GetCacheVersion()
    {
        var version = ClientVersionWindow.CurrentClientVersion;
        if (version.Length == 0) version = typeof(OfflineShell).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

        return version.Length == 0 ? "unversioned" : version.Replace('"', '-');
    }
}
