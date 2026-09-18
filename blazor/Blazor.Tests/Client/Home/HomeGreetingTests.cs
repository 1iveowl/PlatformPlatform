using System.Globalization;
using Blazor.Client.Home;
using FluentAssertions;

namespace Blazor.Tests.Client.Home;

public sealed class HomeGreetingTests
{
    [Theory]
    [InlineData(0, HomeGreetingKind.Night)]
    [InlineData(4, HomeGreetingKind.Night)]
    [InlineData(5, HomeGreetingKind.Morning)]
    [InlineData(11, HomeGreetingKind.Morning)]
    [InlineData(12, HomeGreetingKind.Afternoon)]
    [InlineData(16, HomeGreetingKind.Afternoon)]
    [InlineData(17, HomeGreetingKind.Evening)]
    [InlineData(23, HomeGreetingKind.Evening)]
    public void GetKind_WhenHourOfDay_ShouldFollowTheSameBoundariesAsTheReactEdition(int hourOfDay, HomeGreetingKind expected)
    {
        // Act
        var kind = HomeGreeting.GetKind(hourOfDay);

        // Assert
        kind.Should().Be(expected);
    }

    [Theory]
    [InlineData(9, "Ann", "Good morning, Ann")]
    [InlineData(9, "", "Good morning")]
    [InlineData(9, null, "Good morning")]
    [InlineData(9, "  ", "Good morning")]
    [InlineData(14, "Ann", "Good afternoon, Ann")]
    [InlineData(20, "Ann", "Good evening, Ann")]
    [InlineData(2, "Ann", "Burning the midnight oil, Ann?")]
    [InlineData(2, null, "Burning the midnight oil?")]
    public void GetText_WhenCultureIsEnglish_ShouldNameTheUserWhenTheAccountHasAFirstName(int hourOfDay, string? firstName, string expected)
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var text = HomeGreeting.GetText(hourOfDay, firstName);

        // Assert
        text.Should().Be(expected);
    }

    [Theory]
    [InlineData(9, "Ann", "God morgen, Ann")]
    [InlineData(14, null, "God eftermiddag")]
    [InlineData(20, "Ann", "God aften, Ann")]
    [InlineData(2, "Ann", "Sidder du stadig oppe, Ann?")]
    public void GetText_WhenCultureIsDanish_ShouldGreetInDanish(int hourOfDay, string? firstName, string expected)
    {
        // Arrange
        using var culture = new CultureScope("da-DK");

        // Act
        var text = HomeGreeting.GetText(hourOfDay, firstName);

        // Assert
        text.Should().Be(expected);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string locale)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }
}
