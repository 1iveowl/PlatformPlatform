using Blazor.Client.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.Shell;

public sealed class ThemePreferenceTests
{
    private static readonly string ThemeScript = File.ReadAllText(Path.Combine(FindBlazorRoot(), "Blazor.Host", "wwwroot", "js", "theme.js"));

    [Theory]
    [InlineData("light", false, AppliedTheme.Light)]
    [InlineData("light", true, AppliedTheme.Light)]
    [InlineData("dark", false, AppliedTheme.Dark)]
    [InlineData("dark", true, AppliedTheme.Dark)]
    [InlineData("system", false, AppliedTheme.Light)]
    [InlineData("system", true, AppliedTheme.Dark)]
    [InlineData(null, true, AppliedTheme.Dark)]
    [InlineData(null, false, AppliedTheme.Light)]
    [InlineData("", true, AppliedTheme.Dark)]
    [InlineData("DARK", false, AppliedTheme.Light)]
    [InlineData("\"dark\"", false, AppliedTheme.Light)]
    public void Resolve_WhenStoredValueAndSystemPreferenceGiven_ShouldApplyTheSelectedOrSystemTheme(string? stored, bool prefersDark, AppliedTheme expected)
    {
        // Act
        var applied = ThemePreference.Resolve(stored, prefersDark);

        // Assert
        applied.Should().Be(expected);
    }

    [Theory]
    [InlineData("system", ThemeMode.System)]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData(null, ThemeMode.System)]
    [InlineData("sepia", ThemeMode.System)]
    public void Parse_WhenStoredValueGiven_ShouldReturnTheModeOrSystem(string? stored, ThemeMode expected)
    {
        // Act
        var mode = ThemePreference.Parse(stored);

        // Assert
        mode.Should().Be(expected);
    }

    [Fact]
    public void Format_WhenEveryModeFormatted_ShouldRoundTripThroughParse()
    {
        // Act
        var formatted = ThemePreference.Modes.Select(ThemePreference.Format).ToArray();

        // Assert
        formatted.Should().Equal("system", "light", "dark");
        formatted.Select(ThemePreference.Parse).Should().Equal(ThemePreference.Modes);
    }

    [Fact]
    public void ThemeScript_WhenCompared_ShouldUseTheSameStorageKeyModesAndSystemQuery()
    {
        // Assert
        ThemeScript.Should().Contain($"const storageKey = \"{ThemePreference.StorageKey}\";");
        ThemeScript.Should().Contain($"const modes = [{string.Join(", ", ThemePreference.Modes.Select(mode => $"\"{ThemePreference.Format(mode)}\""))}];");
        ThemeScript.Should().Contain("\"(prefers-color-scheme: dark)\"").And.Contain("return modes.includes(value) ? value : \"system\";");
    }

    [Fact]
    public void ThemeScript_WhenInspected_ShouldWriteNoStyleAttribute()
    {
        // Assert
        ThemeScript.Should().NotContain(".style").And.NotContain("setAttribute(\"style\"").And.NotContain("innerHTML");
    }

    private static string FindBlazorRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Blazor.Host", "wwwroot", "app.css")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The blazor build root was not found above the test output folder.");
    }
}
