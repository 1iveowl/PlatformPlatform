// The legal documents the host serves, as an explicit allowlist of three keys mapped to the three embedded documents. No
// route value ever becomes a resource name: a path that is not one of the three keys resolves to nothing and the page
// renders the not-found page. Every document is read and rendered once when the host starts, so a document outside the
// renderer's policy fails loud at startup, and a page render is a dictionary lookup.

using SharedKernel.Localization;

namespace Blazor.Host.Shell;

public sealed class LegalDocuments
{
    public const string TermsKey = "terms";
    public const string PrivacyKey = "privacy";
    public const string DpaKey = "dpa";

    // The documents are published in en-US only, so the text stays English inside chrome of any culture and says so
    public const string DocumentLanguage = "en-US";

    private static readonly Dictionary<string, string> ResourceNames = new(StringComparer.Ordinal)
    {
        [TermsKey] = "legal/terms.en-US.md",
        [PrivacyKey] = "legal/privacy.en-US.md",
        [DpaKey] = "legal/dpa.en-US.md"
    };

    private readonly Dictionary<string, string> _documents;

    public LegalDocuments()
    {
        _documents = ResourceNames.ToDictionary(entry => entry.Key, entry => MarkdownRenderer.Render(Read(entry.Value)), StringComparer.Ordinal);
    }

    public static IReadOnlyCollection<string> Keys => ResourceNames.Keys;

    public string? GetHtml(string? key)
    {
        return key is null ? null : _documents.GetValueOrDefault(key);
    }

    // The key of the page being rendered, from the path below the path base ("legal/terms"), or null for anything else
    public static string? GetKey(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;

        var path = relativePath.Split('?', '#')[0].Trim('/');
        var segments = path.Split('/');
        if (segments.Length != 2 || !segments[0].Equals("legal", StringComparison.Ordinal)) return null;

        return ResourceNames.ContainsKey(segments[1]) ? segments[1] : null;
    }

    public static string GetTitle(string key)
    {
        return key switch
        {
            PrivacyKey => LegalStrings.PrivacyPolicy,
            DpaKey => LegalStrings.DataProcessingAgreement,
            _ => LegalStrings.TermsOfService
        };
    }

    private static string Read(string resourceName)
    {
        using var stream = typeof(LegalDocuments).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
