using Blazor.Client.Bootstrap;
using FluentAssertions;

namespace Blazor.Tests.Client;

// The leaving signal an unsaved-changes guard relies on to release the document before an authentication loss navigates
public sealed class AuthenticationNavigatorTests
{
    [Fact]
    public void LeaveForLogin_ShouldRaiseLeavingOnceBeforeTheFullDocumentNavigation()
    {
        // Arrange
        var navigation = new TestNavigationManager();
        var navigator = new AuthenticationNavigator(navigation);
        var navigationsWhenLeaving = new List<int>();
        navigator.Leaving += () => navigationsWhenLeaving.Add(navigation.Navigations.Count);

        // Act
        navigator.LeaveForLogin();
        navigator.LeaveForUnauthorized("Revoked");

        // Assert
        navigationsWhenLeaving.Should().Equal(0);
        navigator.IsLeaving.Should().BeTrue();
        navigation.Navigations.Should().ContainSingle().Which.ForceLoad.Should().BeTrue();
    }

    [Fact]
    public void IsLeaving_WhenNoNavigationStarted_ShouldBeFalse()
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());

        // Act
        var isLeaving = navigator.IsLeaving;

        // Assert
        isLeaving.Should().BeFalse();
    }
}
