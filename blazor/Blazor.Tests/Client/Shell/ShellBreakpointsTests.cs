using Blazor.Client.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.Shell;

// A media query condition cannot read a CSS custom property, so the breakpoints are literals in three places: ShellBreakpoints,
// the media queries in the host stylesheet and the matchMedia queries in shell.js. These tests fail when one drifts.
public sealed class ShellBreakpointsTests
{
    private static readonly string BlazorRoot = FindBlazorRoot();

    [Theory]
    [InlineData(ShellBreakpoints.Small)]
    [InlineData(ShellBreakpoints.ExtraLarge)]
    public void HostStylesheet_WhenShellBreakpointGiven_ShouldHaveAMinWidthMediaQueryForIt(string breakpoint)
    {
        // Act
        var stylesheet = File.ReadAllText(Path.Combine(BlazorRoot, "Blazor.Host", "wwwroot", "app.css"));

        // Assert
        stylesheet.Should().Contain($"@media (min-width: {breakpoint})");
    }

    [Theory]
    [InlineData(ShellBreakpoints.Small)]
    [InlineData(ShellBreakpoints.ExtraLarge)]
    public void ShellModule_WhenShellBreakpointGiven_ShouldQueryTheSameWidth(string breakpoint)
    {
        // Act
        var module = File.ReadAllText(Path.Combine(BlazorRoot, "Blazor.Client", "wwwroot", "js", "shell.js"));

        // Assert
        module.Should().Contain($"\"(min-width: {breakpoint})\"");
    }

    [Fact]
    public void Stylesheet_WhenMediaQueriesListed_ShouldUseOnlyTheShellBreakpoints()
    {
        // Arrange
        var stylesheet = File.ReadAllText(Path.Combine(BlazorRoot, "Blazor.Host", "wwwroot", "app.css"));
        HashSet<string> allowed = [$"(min-width: {ShellBreakpoints.Small})", $"(min-width: {ShellBreakpoints.ExtraLarge})", "(max-width: 79.99rem)"];

        // Act
        var conditions = stylesheet.Split('\n').Where(line => line.TrimStart().StartsWith("@media", StringComparison.Ordinal))
            .SelectMany(line => line.Trim().TrimStart('@').Replace("media", "").TrimEnd('{').Split(" and ", StringSplitOptions.TrimEntries));

        // Assert
        conditions.Should().OnlyContain(condition => allowed.Contains(condition));
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
