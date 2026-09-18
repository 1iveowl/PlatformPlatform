using Account.Features.Users.Domain;
using Blazor.Client.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.Shell;

public sealed class MobileMenuModelTests
{
    [Theory]
    [InlineData("support@platformplatform.net", "mailto:support%40platformplatform.net")]
    [InlineData("  support@platformplatform.net  ", "mailto:support%40platformplatform.net")]
    public void SupportHref_WhenTheBrandNamesAnAddress_ShouldBeAMailLinkToIt(string supportEmail, string expected)
    {
        // Act
        var href = MobileMenuModel.SupportHref(supportEmail);

        // Assert
        href.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    public void SupportHref_WhenTheBrandNamesNoAddress_ShouldBeNull(string? supportEmail)
    {
        // Act
        var href = MobileMenuModel.SupportHref(supportEmail);

        // Assert
        href.Should().BeNull();
    }

    [Fact]
    public void ThemeItems_WhenGivenTheUserMenuItems_ShouldKeepTheThemeModesInOrderWithTheCurrentOneChecked()
    {
        // Arrange
        var items = UserMenuModel.Create(nameof(UserRole.Member), [], false, ThemeMode.Dark);

        // Act
        var themes = MobileMenuModel.ThemeItems(items);

        // Assert
        themes.Select(item => item.Theme).Should().Equal(ThemePreference.Modes.Cast<ThemeMode?>());
        themes.Where(item => item.Checked).Select(item => item.Theme).Should().Equal(ThemeMode.Dark);
    }
}
