using Blazor.Client.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.Shell;

public sealed class ShellLocationWatcherTests
{
    private const string Users = $"{TestNavigationManager.BaseAddress}blazor/account/users";

    [Fact]
    public void LocationChanged_WhenThePathChanges_ShouldReportTheChange()
    {
        // Arrange
        var navigation = new TestNavigationManager();
        var changes = 0;
        using var watcher = new ShellLocationWatcher(navigation, () => changes++);

        // Act
        navigation.CompleteNavigation(Users);

        // Assert
        changes.Should().Be(1);
    }

    [Fact]
    public void LocationChanged_WhenSeveralPathsFollow_ShouldReportEachOfThem()
    {
        // Arrange
        var navigation = new TestNavigationManager();
        var changes = 0;
        using var watcher = new ShellLocationWatcher(navigation, () => changes++);

        // Act
        navigation.CompleteNavigation(Users);
        navigation.CompleteNavigation($"{TestNavigationManager.BaseAddress}blazor/app");
        navigation.CompleteNavigation($"{TestNavigationManager.BaseAddress}blazor/user/profile");

        // Assert
        changes.Should().Be(3);
    }

    [Theory]
    [InlineData($"{Users}?search=ada")]
    [InlineData($"{Users}#top")]
    [InlineData($"{Users}/")]
    public void LocationChanged_WhenOnlyTheQueryFragmentOrTrailingSlashChanges_ShouldReportNothing(string uri)
    {
        // Arrange
        var navigation = new TestNavigationManager();
        navigation.CompleteNavigation(Users);
        var changes = 0;
        using var watcher = new ShellLocationWatcher(navigation, () => changes++);

        // Act
        navigation.CompleteNavigation(uri);

        // Assert
        changes.Should().Be(0);
    }

    [Fact]
    public void LocationChanged_AfterDispose_ShouldReportNothing()
    {
        // Arrange
        var navigation = new TestNavigationManager();
        var changes = 0;
        var watcher = new ShellLocationWatcher(navigation, () => changes++);

        // Act
        watcher.Dispose();
        navigation.CompleteNavigation(Users);

        // Assert
        changes.Should().Be(0);
    }
}
