using Account.Features.Authentication.Queries;
using Blazor.Client.Users;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.Users;

// The role and delete actions are enabled only for an owner acting on another user, in the order the server checks each rule,
// and the toolbar shows invite or bulk delete by the size of the selection
public sealed class UserRolePermissionsTests
{
    private static readonly UserId CurrentUserId = new("usr_01KC0CURRENT0000000000000A");
    private static readonly UserId OtherUserId = new("usr_01KC0OTHER000000000000000A");
    private static readonly UserId SecondOtherUserId = new("usr_01KC0SECOND00000000000000A");

    [Theory]
    [InlineData("Owner", RoleChangePermission.Allowed)]
    [InlineData("Admin", RoleChangePermission.NotOwner)]
    [InlineData("Member", RoleChangePermission.NotOwner)]
    [InlineData(null, RoleChangePermission.NotOwner)]
    public void ForTarget_WhenTargetIsAnotherUser_ShouldAllowOnlyAnOwner(string? role, RoleChangePermission expected)
    {
        // Arrange
        var currentUser = CreateUser(role);

        // Act
        var permission = UserRolePermissions.ForTarget(currentUser, OtherUserId);

        // Assert
        permission.Should().Be(expected);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    [InlineData("Member")]
    public void ForTarget_WhenTargetIsTheCurrentUser_ShouldReportOwnRoleBeforeTheOwnerRule(string role)
    {
        // Arrange
        var currentUser = CreateUser(role);

        // Act
        var permission = UserRolePermissions.ForTarget(currentUser, CurrentUserId);

        // Assert
        permission.Should().Be(RoleChangePermission.OwnRole);
    }

    [Fact]
    public void ForTarget_WhenNoUserIsSignedIn_ShouldNotAllow()
    {
        // Act
        var permission = UserRolePermissions.ForTarget(null, OtherUserId);

        // Assert
        permission.Should().Be(RoleChangePermission.NotOwner);
    }

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Admin", false)]
    [InlineData("Member", false)]
    [InlineData(null, false)]
    public void IsOwner_ShouldBeTrueOnlyForTheOwnerRole(string? role, bool expected)
    {
        // Act
        var isOwner = UserRolePermissions.IsOwner(CreateUser(role));

        // Assert
        isOwner.Should().Be(expected);
    }

    [Theory]
    [InlineData(RoleChangePermission.OwnRole)]
    [InlineData(RoleChangePermission.NotOwner)]
    [InlineData(RoleChangePermission.Allowed)]
    public void DisabledReason_ShouldExplainADisabledRoleAction(RoleChangePermission permission)
    {
        // Arrange
        var expected = permission switch
        {
            RoleChangePermission.OwnRole => UsersStrings.CannotChangeOwnRole,
            RoleChangePermission.NotOwner => UsersStrings.OnlyOwnersCanChangeRoles,
            _ => null
        };

        // Act
        var reason = UserRolePermissions.DisabledReason(permission);

        // Assert
        reason.Should().Be(expected);
    }

    [Theory]
    [InlineData("Owner", 0, true)]
    [InlineData("Owner", 1, true)]
    [InlineData("Owner", 2, false)]
    [InlineData("Admin", 0, false)]
    [InlineData("Member", 1, false)]
    [InlineData(null, 0, false)]
    public void CanInvite_ShouldAllowOnlyAnOwnerWithAtMostOneRowSelected(string? role, int selectedCount, bool expected)
    {
        // Act
        var canInvite = UserRolePermissions.CanInvite(CreateUser(role), selectedCount);

        // Assert
        canInvite.Should().Be(expected);
    }

    [Theory]
    [InlineData("Owner", UserDeletePermission.Allowed)]
    [InlineData("Admin", UserDeletePermission.NotOwner)]
    [InlineData("Member", UserDeletePermission.NotOwner)]
    [InlineData(null, UserDeletePermission.NotOwner)]
    public void ForDelete_WhenTargetIsAnotherUser_ShouldAllowOnlyAnOwner(string? role, UserDeletePermission expected)
    {
        // Act
        var permission = UserRolePermissions.ForDelete(CreateUser(role), OtherUserId);

        // Assert
        permission.Should().Be(expected);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    [InlineData("Member")]
    public void ForDelete_WhenTargetIsTheCurrentUser_ShouldReportSelfBeforeTheOwnerRule(string role)
    {
        // Act
        var permission = UserRolePermissions.ForDelete(CreateUser(role), CurrentUserId);

        // Assert
        permission.Should().Be(UserDeletePermission.Self);
    }

    [Fact]
    public void ForDelete_WhenNoUserIsSignedIn_ShouldNotAllow()
    {
        // Act
        var permission = UserRolePermissions.ForDelete(null, OtherUserId);

        // Assert
        permission.Should().Be(UserDeletePermission.NotOwner);
    }

    [Theory]
    [InlineData("Owner", 1, false)]
    [InlineData("Owner", 2, true)]
    [InlineData("Owner", 25, true)]
    [InlineData("Admin", 2, false)]
    [InlineData("Member", 3, false)]
    [InlineData(null, 2, false)]
    public void ShowsBulkDelete_ShouldShowOnlyForAnOwnerWithTwoOrMoreRowsSelected(string? role, int selectedCount, bool expected)
    {
        // Act
        var shows = UserRolePermissions.ShowsBulkDelete(CreateUser(role), selectedCount);

        // Assert
        shows.Should().Be(expected);
    }

    [Fact]
    public void ForBulkDelete_WhenOwnerSelectsOtherUsers_ShouldAllow()
    {
        // Arrange
        UserId[] targetUserIds = [OtherUserId, SecondOtherUserId];

        // Act
        var permission = UserRolePermissions.ForBulkDelete(CreateUser("Owner"), targetUserIds);

        // Assert
        permission.Should().Be(UserDeletePermission.Allowed);
    }

    [Fact]
    public void ForBulkDelete_WhenSelectionIncludesTheCurrentUser_ShouldReportSelf()
    {
        // Arrange
        UserId[] targetUserIds = [OtherUserId, CurrentUserId];

        // Act
        var permission = UserRolePermissions.ForBulkDelete(CreateUser("Owner"), targetUserIds);

        // Assert
        permission.Should().Be(UserDeletePermission.Self);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Member")]
    [InlineData(null)]
    public void ForBulkDelete_WhenNotOwner_ShouldReportTheOwnerRuleBeforeSelf(string? role)
    {
        // Arrange
        UserId[] targetUserIds = [OtherUserId, CurrentUserId];

        // Act
        var permission = UserRolePermissions.ForBulkDelete(CreateUser(role), targetUserIds);

        // Assert
        permission.Should().Be(UserDeletePermission.NotOwner);
    }

    [Theory]
    [InlineData(UserDeletePermission.Self)]
    [InlineData(UserDeletePermission.NotOwner)]
    [InlineData(UserDeletePermission.Allowed)]
    public void DeleteDisabledReason_ShouldExplainOnlyTheOwnAccount(UserDeletePermission permission)
    {
        // Arrange
        var expected = permission == UserDeletePermission.Self ? UsersStrings.CannotDeleteYourself : null;

        // Act
        var reason = UserRolePermissions.DeleteDisabledReason(permission);

        // Assert
        reason.Should().Be(expected);
    }

    private static BootstrapUser CreateUser(string? role)
    {
        return new BootstrapUser(CurrentUserId, new TenantId(1), role, "ann@example.com", "Ann", "Lee", null, null, "Acme", null, null, false, []);
    }
}
