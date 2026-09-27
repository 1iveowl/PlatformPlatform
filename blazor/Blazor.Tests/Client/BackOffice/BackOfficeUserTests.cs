using Account.Features.BackOffice.Queries;
using Blazor.Client.BackOffice.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.BackOffice;

public sealed class BackOfficeUserTests
{
    [Theory]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("ada  byron lovelace", "AB")]
    [InlineData("Admin", "A")]
    [InlineData("  ", "PP")]
    public void GetInitials_ShouldTakeTheFirstLetterOfTheFirstTwoWords(string displayName, string expected)
    {
        // Arrange
        var me = new MeResponse(displayName, "ada@example.com", false, []);

        // Act
        var initials = BackOfficeUser.GetInitials(me);

        // Assert
        initials.Should().Be(expected);
    }

    [Fact]
    public void GetInitials_WhenTheIdentityIsNotLoaded_ShouldFallBack()
    {
        // Act
        var initials = BackOfficeUser.GetInitials(null);

        // Assert
        initials.Should().Be("PP");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void CanRunAdminActions_ShouldFollowTheAccountApisAdminVerdict(bool isAdmin, bool expected)
    {
        // Act
        var canRun = BackOfficeUser.CanRunAdminActions(new MeResponse("Admin", "admin@dev.localhost", isAdmin, []));

        // Assert
        canRun.Should().Be(expected);
    }

    [Fact]
    public void CanRunAdminActions_WhenTheIdentityIsNotLoaded_ShouldOfferNothing()
    {
        // Act
        var canRun = BackOfficeUser.CanRunAdminActions(null);

        // Assert
        canRun.Should().BeFalse();
    }
}
