using FluentAssertions;
using SharedKernel.Localization;

namespace Blazor.Tests.Localization;

public sealed class SupportedCulturesTests
{
    [Theory]
    [InlineData("da-DK", "en-US", "da-DK")]
    [InlineData("en-US", "da-DK", "en-US")]
    [InlineData("da", "en-US", "da-DK")]
    [InlineData("EN-gb", "da-DK", "en-US")]
    [InlineData("fr-FR", "da-DK", "en-US")]
    public void SelectLocale_WhenClaimIsPresent_ShouldIgnoreAcceptLanguage(string claimLocale, string acceptLanguage, string expectedLocale)
    {
        // Act
        var locale = SupportedCultures.SelectLocale(claimLocale, [acceptLanguage]);

        // Assert
        locale.Should().Be(expectedLocale);
    }

    [Theory]
    [InlineData(new[] { "da-DK", "en-US" }, "da-DK")]
    [InlineData(new[] { "DA-dk" }, "da-DK")]
    [InlineData(new[] { "da" }, "da-DK")]
    [InlineData(new[] { "fr-FR", "da-NO", "en-US" }, "da-DK")]
    [InlineData(new[] { "fr-FR", "de-DE" }, "en-US")]
    [InlineData(new[] { "x" }, "en-US")]
    [InlineData(new string[0], "en-US")]
    public void SelectLocale_WhenAnonymous_ShouldTakeFirstExactOrBaseLanguageMatchElseDefault(string[] acceptLanguages, string expectedLocale)
    {
        // Act
        var locale = SupportedCultures.SelectLocale(null, acceptLanguages);

        // Assert
        locale.Should().Be(expectedLocale);
    }

    [Theory]
    [InlineData("da-DK", "da-DK")]
    [InlineData("", "en-US")]
    [InlineData(null, "en-US")]
    [InlineData("zz-ZZ", "en-US")]
    public void GetCulture_ShouldReturnSupportedCultureOrDefault(string? locale, string expectedCulture)
    {
        // Act
        var culture = SupportedCultures.GetCulture(locale);

        // Assert
        culture.Name.Should().Be(expectedCulture);
    }
}
