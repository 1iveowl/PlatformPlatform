using Account.Features.Authentication.Queries;
using Blazor.Client.Users;
using FluentAssertions;
using SharedKernel.Domain;

namespace Blazor.Tests.Client.Users;

// The recycle bin follows the account API's rules: owners and admins view, restore and purge one user; only owners purge
// several at once or empty the bin
public sealed class RecycleBinPermissionsTests
{
    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Admin", true)]
    [InlineData("Member", false)]
    [InlineData(null, false)]
    public void CanView_WhenRoleGiven_ShouldAllowOwnersAndAdmins(string? role, bool expected)
    {
        // Act
        var canView = RecycleBinPermissions.CanView(CreateUser(role));

        // Assert
        canView.Should().Be(expected);
    }

    [Fact]
    public void CanView_WhenNoUser_ShouldRefuse()
    {
        // Act
        var canView = RecycleBinPermissions.CanView(null);

        // Assert
        canView.Should().BeFalse();
    }

    [Theory]
    [InlineData("Owner", 0, false)]
    [InlineData("Owner", 1, true)]
    [InlineData("Owner", 2, true)]
    [InlineData("Admin", 1, true)]
    [InlineData("Admin", 2, false)]
    [InlineData("Member", 1, false)]
    public void CanPermanentlyDelete_WhenSelectionGiven_ShouldAllowBulkPurgeOnlyForOwners(string role, int selectedCount, bool expected)
    {
        // Act
        var canDelete = RecycleBinPermissions.CanPermanentlyDelete(CreateUser(role), selectedCount);

        // Assert
        canDelete.Should().Be(expected);
    }

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Admin", false)]
    [InlineData("Member", false)]
    public void CanEmpty_WhenRoleGiven_ShouldAllowOnlyOwners(string role, bool expected)
    {
        // Act
        var canEmpty = RecycleBinPermissions.CanEmpty(CreateUser(role));

        // Assert
        canEmpty.Should().Be(expected);
    }

    [Theory]
    [InlineData("Owner", 0, 3, true, false, false)]
    [InlineData("Owner", 0, 0, false, false, false)]
    [InlineData("Owner", 1, 3, false, true, true)]
    [InlineData("Owner", 2, 3, false, true, true)]
    [InlineData("Admin", 0, 3, false, false, false)]
    [InlineData("Admin", 1, 3, false, true, true)]
    [InlineData("Admin", 2, 3, false, true, false)]
    [InlineData("Member", 1, 3, false, false, false)]
    public void Actions_WhenSelectionAndBinGiven_ShouldShowTheToolbarActions(string role, int selectedCount, int totalCount, bool empty, bool restore, bool delete)
    {
        // Act
        var actions = RecycleBinPermissions.Actions(CreateUser(role), selectedCount, totalCount);

        // Assert
        actions.Should().Be(new RecycleBinActions(empty, restore, delete));
    }

    private static BootstrapUser CreateUser(string? role)
    {
        return new BootstrapUser(new UserId("usr_01KC0CURRENT0000000000000A"), new TenantId(1), role, "ann@example.com", "Ann", "Lee", null, null, "Acme", null, null, false, []);
    }
}
