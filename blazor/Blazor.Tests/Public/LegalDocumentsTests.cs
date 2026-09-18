using System.Text.RegularExpressions;
using Blazor.Host.Shell;
using FluentAssertions;

namespace Blazor.Tests.Public;

// The allowlist of documents the host serves: three keys, three embedded documents, and nothing else reachable. The two
// internal documents that sit beside them in the React edition's folder are not embedded and have no key.
public sealed class LegalDocumentsTests
{
    private static readonly LegalDocuments Documents = new();

    [Theory]
    [InlineData(LegalDocuments.TermsKey, "Terms of Service")]
    [InlineData(LegalDocuments.PrivacyKey, "Privacy Policy")]
    [InlineData(LegalDocuments.DpaKey, "Data Processing Agreement")]
    public void GetHtml_WhenKeyIsAllowed_ShouldRenderTheDocumentUnderThePolicy(string key, string heading)
    {
        // Act
        var html = Documents.GetHtml(key);

        // Assert
        html.Should().NotBeNull().And.Contain($"<h1>{heading}</h1>").And.Contain("<h2>").And.Contain("<blockquote>").And.Contain("<li>");
        html.Should().NotContain("<script").And.NotContain("style=").And.NotContain("javascript:");
        Regex.IsMatch(html, @"\son[a-z]+\s*=").Should().BeFalse("no event attribute may reach the document");
        html.Should().Contain("""<a href="/blazor/legal/""");
    }

    [Fact]
    public void GetHtml_WhenTheDocumentHasATableOrACodeBlock_ShouldRenderIt()
    {
        // Act
        var privacy = Documents.GetHtml(LegalDocuments.PrivacyKey);
        var dpa = Documents.GetHtml(LegalDocuments.DpaKey);

        // Assert
        privacy.Should().Contain("<table>").And.Contain("<th>Data Type</th>");
        dpa.Should().Contain("<pre><code>");
    }

    [Theory]
    [InlineData("cross-references")]
    [InlineData("legitimate-interest-assessment")]
    [InlineData("terms.en-US")]
    [InlineData("../platform-settings")]
    [InlineData("")]
    [InlineData(null)]
    public void GetHtml_WhenKeyIsNotAllowed_ShouldReturnNothing(string? key)
    {
        // Act
        var html = Documents.GetHtml(key);

        // Assert
        html.Should().BeNull();
    }

    [Theory]
    [InlineData("legal/terms", LegalDocuments.TermsKey)]
    [InlineData("legal/privacy/", LegalDocuments.PrivacyKey)]
    [InlineData("legal/dpa?print=1", LegalDocuments.DpaKey)]
    [InlineData("legal/dpa#section-2", LegalDocuments.DpaKey)]
    public void GetKey_WhenPathNamesADocument_ShouldReturnItsKey(string path, string expected)
    {
        // Act
        var key = LegalDocuments.GetKey(path);

        // Assert
        key.Should().Be(expected);
    }

    [Theory]
    [InlineData("legal")]
    [InlineData("legal/unknown")]
    [InlineData("legal/terms/extra")]
    [InlineData("legal/cross-references.internal")]
    [InlineData("app/terms")]
    [InlineData("")]
    [InlineData(null)]
    public void GetKey_WhenPathNamesNoDocument_ShouldReturnNothing(string? path)
    {
        // Act
        var key = LegalDocuments.GetKey(path);

        // Assert
        key.Should().BeNull();
    }

    [Fact]
    public void EmbeddedResources_WhenTheHostIsBuilt_ShouldCarryTheThreePublishedDocumentsOnly()
    {
        // Act
        var documents = typeof(LegalDocuments).Assembly.GetManifestResourceNames().Where(name => name.EndsWith(".md", StringComparison.Ordinal)).ToArray();

        // Assert
        documents.Should().BeEquivalentTo("legal/terms.en-US.md", "legal/privacy.en-US.md", "legal/dpa.en-US.md");
        documents.Should().NotContain(name => name.Contains("internal", StringComparison.OrdinalIgnoreCase));
    }
}
