using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.Localization;

namespace Blazor.Tests.Localization;

// Each culture's own resource entries are compared without parent fallback, so a missing da-DK translation cannot pass by
// returning the English value, and every translation takes the same composite format arguments as its English text.
public sealed partial class LocalizationResourceTests
{
    private static readonly CultureInfo Danish = CultureInfo.GetCultureInfo("da-DK");

    public static TheoryData<string> ResourceClasses =>
    [
        nameof(CommonStrings), nameof(AuthenticationStrings), nameof(UsersStrings), nameof(AccountStrings), nameof(FluentComponentStrings)
    ];

    [Theory]
    [MemberData(nameof(ResourceClasses))]
    public void DanishResources_ShouldHaveOwnEntryForEveryEnglishKey(string resourceClass)
    {
        // Arrange
        var resourceManager = GetResourceManager(resourceClass);

        // Act
        var english = ReadOwnEntries(resourceManager, CultureInfo.InvariantCulture);
        var danish = ReadOwnEntries(resourceManager, Danish);

        // Assert
        english.Should().NotBeEmpty();
        danish.Keys.Should().BeEquivalentTo(english.Keys);
        danish.Values.Should().NotContain(text => string.IsNullOrWhiteSpace(text));
        english.Values.Should().NotContain(text => string.IsNullOrWhiteSpace(text));
    }

    [Theory]
    [MemberData(nameof(ResourceClasses))]
    public void DanishResources_ShouldUseSameFormatArgumentsAsEnglish(string resourceClass)
    {
        // Arrange
        var resourceManager = GetResourceManager(resourceClass);
        var english = ReadOwnEntries(resourceManager, CultureInfo.InvariantCulture);
        var danish = ReadOwnEntries(resourceManager, Danish);

        // Act
        var mismatches = english.Keys
            .Where(key => !GetFormatArguments(english[key]).SequenceEqual(GetFormatArguments(danish.GetValueOrDefault(key) ?? "")))
            .ToArray();

        // Assert
        mismatches.Should().BeEmpty();
        english.Values.Should().AllSatisfy(text => FluentActions.Invoking(() => string.Format(CultureInfo.InvariantCulture, text, "a", "b", "c")).Should().NotThrow());
        danish.Values.Should().AllSatisfy(text => FluentActions.Invoking(() => string.Format(Danish, text, "a", "b", "c")).Should().NotThrow());
    }

    [Fact]
    public void GeneratedClass_WhenUiCultureIsDanish_ShouldReturnDanishText()
    {
        // Arrange
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = Danish;

        try
        {
            // Act
            var danishText = CommonStrings.Continue;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var englishText = CommonStrings.Continue;

            // Assert
            danishText.Should().Be("Fortsæt");
            englishText.Should().Be("Continue");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static ResourceManager GetResourceManager(string resourceClass)
    {
        var type = typeof(CommonStrings).Assembly.GetType($"SharedKernel.Localization.{resourceClass}", true)!;
        return (ResourceManager)type.GetProperty(nameof(CommonStrings.ResourceManager))!.GetValue(null)!;
    }

    private static Dictionary<string, string> ReadOwnEntries(ResourceManager resourceManager, CultureInfo culture)
    {
        var resourceSet = resourceManager.GetResourceSet(culture, true, false);
        resourceSet.Should().NotBeNull($"the {culture.Name} resources must exist without parent fallback");
        return resourceSet.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    private static int[] GetFormatArguments(string text)
    {
        return FormatArgumentPattern().Matches(text).Select(match => int.Parse(match.Groups[1].Value)).Distinct().Order().ToArray();
    }

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex FormatArgumentPattern();
}
