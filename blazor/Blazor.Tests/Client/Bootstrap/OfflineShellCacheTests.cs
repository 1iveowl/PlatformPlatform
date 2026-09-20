using Blazor.Client.Bootstrap;
using FluentAssertions;
using Microsoft.JSInterop;

namespace Blazor.Tests.Client.Bootstrap;

// The second guard on the offline shell: whichever way the session of a runtime ends, the stored shell document is dropped
// before the full document navigation starts. The worker's own rules are what keep identity out of the cache in the first
// place; these cases are about the message being sent once, on every departure, and about a runtime with no worker.
public sealed class OfflineShellCacheTests
{
    [Fact]
    public async Task Leaving_WhenTheModuleIsAttached_ShouldClearTheShellOnceBeforeTheNavigation()
    {
        // Arrange
        var navigation = new TestNavigationManager();
        var navigator = new AuthenticationNavigator(navigation);
        var javaScript = new RecordingJavaScript();
        var cache = new OfflineShellCache(javaScript, navigator);
        await cache.AttachAsync();

        // Act
        navigator.LeaveForLoggedOut();
        navigator.LeaveForLogin();

        // Assert
        javaScript.Calls.Should().Equal("import", "clearShell");
        cache.Cleared.Should().BeTrue();
        navigation.Navigations.Should().ContainSingle();
    }

    [Theory]
    // A logout, a tenant switch and a session the account API ended all raise the one signal
    [InlineData("logout")]
    [InlineData("switch")]
    [InlineData("unauthorized")]
    public async Task Leaving_ShouldClearTheShellForEveryWayASessionEnds(string departure)
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript();
        var cache = new OfflineShellCache(javaScript, navigator);
        await cache.AttachAsync();

        // Act
        switch (departure)
        {
            case "logout": navigator.LeaveForLoggedOut(); break;
            case "switch": navigator.EndSession(); break;
            default: navigator.LeaveForUnauthorized("Revoked"); break;
        }

        // Assert
        cache.Cleared.Should().BeTrue();
        javaScript.Calls.Should().Contain("clearShell");
    }

    [Fact]
    public async Task Leaving_WhenTheModuleCannotBeImported_ShouldLeaveWithoutClearing()
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript { FailImport = true };
        var cache = new OfflineShellCache(javaScript, navigator);
        await cache.AttachAsync();

        // Act
        navigator.LeaveForLoggedOut();

        // Assert
        cache.Cleared.Should().BeFalse();
        javaScript.Calls.Should().Equal("import");
    }

    [Fact]
    public async Task DisposeAsync_ShouldStopListeningSoALaterDepartureClearsNothing()
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript();
        var cache = new OfflineShellCache(javaScript, navigator);
        await cache.AttachAsync();

        // Act
        await cache.DisposeAsync();
        navigator.LeaveForLoggedOut();

        // Assert
        cache.Cleared.Should().BeFalse();
        javaScript.Calls.Should().Equal("import", "dispose");
    }

    private sealed class RecordingJavaScript : IJSRuntime, IJSInProcessObjectReference
    {
        public List<string> Calls { get; } = [];

        public bool FailImport { get; init; }

        public void Dispose()
        {
            Calls.Add("dispose");
        }

        public ValueTask DisposeAsync()
        {
            Calls.Add("dispose");
            return ValueTask.CompletedTask;
        }

        public TValue Invoke<TValue>(string identifier, params object?[]? args)
        {
            Calls.Add(identifier);
            return default!;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            try
            {
                return ValueTask.FromResult(Record<TValue>(identifier));
            }
            catch (JSException exception)
            {
                return ValueTask.FromException<TValue>(exception);
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            try
            {
                return ValueTask.FromResult(Record<TValue>(identifier));
            }
            catch (JSException exception)
            {
                return ValueTask.FromException<TValue>(exception);
            }
        }

        private TValue Record<TValue>(string identifier)
        {
            Calls.Add(identifier);
            if (FailImport) throw new JSException("No module.");

            return (TValue)(object)this;
        }
    }
}
