// Root-absolute links to the static assets: the import map and the preload links the host page renders, and the Link
// header targets of static asset responses. base-uri 'none' makes the browser ignore <base href>, so a relative URL would
// resolve against the document URL and break on any page below the first path segment.

using System.Runtime.CompilerServices;
using System.Text;
using Blazor.Client;
using Microsoft.AspNetCore.Components;

namespace Blazor.Host.Shell;

public sealed record PreloadLink(
    string Href,
    string Rel,
    string? As,
    string? FetchPriority,
    string? CrossOrigin,
    string? Integrity,
    int Order
);

public sealed class AssetLinks
{
    private readonly ConditionalWeakTable<ImportMapDefinition, ImportMapDefinition> _absoluteImportMaps = new();

    // Relative ("./") specifiers and targets become root-absolute; bare specifiers such as "_framework/resource-collection.js"
    // are matched literally by the browser and stay unchanged. The rewrite is kept per source definition, so every page
    // rendered from the same definition shares one result.
    public ImportMapDefinition GetAbsoluteImportMap(ImportMapDefinition source)
    {
        return _absoluteImportMaps.GetValue(source, definition =>
            {
                return new ImportMapDefinition(
                    RewriteEntries(definition.Imports),
                    definition.Scopes?.ToDictionary(scope => ToAbsoluteSpecifier(scope.Key), scope => RewriteEntries(scope.Value)!),
                    definition.Integrity?.ToDictionary(entry => ToAbsoluteSpecifier(entry.Key), entry => entry.Value)
                );
            }
        );
    }

    // The link elements <ResourcePreloader/> would render, with root-absolute hrefs; it renders relative hrefs and takes no
    // parameters. A preload is only reused when its integrity matches the later module request, which takes it from the import map.
    public static IEnumerable<PreloadLink> GetPreloadLinks(ResourceAssetCollection assets, ImportMapDefinition importMap)
    {
        foreach (var asset in assets)
        {
            var properties = asset.Properties?.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
            if (properties is null || !properties.TryGetValue("preloadrel", out var rel)) continue;

            properties.TryGetValue("preloadorder", out var order);
            var href = AppUrls.ToAbsolute(asset.Url);
            yield return new PreloadLink(
                href,
                rel,
                properties.GetValueOrDefault("preloadas"),
                properties.GetValueOrDefault("preloadpriority"),
                properties.GetValueOrDefault("preloadcrossorigin"),
                importMap.Integrity?.GetValueOrDefault(href),
                int.TryParse(order, out var parsedOrder) ? parsedOrder : int.MaxValue
            );
        }
    }

    // Static asset responses name further preloads in a Link header with targets relative to the asset: the scoped stylesheet
    // bundle lists "_content/<package>/<package>.bundle.scp.css". Chromium resolves such a target against the response URL, as
    // RFC 8288 specifies, but WebKit and Firefox resolve it against the document URL, which requests a path below the page and
    // returns 404 on any page deeper than one segment. Rewriting each relative target root-absolute against the response URL
    // makes every browser request the file Chromium already does.
    public static Task RewriteLinkHeadersAsync(HttpContext context, RequestDelegate next)
    {
        var responsePath = $"{context.Request.PathBase}{context.Request.Path}";
        context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                if (headers.Link.Count > 0) headers.Link = ToRootAbsoluteLinkHeader(headers.Link.ToString(), responsePath);
                return Task.CompletedTask;
            }
        );

        return next(context);
    }

    public static string ToRootAbsoluteLinkHeader(string header, string responsePath)
    {
        if (!Uri.TryCreate($"http://host{responsePath}", UriKind.Absolute, out var responseUrl)) return header;

        var rewritten = new StringBuilder(header.Length);
        var position = 0;
        while (true)
        {
            var start = header.IndexOf('<', position);
            var end = start < 0 ? -1 : header.IndexOf('>', start + 1);
            if (end < 0) break;

            rewritten.Append(header, position, start + 1 - position);
            rewritten.Append(ToRootAbsoluteLinkTarget(header[(start + 1)..end], responseUrl));
            rewritten.Append('>');
            position = end + 1;
        }

        rewritten.Append(header, position, header.Length - position);
        return rewritten.ToString();
    }

    private static string ToRootAbsoluteLinkTarget(string target, Uri responseUrl)
    {
        if (target.StartsWith('/') || target.StartsWith("http://", StringComparison.Ordinal) || target.StartsWith("https://", StringComparison.Ordinal)) return target;

        var resolved = new Uri(responseUrl, target);
        return $"{resolved.AbsolutePath}{resolved.Query}{resolved.Fragment}";
    }

    private static IReadOnlyDictionary<string, string>? RewriteEntries(IReadOnlyDictionary<string, string>? entries)
    {
        return entries?.ToDictionary(entry => ToAbsoluteSpecifier(entry.Key), entry => AppUrls.ToAbsolute(entry.Value));
    }

    private static string ToAbsoluteSpecifier(string specifier)
    {
        return specifier.StartsWith("./", StringComparison.Ordinal) ? AppUrls.ToAbsolute(specifier) : specifier;
    }
}
