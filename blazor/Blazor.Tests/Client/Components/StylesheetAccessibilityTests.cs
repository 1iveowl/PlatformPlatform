using System.Text.RegularExpressions;
using FluentAssertions;

namespace Blazor.Tests.Client.Components;

// The parts of the accessibility bar that live in the host stylesheet and in the focus trap, which no renderer is needed
// to read: reduced motion, the minimum font size a focused field may compute to, and the elements the trap counts as
// focusable. The bar itself is .claude/rules/blazor/accessibility.md.
public sealed class StylesheetAccessibilityTests
{
    private static readonly string BlazorRoot = FindBlazorRoot();

    private static readonly string Stylesheet = File.ReadAllText(Path.Combine(BlazorRoot, "Blazor.Host", "wwwroot", "app.css"));

    private static readonly string UnsavedChangesModule = File.ReadAllText(Path.Combine(BlazorRoot, "Blazor.Client", "wwwroot", "js", "unsaved-changes.js"));

    [Fact]
    public void Stylesheet_WhenReducedMotionIsPreferred_ShouldShortenEveryAnimationTransitionAndScroll()
    {
        // Act
        var block = Section(Stylesheet, "@media (prefers-reduced-motion: reduce) {");

        // Assert
        block.Should().Contain("*,").And.Contain("*::before,").And.Contain("*::after");
        block.Should().Contain("animation-duration: 0s !important;");
        block.Should().Contain("animation-iteration-count: 1 !important;");
        block.Should().Contain("transition-duration: 0s !important;");
        block.Should().Contain("scroll-behavior: auto !important;");
    }

    // iOS Safari zooms the page when the field it focuses computes below 16px, which moves the rest of the form off
    // screen. The enhanced verification code input draws its value in the slots beside it and its own text is transparent,
    // so its size is read by the browser alone and was 1px until this bar was written.
    [Fact]
    public void Stylesheet_WhenAFontSizeIsGivenInPixels_ShouldBeSixteenOrMore()
    {
        // Act
        var pixelSizes = Regex.Matches(Stylesheet, @"font-size:\s*(\d+(?:\.\d+)?)px").Select(match => double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));

        // Assert
        pixelSizes.Should().NotBeEmpty().And.OnlyContain(size => size >= 16);
        Section(Stylesheet, ".one-time-password-enhanced .one-time-password-input {").Should().Contain("font-size: 16px;").And.Contain("color: transparent;");
    }

    // A field's validation messages are always rendered so its aria-describedby names an element that exists; the
    // container takes no room while it is empty
    [Fact]
    public void Stylesheet_WhenAFieldHasNoMessage_ShouldGiveItsValidationContainerNoRoom()
    {
        // Assert
        Section(Stylesheet, ".field-validation:empty {").Should().Contain("display: none;");
    }

    // An element with tabindex="-1" is reachable by script and by pointer but never by Tab, so a trap that counted it
    // would stop on it. The side pane's full-screen backdrop is such a button.
    [Theory]
    [InlineData("a[href]")]
    [InlineData("button:not([disabled])")]
    [InlineData("input:not([disabled])")]
    [InlineData("select:not([disabled])")]
    [InlineData("textarea:not([disabled])")]
    [InlineData("[tabindex]")]
    public void FocusTrap_WhenListingFocusableElements_ShouldExcludeNegativeTabIndexOnEveryBranch(string selector)
    {
        // Act
        var module = UnsavedChangesModule;

        // Assert
        module.Should().Contain($"\"{selector}\"");
        module.Should().Contain("`${selector}:not([tabindex='-1'])`");
    }

    private static string Section(string text, string opening)
    {
        var start = text.IndexOf(opening, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"the stylesheet declares {opening}");
        var end = text.IndexOf('}', start + opening.Length);
        return text[start..end];
    }

    private static string FindBlazorRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Blazor.Host", "wwwroot", "app.css"))) return directory.FullName;
        }

        throw new InvalidOperationException("The Blazor build root was not found above the test output directory.");
    }
}
