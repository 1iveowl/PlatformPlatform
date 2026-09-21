using Blazor.Client.Bootstrap;
using Blazor.Client.Preferences;
using FluentAssertions;
using Microsoft.JSInterop;

namespace Blazor.Tests.Client.Preferences;

/// <summary>
///     The departure table of the notifications section: which ways out of the authenticated surface take this browser's
///     subscription with them and which leave it alone. Every departure ends the identity of the runtime, so all of them
///     forget; a reload of the same user is a new document that raises nothing, which is the row that must not forget.
///     The table is driven through the real AuthenticationNavigator rather than described, so a departure added later
///     without a row here shows up as an unexplained call.
/// </summary>
public sealed class PushNotificationDepartureTests
{
    [Theory]
    // The user logged out here: SessionTransition ends the session and leaves for the login page
    [InlineData("logout")]
    // The account API ended the session: the unauthorized handler leaves for the page its reason names
    [InlineData("session-ended")]
    // An anonymous bootstrap on an authenticated surface: the surface leaves for the login page
    [InlineData("unauthenticated-bootstrap")]
    // A tenant switch: the session is ended before the authenticated home is loaded, and the new tenant is another user
    [InlineData("tenant-switch")]
    // Another tab logged out, logged in as someone else or switched tenant: AuthSyncCoordinator ends this identity
    [InlineData("another-tab")]
    public async Task Leaving_ForADepartureThatEndsTheIdentity_ShouldForgetThisBrowsersSubscription(string departure)
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript();
        var departureListener = new PushNotificationDeparture(javaScript, navigator);
        await departureListener.AttachAsync();

        // Act
        Depart(navigator, departure);

        // Assert
        departureListener.Forgotten.Should().BeTrue();
        javaScript.Calls.Should().Equal("import", "forgetDevice");
    }

    [Fact]
    public async Task Leaving_WhenTheDialogReloadsAfterTheIdentityAlreadyEnded_ShouldForgetOnceAndLoadThePageOnce()
    {
        // Arrange
        var navigation = new TestNavigationManager();
        var navigator = new AuthenticationNavigator(navigation);
        var javaScript = new RecordingJavaScript();
        var departureListener = new PushNotificationDeparture(javaScript, navigator);
        await departureListener.AttachAsync();

        // Act: what the invalidation dialog does, which is the only caller of LeaveForReload
        navigator.EndSession();
        navigator.LeaveForReload();

        // Assert
        javaScript.Calls.Should().Equal("import", "forgetDevice");
        navigation.Navigations.Should().ContainSingle();
    }

    [Fact]
    public async Task Leaving_WhenNothingDeparts_ShouldKeepThisBrowsersSubscription()
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript();
        var departureListener = new PushNotificationDeparture(javaScript, navigator);

        // Act: a reload of the same user is a new document; no departure of this runtime happens and Leaving is never raised
        await departureListener.AttachAsync();

        // Assert
        departureListener.Forgotten.Should().BeFalse();
        javaScript.Calls.Should().Equal("import");
    }

    [Fact]
    public async Task Leaving_WhenTheModuleCannotBeImported_ShouldLeaveWithoutForgetting()
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript { FailImport = true };
        var departureListener = new PushNotificationDeparture(javaScript, navigator);
        await departureListener.AttachAsync();

        // Act
        navigator.LeaveForLoggedOut();

        // Assert
        departureListener.Forgotten.Should().BeFalse();
        javaScript.Calls.Should().Equal("import");
    }

    [Fact]
    public async Task DisposeAsync_ShouldStopListeningSoALaterDepartureForgetsNothing()
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript();
        var departureListener = new PushNotificationDeparture(javaScript, navigator);
        await departureListener.AttachAsync();

        // Act
        await departureListener.DisposeAsync();
        navigator.LeaveForLoggedOut();

        // Assert
        departureListener.Forgotten.Should().BeFalse();
        javaScript.Calls.Should().Equal("import", "dispose");
    }

    private static void Depart(AuthenticationNavigator navigator, string departure)
    {
        switch (departure)
        {
            case "logout":
                navigator.EndSession();
                navigator.LeaveForLoggedOut();
                break;
            case "session-ended":
                navigator.LeaveForUnauthorized("Revoked");
                break;
            case "unauthenticated-bootstrap":
                navigator.LeaveForLogin();
                break;
            case "tenant-switch":
                navigator.EndSession();
                navigator.LeaveForAuthenticatedHome();
                break;
            default:
                navigator.EndSession();
                break;
        }
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
