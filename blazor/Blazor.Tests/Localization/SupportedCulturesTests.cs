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
    public void SelectLocale_WhenClaimIsPresent_ShouldIgnorePreferredLocaleAndAcceptLanguage(string claimLocale, string acceptLanguage, string expectedLocale)
    {
        // Act
        var locale = SupportedCultures.SelectLocale(claimLocale, acceptLanguage, [acceptLanguage]);

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
        var locale = SupportedCultures.SelectLocale(null, null, acceptLanguages);

        // Assert
        locale.Should().Be(expectedLocale);
    }

    [Theory]
    [InlineData("da-DK", "en-US", "da-DK")]
    [InlineData("DA-dk", "en-US", "da-DK")]
    [InlineData("en-US", "da-DK", "en-US")]
    public void SelectLocale_WhenAnonymousWithSupportedPreferredLocale_ShouldTakePreferredLocaleBeforeAcceptLanguage(string preferredLocale, string acceptLanguage, string expectedLocale)
    {
        // Act
        var locale = SupportedCultures.SelectLocale(null, preferredLocale, [acceptLanguage]);

        // Assert
        locale.Should().Be(expectedLocale);
    }

    [Theory]
    [InlineData("da")]
    [InlineData("fr-FR")]
    [InlineData("")]
    [InlineData("da-DK; Path=/")]
    [InlineData("<script>")]
    public void SelectLocale_WhenPreferredLocaleIsNotExactlySupported_ShouldFallBackToAcceptLanguage(string preferredLocale)
    {
        // Act
        var locale = SupportedCultures.SelectLocale(null, preferredLocale, ["da-DK"]);

        // Assert
        locale.Should().Be("da-DK");
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
