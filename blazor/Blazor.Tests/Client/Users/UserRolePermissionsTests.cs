using Account.Features.Authentication.Queries;
using Blazor.Client.Users;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.Users;

// The role action is enabled only for an owner acting on another user, in the order the server checks the rule
public sealed class UserRolePermissionsTests
{
    private static readonly UserId CurrentUserId = new("usr_01KC0CURRENT0000000000000A");
    private static readonly UserId OtherUserId = new("usr_01KC0OTHER000000000000000A");

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

    private static BootstrapUser CreateUser(string? role)
    {
        return new BootstrapUser(CurrentUserId, new TenantId(1), role, "ann@example.com", "Ann", "Lee", null, null, "Acme", null, null, false, []);
    }
}
