using System.Text.RegularExpressions;
using Blazor.Client.Preferences;
using FluentAssertions;

namespace Blazor.Tests.Client.Preferences;

public sealed class ZoomPreferenceTests
{
    private static readonly string BlazorRoot = FindBlazorRoot();
    private static readonly string ThemeScript = File.ReadAllText(Path.Combine(BlazorRoot, "Blazor.Host", "wwwroot", "js", "theme.js"));
    private static readonly string Stylesheet = File.ReadAllText(Path.Combine(BlazorRoot, "Blazor.Host", "wwwroot", "app.css"));

    [Theory]
    [InlineData("0.875", "0.875")]
    [InlineData("1", "1")]
    [InlineData("1.125", "1.125")]
    [InlineData("1.25", "1.25")]
    [InlineData(null, "1")]
    [InlineData("", "1")]
    [InlineData("2", "1")]
    [InlineData("1.0", "1")]
    [InlineData(" 1.25", "1")]
    [InlineData("1.25; background: red", "1")]
    public void Parse_WhenStoredValueGiven_ShouldReturnTheSupportedLevelOrTheDefault(string? stored, string expected)
    {
        // Act
        var level = ZoomPreference.Parse(stored);

        // Assert
        level.Should().Be(expected);
    }

    [Fact]
    public void Levels_WhenListed_ShouldBeTheReactLevelsInAscendingOrderWithTheDefault()
    {
        // Assert
        ZoomPreference.Levels.Should().Equal("0.875", "1", "1.125", "1.25");
        ZoomPreference.DefaultLevel.Should().Be("1");
        ZoomPreference.StorageKey.Should().Be("zoom-level");
    }

    [Fact]
    public void Label_WhenEveryLevelLabelled_ShouldGiveEachLevelItsOwnName()
    {
        // Act
        var labels = ZoomPreference.Levels.Select(ZoomPreference.Label).ToArray();

        // Assert
        labels.Should().OnlyHaveUniqueItems().And.NotContain(string.Empty);
        ZoomPreference.Label("unknown").Should().Be(ZoomPreference.Label(ZoomPreference.DefaultLevel));
    }

    [Fact]
    public void ThemeScript_WhenCompared_ShouldUseTheSameStorageKeyAndLevels()
    {
        // Assert
        ThemeScript.Should().Contain($"const zoomStorageKey = \"{ZoomPreference.StorageKey}\";");
        ThemeScript.Should().Contain($"const zoomLevels = [{string.Join(", ", ZoomPreference.Levels.Select(level => $"\"{level}\""))}];");
        ThemeScript.Should().Contain($"const defaultZoomLevel = \"{ZoomPreference.DefaultLevel}\";");
        ThemeScript.Should().Contain("return zoomLevels.includes(value) ? value : defaultZoomLevel;");
    }

    [Fact]
    public void ThemeScript_WhenCompared_ShouldWriteTheSameLocaleCookieAsLocalePreference()
    {
        // Assert
        ThemeScript.Should().Contain($"const localeCookieName = \"{LocalePreference.CookieName}\";");
        ThemeScript.Should().Contain($"const locales = [{string.Join(", ", LocalePreference.Locales.Select(locale => $"\"{locale}\""))}];");
        ThemeScript.Should().Contain($"const localeCookieMaxAgeSeconds = {(long)LocalePreference.MaxAge.TotalSeconds};");
        ThemeScript.Should().Contain("; Path=/; Max-Age=${localeCookieMaxAgeSeconds}; Secure; SameSite=Lax`");
    }

    [Fact]
    public void Stylesheet_WhenInspected_ShouldSetTheZoomVariableForEveryLevelOtherThanTheDefault()
    {
        // Assert
        foreach (var level in ZoomPreference.Levels.Where(level => level != ZoomPreference.DefaultLevel))
        {
            Stylesheet.Should().MatchRegex($@":root\[data-zoom-level=""{Regex.Escape(level)}""\]\s*\{{\s*--zoom-level: {Regex.Escape(level)};\s*\}}");
        }

        Stylesheet.Should().Contain("font-size: calc(100% * var(--zoom-level));");
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
