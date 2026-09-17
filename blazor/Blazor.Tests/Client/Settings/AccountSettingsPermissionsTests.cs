using Account.Features.Authentication.Queries;
using Blazor.Client.Settings;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.Settings;

// Every authenticated user may open the account settings page; only the owner edits the name and the logo, and a
// non-owner is told why the name is read-only
public sealed class AccountSettingsPermissionsTests
{
    private static readonly UserId CurrentUserId = new("usr_01KC0CURRENT0000000000000A");

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Admin", false)]
    [InlineData("Member", false)]
    [InlineData(null, false)]
    public void CanEditAccount_ShouldAllowOnlyAnOwner(string? role, bool expected)
    {
        // Act and Assert
        AccountSettingsPermissions.CanEditAccount(CreateUser(role)).Should().Be(expected);
    }

    [Fact]
    public void CanEditAccount_WhenThereIsNoUser_ShouldRefuse()
    {
        // Act and Assert
        AccountSettingsPermissions.CanEditAccount(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Member")]
    [InlineData(null)]
    public void ReadOnlyDescription_WhenTheUserIsNotAnOwner_ShouldExplainWhyTheNameIsReadOnly(string? role)
    {
        // Act and Assert
        AccountSettingsPermissions.ReadOnlyDescription(CreateUser(role)).Should().Be(AccountStrings.OnlyOwnersCanModifyAccountName);
    }

    [Fact]
    public void ReadOnlyDescription_WhenTheUserIsTheOwner_ShouldBeNull()
    {
        // Act and Assert
        AccountSettingsPermissions.ReadOnlyDescription(CreateUser("Owner")).Should().BeNull();
    }

    private static BootstrapUser CreateUser(string? role)
    {
        return new BootstrapUser(CurrentUserId, new TenantId(1), role, "ann@example.com", "Ann", "Lee", null, null, "Acme", null, null, false, []);
    }
}
