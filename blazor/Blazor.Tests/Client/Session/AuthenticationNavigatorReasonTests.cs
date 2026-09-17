using Blazor.Client.Bootstrap;
using FluentAssertions;

namespace Blazor.Tests.Client.Session;

// Where a 401 sends the user, by the account API's x-unauthorized-reason, the React edition's AuthenticationMiddleware:
// a reused refresh token goes to login without an error page, because replay detection is mostly driven by false
// positives such as two tabs refreshing at once; a session that ended for good goes to the error page that names why.
public sealed class AuthenticationNavigatorReasonTests
{
    [Theory]
    [InlineData("Revoked", "session_revoked")]
    [InlineData("SessionNotFound", "session_not_found")]
    [InlineData("TenantDeleted", "tenant_deleted")]
    [InlineData("ReplayAttackDetected", null)]
    [InlineData(null, null)]
    [InlineData("SomethingNew", null)]
    public void GetErrorCode_ShouldNameAnErrorPageOnlyForASessionThatEndedForGood(string? unauthorizedReason, string? errorCode)
    {
        // Act & Assert
        AuthenticationNavigator.GetErrorCode(unauthorizedReason).Should().Be(errorCode);
    }

    [Fact]
    public void LeaveForUnauthorized_WhenReplayAttackDetected_ShouldLeaveForLoginWithTheReturnPath()
    {
        // Arrange
        var navigation = new TestNavigationManager();
        var navigator = new AuthenticationNavigator(navigation);

        // Act
        navigator.LeaveForUnauthorized("ReplayAttackDetected");

        // Assert
        navigation.Navigations.Should().Equal(
            new RecordedNavigation($"/blazor/login?returnPath={Uri.EscapeDataString("/blazor/development/form-errors/interactive?tab=forms")}", true)
        );
    }

    [Fact]
    public void LeaveForUnauthorized_WhenRevoked_ShouldLeaveForTheSessionEndedPage()
    {
        // Arrange
        var navigation = new TestNavigationManager();
        var navigator = new AuthenticationNavigator(navigation);

        // Act
        navigator.LeaveForUnauthorized("Revoked");

        // Assert
        navigation.Navigations.Should().Equal(new RecordedNavigation("/blazor/error?error=session_revoked", true));
    }

    [Fact]
    public void LeaveForReload_WhenTheSessionWasEndedFirst_ShouldLoadTheCurrentPageOnce()
    {
        // Arrange
        var navigation = new TestNavigationManager();
        var navigator = new AuthenticationNavigator(navigation);
        navigator.EndSession();

        // Act
        navigator.LeaveForReload();
        navigator.LeaveForLogin();

        // Assert
        navigation.Navigations.Should().Equal(new RecordedNavigation("/blazor/development/form-errors/interactive?tab=forms", true));
    }
}
