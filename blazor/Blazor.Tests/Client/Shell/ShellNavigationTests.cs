using Blazor.Client.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.Shell;

public sealed class ShellNavigationTests
{
    private const string Origin = "https://app.dev.localhost:9000";

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    public void Create_WhenOwnerOrAdmin_ShouldIncludeAccountGroup(string role)
    {
        // Act
        var items = ShellNavigation.Create(role, $"{Origin}/blazor/app");

        // Assert
        items.Select(item => item.Target).Should().Equal(
            ShellNavigationTarget.Home, ShellNavigationTarget.Profile, ShellNavigationTarget.Preferences, ShellNavigationTarget.Sessions,
            ShellNavigationTarget.AccountSettings, ShellNavigationTarget.Users
        );
    }

    [Theory]
    [InlineData("Member")]
    [InlineData(null)]
    [InlineData("owner")]
    public void Create_WhenNotOwnerOrAdmin_ShouldOmitAccountGroup(string? role)
    {
        // Act
        var items = ShellNavigation.Create(role, $"{Origin}/blazor/app");

        // Assert
        items.Should().NotContain(item => item.Group == ShellNavigationGroup.Account);
        items.Should().HaveCount(4);
    }

    [Fact]
    public void Create_ShouldBuildRootAbsoluteHrefs()
    {
        // Act
        var items = ShellNavigation.Create("Owner", $"{Origin}/blazor/app");

        // Assert
        items.Select(item => item.Href).Should().Equal(
            "/blazor/app", "/blazor/user/profile", "/blazor/user/preferences", "/blazor/user/sessions", "/blazor/account/settings", "/blazor/account/users"
        );
    }

    [Theory]
    [InlineData("/blazor/account/users", ShellNavigationTarget.Users)]
    [InlineData("/blazor/account/users?search=ada#top", ShellNavigationTarget.Users)]
    [InlineData("/blazor/user/profile/", ShellNavigationTarget.Profile)]
    [InlineData("/blazor/app", ShellNavigationTarget.Home)]
    public void Create_WhenOnItemPath_ShouldMarkOnlyThatItemCurrent(string path, ShellNavigationTarget expected)
    {
        // Act
        var items = ShellNavigation.Create("Admin", $"{Origin}{path}");

        // Assert
        items.Where(item => item.IsCurrent).Select(item => item.Target).Should().Equal(expected);
    }

    [Theory]
    [InlineData("/blazor/app/details")]
    [InlineData("/blazor/account/users-archive")]
    [InlineData("/blazor/")]
    public void Create_WhenOnOtherPath_ShouldMarkNoItemCurrent(string path)
    {
        // Act
        var items = ShellNavigation.Create("Owner", $"{Origin}{path}");

        // Assert
        items.Should().NotContain(item => item.IsCurrent);
    }
}
