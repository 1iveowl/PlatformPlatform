using Blazor.Host.Shell;
using FluentAssertions;

namespace Blazor.Tests.Public;

// The renderer's policy, not only its happy path: the constructs the legal documents use render, and markup, executable
// destinations and everything outside the element and attribute allowlist never reach the output.
public sealed class MarkdownRendererTests
{
    [Fact]
    public void Render_WhenDocumentUsesTheSupportedConstructs_ShouldRenderThemAsHtml()
    {
        // Arrange
        const string markdown = """
                                # Title

                                ## Section

                                ### Subsection

                                A paragraph with **bold** and *emphasis* text.

                                - First item
                                - Second item

                                > A quoted warning
                                >
                                > - with a bullet

                                | Column | Purpose |
                                | ------ | ------- |
                                | Value  | Reason  |

                                ```
                                A code block
                                ```

                                ---
                                """;

        // Act
        var html = MarkdownRenderer.Render(markdown);

        // Assert
        html.Should().Contain("<h1>Title</h1>").And.Contain("<h2>Section</h2>").And.Contain("<h3>Subsection</h3>");
        html.Should().Contain("<strong>bold</strong>").And.Contain("<em>emphasis</em>");
        html.Should().Contain("<ul>").And.Contain("<li>First item</li>").And.Contain("<blockquote>").And.Contain("<hr />");
        html.Should().Contain("<table>").And.Contain("<th>Column</th>").And.Contain("<td>Value</td>");
        html.Should().Contain("<pre><code>A code block");
    }

    [Theory]
    [InlineData("/legal/privacy", "/blazor/legal/privacy")]
    [InlineData("/legal/", "/blazor/legal/")]
    [InlineData("#section-1", "#section-1")]
    [InlineData("https://learn.microsoft.com/azure/", "https://learn.microsoft.com/azure/")]
    public void Render_WhenDestinationIsLocalOrHttps_ShouldKeepTheLinkUnderThePathBase(string destination, string expected)
    {
        // Act
        var html = MarkdownRenderer.Render($"See the [document]({destination}).");

        // Assert
        html.Should().Contain($"""<a href="{expected}">document</a>""");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("&#106;avascript:alert(1)")]
    [InlineData("%6aavascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("//evil.example.com/legal")]
    [InlineData("/\\evil.example.com")]
    [InlineData("http://insecure.example.com")]
    [InlineData("mailto:legal@example.com")]
    [InlineData("legal/terms")]
    public void Render_WhenDestinationIsNotAllowed_ShouldKeepTheTextAndDropTheLink(string destination)
    {
        // Act
        var html = MarkdownRenderer.Render($"See the [document]({destination}).");

        // Assert
        html.Should().Contain("See the document.").And.NotContain("<a").And.NotContain("href");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "script")]
    [InlineData("""<style>body { display: none }</style>""", "style")]
    [InlineData("""<svg onload="alert(1)"><use href="#x"/></svg>""", "svg")]
    [InlineData("""<img src=x onerror="alert(1)">""", "img")]
    [InlineData("""<iframe src="https://evil.example.com"></iframe>""", "iframe")]
    [InlineData("""<form action="https://evil.example.com"><input name="a"></form>""", "form")]
    [InlineData("""<p onclick="alert(1)">Text</p>""", "p onclick")]
    [InlineData("<div><span>Text</span></div>", "div")]
    public void Render_WhenDocumentContainsMarkup_ShouldRenderItAsEscapedText(string markdown, string forbidden)
    {
        // Act
        var html = MarkdownRenderer.Render(markdown);

        // Assert
        html.Should().NotContain($"<{forbidden}").And.Contain($"&lt;{forbidden}");
        html.Should().NotContain("&lt;/p>").And.NotContain("style=\"").And.NotContain("onclick=\"").And.NotContain("onerror=\"").And.NotContain("onload=\"");
    }

    [Theory]
    [InlineData("[Unclosed](")]
    [InlineData("[Empty]()")]
    [InlineData("![An image](/images/logo.png)")]
    [InlineData("[Link with a newline](java\nscript:alert(1))")]
    public void Render_WhenLinkIsMalformedOrAnImage_ShouldEmitNoLinkAndNoImage(string markdown)
    {
        // Act
        var html = MarkdownRenderer.Render(markdown);

        // Assert
        html.Should().NotContain("<a ").And.NotContain("<img").And.NotContain("href");
    }

    [Fact]
    public void Render_WhenTableDeclaresAlignment_ShouldEmitNoStyleAttribute()
    {
        // Arrange
        const string markdown = """
                                | Left | Center | Right |
                                | :--- | :----: | ----: |
                                | a    | b      | c     |
                                """;

        // Act
        var html = MarkdownRenderer.Render(markdown);

        // Assert
        html.Should().Contain("<th>Left</th>").And.NotContain("style");
    }
}
