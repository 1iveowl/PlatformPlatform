// Renders the legal documents to the HTML the public pages show, under a policy rather than under trust in today's
// documents: HTML parsing is off, so any markup in a document becomes text; only the constructs the documents use are
// enabled; every link destination is checked against an allowlist and a rejected one loses its link and keeps its text;
// and the emitted HTML is verified element by element and attribute by attribute before it leaves the renderer. A
// violation throws, so a document that grew a construct outside the policy fails loud at startup instead of reaching a
// browser. The content security policy is a second line, never the first.

using System.Net;
using System.Text.RegularExpressions;
using Blazor.Client;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Blazor.Host.Shell;

public sealed class MarkdownPolicyException(string message) : Exception(message);

public static partial class MarkdownRenderer
{
    // The feature set the React edition's converter renders: headings, tables, emphasis, links, lists, block quotes and
    // code. Nothing else is enabled, and DisableHtml turns a raw HTML block or tag into escaped text
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UsePipeTables().Build();

    private static readonly HashSet<string> AllowedElements = new(StringComparer.Ordinal)
    {
        "h1", "h2", "h3", "h4", "h5", "h6", "p", "blockquote", "ul", "ol", "li", "strong", "em", "a", "code", "pre", "hr", "br",
        "table", "thead", "tbody", "tr", "th", "td"
    };

    public static string Render(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>().ToArray())
        {
            ApplyLinkPolicy(link);
        }

        // Alignment colons in a table's separator row would render as a style attribute, which the policy has no source for
        foreach (var table in document.Descendants<Table>())
        {
            foreach (var column in table.ColumnDefinitions)
            {
                column.Alignment = null;
            }
        }

        var html = document.ToHtml(Pipeline);
        Verify(html);
        return html;
    }

    // A local path of this edition, a fragment on the page itself, or an https address. Everything else, a scheme that can
    // execute and a protocol-relative or backslash-bearing path included, is not a destination this renderer emits
    public static string? ToAllowedDestination(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination)) return null;

        var candidate = destination.Trim();
        if (candidate.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)) || candidate.Contains('\\')) return null;
        if (candidate.StartsWith('#')) return candidate;
        if (candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return candidate;
        if (candidate.StartsWith('/') && !candidate.StartsWith("//", StringComparison.Ordinal)) return AppUrls.ToAbsolute(candidate);

        return null;
    }

    private static void ApplyLinkPolicy(LinkInline link)
    {
        var allowed = link.IsImage ? null : ToAllowedDestination(link.Url);
        if (allowed is not null)
        {
            link.Url = allowed;
            link.Title = null;
            return;
        }

        // A rejected link, and every image, keeps its text and loses everything the browser could act on
        link.ReplaceBy(new LiteralInline(GetText(link)), false);
    }

    private static string GetText(LinkInline link)
    {
        var text = link.Descendants().Select(descendant => descendant switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                _ => ""
            }
        );
        return string.Concat(text);
    }

    private static void Verify(string html)
    {
        foreach (var tag in TagPattern().Matches(html).Cast<Match>())
        {
            var element = tag.Groups["element"].Value.ToLowerInvariant();
            if (!AllowedElements.Contains(element)) throw new MarkdownPolicyException($"The rendered document contains the element '{element}'.");

            foreach (var attribute in AttributePattern().Matches(tag.Groups["attributes"].Value).Cast<Match>())
            {
                var name = attribute.Groups["name"].Value.ToLowerInvariant();
                var value = attribute.Groups["value"].Value.Trim('"', '\'');
                var isAllowed = (element, name) switch
                {
                    // The renderer escapes the destination it writes, so the emitted value is decoded before the same allowlist
                    // decides again; a percent-encoded scheme stays encoded and is rejected, as a browser would not decode it either
                    ("a", "href") => ToAllowedDestination(WebUtility.HtmlDecode(value)) is not null,
                    ("code", "class") => LanguageClassPattern().IsMatch(value),
                    _ => false
                };
                if (!isAllowed) throw new MarkdownPolicyException($"The rendered document sets '{name}' on '{element}'.");
            }
        }
    }

    [GeneratedRegex("""<\s*/?\s*(?<element>[a-zA-Z0-9]+)(?<attributes>(?:[^>"']|"[^"]*"|'[^']*')*)>""")]
    private static partial Regex TagPattern();

    [GeneratedRegex("""(?<name>[a-zA-Z_:][a-zA-Z0-9_:.-]*)\s*=\s*(?<value>"[^"]*"|'[^']*'|[^\s>]+)""")]
    private static partial Regex AttributePattern();

    [GeneratedRegex("^language-[a-z0-9-]+$")]
    private static partial Regex LanguageClassPattern();
}
