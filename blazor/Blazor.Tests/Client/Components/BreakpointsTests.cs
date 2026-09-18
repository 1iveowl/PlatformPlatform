using Blazor.Client.Components;
using FluentAssertions;

namespace Blazor.Tests.Client.Components;

// A media query condition cannot read a CSS custom property, so the breakpoints are literals in four places: Breakpoints,
// the media queries and the --breakpoint-* declarations in the host stylesheet, and the matchMedia queries in shell.js,
// viewport.js and data-list.js. These tests fail when one drifts.
public sealed class BreakpointsTests
{
    private static readonly string BlazorRoot = FindBlazorRoot();

    private static readonly string Stylesheet = File.ReadAllText(Path.Combine(BlazorRoot, "Blazor.Host", "wwwroot", "app.css"));

    [Theory]
    [InlineData(Breakpoint.Small, "40rem", "sm")]
    [InlineData(Breakpoint.Medium, "48rem", "md")]
    [InlineData(Breakpoint.Large, "64rem", "lg")]
    [InlineData(Breakpoint.ExtraLarge, "80rem", "xl")]
    [InlineData(Breakpoint.ExtraExtraLarge, "96rem", "2xl")]
    public void Width_WhenBreakpointGiven_ShouldMirrorTheReactEditionsValue(Breakpoint breakpoint, string expectedWidth, string expectedSuffix)
    {
        // Act
        var width = Breakpoints.Width(breakpoint);
        var suffix = Breakpoints.Suffix(breakpoint);

        // Assert
        width.Should().Be(expectedWidth);
        suffix.Should().Be(expectedSuffix);
    }

    [Theory]
    [InlineData(Breakpoint.Small, "(max-width: 39.99rem)")]
    [InlineData(Breakpoint.Medium, "(max-width: 47.99rem)")]
    [InlineData(Breakpoint.ExtraLarge, "(max-width: 79.99rem)")]
    public void MaxWidthQuery_WhenBreakpointGiven_ShouldBeOneHundredthOfARemBelowIt(Breakpoint breakpoint, string expected)
    {
        // Act
        var query = Breakpoints.MaxWidthQuery(breakpoint);

        // Assert
        query.Should().Be(expected);
    }

    [Theory]
    [InlineData(Breakpoint.Small, "data-list-hide-below-sm")]
    [InlineData(Breakpoint.ExtraExtraLarge, "data-list-hide-below-2xl")]
    public void HideBelowClass_WhenBreakpointGiven_ShouldNameTheStylesheetsClass(Breakpoint breakpoint, string expected)
    {
        // Act
        var className = Breakpoints.HideBelowClass(breakpoint);

        // Assert
        className.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(AllBreakpoints))]
    public void HostStylesheet_WhenBreakpointGiven_ShouldDeclareItAndHideAColumnBelowIt(Breakpoint breakpoint)
    {
        // Assert
        Stylesheet.Should().Contain($"{Breakpoints.CustomProperty(breakpoint)}: {Breakpoints.Width(breakpoint)};");
        Stylesheet.Should().Contain($"@media {Breakpoints.MinWidthQuery(breakpoint)}");
        Stylesheet.Should().Contain($".{Breakpoints.HideBelowClass(breakpoint)}");
    }

    [Theory]
    [InlineData(Breakpoint.Small)]
    [InlineData(Breakpoint.ExtraLarge)]
    public void ShellModule_WhenShellBreakpointGiven_ShouldQueryTheSameWidth(Breakpoint breakpoint)
    {
        // Act
        var module = ReadModule("shell.js");

        // Assert
        module.Should().Contain($"\"{Breakpoints.MinWidthQuery(breakpoint)}\"");
    }

    [Theory]
    [MemberData(nameof(AllBreakpoints))]
    public void ViewportModule_WhenBreakpointGiven_ShouldQueryTheSameWidth(Breakpoint breakpoint)
    {
        // Act
        var module = ReadModule("viewport.js");

        // Assert
        module.Should().Contain($"\"{Breakpoints.MinWidthQuery(breakpoint)}\"");
    }

    [Fact]
    public void DataListModule_ShouldQueryTheSmallBreakpoint()
    {
        // Act
        var module = ReadModule("data-list.js");

        // Assert
        module.Should().Contain($"\"{Breakpoints.MinWidthQuery(Breakpoint.Small)}\"");
    }

    [Fact]
    public void Stylesheet_WhenMediaQueriesListed_ShouldUseOnlyTheBreakpoints()
    {
        // Arrange
        var allowed = Breakpoints.All.SelectMany(breakpoint => new[] { Breakpoints.MinWidthQuery(breakpoint), Breakpoints.MaxWidthQuery(breakpoint) }).ToHashSet();

        // Act
        var conditions = Stylesheet.Split('\n').Where(line => line.TrimStart().StartsWith("@media", StringComparison.Ordinal))
            .SelectMany(line => line.Trim().TrimStart('@').Replace("media", "").TrimEnd('{').Split(" and ", StringSplitOptions.TrimEntries));

        // Assert
        conditions.Should().OnlyContain(condition => allowed.Contains(condition));
    }

    public static TheoryData<Breakpoint> AllBreakpoints()
    {
        return [..Breakpoints.All];
    }

    private static string ReadModule(string fileName)
    {
        return File.ReadAllText(Path.Combine(BlazorRoot, "Blazor.Client", "wwwroot", "js", fileName));
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
