using Account.Features.Users.Domain;
using Blazor.Client.Components.Menus;
using Blazor.Client.Session;
using Blazor.Client.Shell;
using FluentAssertions;
using SharedKernel.Domain;

namespace Blazor.Tests.Client.Shell;

public sealed class UserMenuModelTests
{
    private static readonly TenantSwitcherOption CurrentTenant = new(new TenantId(1), "Primary", true);
    private static readonly TenantSwitcherOption OtherTenant = new(new TenantId(2), "Secondary", false);

    [Fact]
    public void Create_WhenMemberWithOneTenant_ShouldHaveProfilePreferencesAndLogOutOnly()
    {
        // Act
        var items = UserMenuModel.Create(nameof(UserRole.Member), [CurrentTenant], false);

        // Assert
        items.Select(item => item.Kind).Should().Equal(UserMenuItemKind.Profile, UserMenuItemKind.Preferences, UserMenuItemKind.LogOut);
        items[0].Href.Should().Be("/blazor/user/profile");
        items[1].Href.Should().Be("/blazor/user/preferences");
    }

    [Theory]
    [InlineData(nameof(UserRole.Owner))]
    [InlineData(nameof(UserRole.Admin))]
    public void Create_WhenOwnerOrAdmin_ShouldHaveAccountSettingsBeforeLogOut(string role)
    {
        // Act
        var items = UserMenuModel.Create(role, [CurrentTenant], false);

        // Assert
        items.Select(item => item.Kind).Should().Equal(UserMenuItemKind.Profile, UserMenuItemKind.Preferences, UserMenuItemKind.AccountSettings, UserMenuItemKind.LogOut);
        items[2].Href.Should().Be("/blazor/account/settings");
    }

    [Fact]
    public void Create_WhenSeveralTenants_ShouldListEveryTenantWithTheCurrentOneDisabled()
    {
        // Act
        var items = UserMenuModel.Create(nameof(UserRole.Member), [CurrentTenant, OtherTenant], false);

        // Assert
        var tenants = items.Where(item => item.Kind == UserMenuItemKind.SwitchTenant).ToArray();
        tenants.Select(item => item.Tenant).Should().Equal(CurrentTenant, OtherTenant);
        tenants.Select(item => item.Disabled).Should().Equal(true, false);
        items.Single(item => item.Kind == UserMenuItemKind.LogOut).Disabled.Should().BeFalse();
    }

    [Fact]
    public void Create_WhenTransitionIsBusy_ShouldDisableTenantsAndLogOut()
    {
        // Act
        var items = UserMenuModel.Create(nameof(UserRole.Member), [CurrentTenant, OtherTenant], true);

        // Assert
        items.Where(item => item.Kind is UserMenuItemKind.SwitchTenant or UserMenuItemKind.LogOut).Should().OnlyContain(item => item.Disabled);
        items.Where(item => item.Kind is UserMenuItemKind.Profile or UserMenuItemKind.Preferences).Should().OnlyContain(item => !item.Disabled);
    }

    [Theory]
    [InlineData(UserMenuItemKind.Profile, true)]
    [InlineData(UserMenuItemKind.Preferences, true)]
    [InlineData(UserMenuItemKind.AccountSettings, true)]
    [InlineData(UserMenuItemKind.SwitchTenant, false)]
    [InlineData(UserMenuItemKind.LogOut, false)]
    public void ClosesOnSelect_WhenItemChosen_ShouldCloseOnlyForLinks(UserMenuItemKind kind, bool expected)
    {
        // Act
        var closes = UserMenuModel.ClosesOnSelect(new UserMenuItem(kind));

        // Assert
        closes.Should().Be(expected);
    }

    [Theory]
    [InlineData("ArrowDown", 0, 1)]
    [InlineData("ArrowDown", 1, 3)]
    [InlineData("ArrowUp", 0, 4)]
    [InlineData("Home", 3, 0)]
    [InlineData("End", 0, 4)]
    public void MoveForKey_WhenMenuHasADisabledCurrentTenant_ShouldSkipIt(string key, int activeIndex, int expected)
    {
        // Arrange
        var items = UserMenuModel.Create(nameof(UserRole.Member), [CurrentTenant, OtherTenant], false);

        // Act
        var index = MenuNavigation.MoveForKey(key, items, item => item.Disabled, activeIndex);

        // Assert
        index.Should().Be(expected);
    }

    [Fact]
    public void MoveForKey_WhenKeyIsNotAMovementKey_ShouldReturnNull()
    {
        // Arrange
        var items = UserMenuModel.Create(nameof(UserRole.Member), [CurrentTenant], false);

        // Act
        var index = MenuNavigation.MoveForKey("a", items, item => item.Disabled, 0);

        // Assert
        index.Should().BeNull();
    }
}
