using System.Collections;
using System.Globalization;
using Blazor.Client.Localization;
using FluentAssertions;
using Microsoft.FluentUI.AspNetCore.Components;
using SharedKernel.Localization;

namespace Blazor.Tests.Localization;

public sealed class FluentResourceLocalizerTests
{
    private readonly IFluentLocalizer _localizer = new FluentResourceLocalizer();

    public static TheoryData<string> CoveredKeys
    {
        get
        {
            var keys = new TheoryData<string>();
            foreach (DictionaryEntry entry in FluentComponentStrings.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!)
            {
                keys.Add((string)entry.Key);
            }

            return keys;
        }
    }

    [Theory]
    [MemberData(nameof(CoveredKeys))]
    public void CoveredKey_ShouldBeKeyFluentUiDefines(string key)
    {
        // Act
        var fluentDefault = _localizer.GetDefault(key);

        // Assert
        fluentDefault.Should().NotBeNullOrEmpty().And.NotBe(key);
    }

    [Theory]
    [MemberData(nameof(CoveredKeys))]
    public void CoveredKey_WhenUiCultureIsDanish_ShouldReturnSharedDanishResource(string key)
    {
        // Act
        var text = WithUiCulture("da-DK", () => _localizer[key]);

        // Assert
        text.Should().Be(FluentComponentStrings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo("da-DK")));
    }

    [Fact]
    public void Indexer_WhenKeyIsNotCovered_ShouldFallBackToFluentUiEnglishDefault()
    {
        // Act
        var text = WithUiCulture("da-DK", () => _localizer["Tabs_MoreItems"]);

        // Assert
        text.Should().Be(_localizer.GetDefault("Tabs_MoreItems"));
    }

    [Fact]
    public void Indexer_WhenUiCultureIsEnglish_ShouldReturnSharedEnglishResource()
    {
        // Act
        var text = WithUiCulture("en-US", () => _localizer["TextInput_RequiredMessage"]);

        // Assert
        text.Should().Be("This field is required");
    }

    private static string WithUiCulture(string culture, Func<string> action)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
