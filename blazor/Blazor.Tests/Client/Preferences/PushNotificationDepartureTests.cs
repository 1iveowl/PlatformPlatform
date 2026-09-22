using System.Net;
using Account.Client;
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
///     A logout and a tenant switch first delete the account's row for this device, while the session that owns it is
///     still valid; the browser side is forgotten afterwards by Leaving, whatever the delete answered.
/// </summary>
public sealed class PushNotificationDepartureTests
{
    private const string SavedSubscriptionId = "psub_01KC0000000000000000000001";
    private const string RowPath = $"/api/account/users/me/push-subscriptions/{SavedSubscriptionId}";

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
        var departureListener = CreateDeparture(javaScript, navigator);
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
        var departureListener = CreateDeparture(javaScript, navigator);
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
        var departureListener = CreateDeparture(javaScript, navigator);

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
        var departureListener = CreateDeparture(javaScript, navigator);
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
        var departureListener = CreateDeparture(javaScript, navigator);
        await departureListener.AttachAsync();

        // Act
        await departureListener.DisposeAsync();
        navigator.LeaveForLoggedOut();

        // Assert
        departureListener.Forgotten.Should().BeFalse();
        javaScript.Calls.Should().Equal("import", "dispose");
    }

    [Fact]
    public async Task DeleteRowAsync_WhenThisDeviceStoredASubscription_ShouldDeleteItsRowBeforeTheBrowserSideIsForgotten()
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript { StoredSubscriptionId = SavedSubscriptionId };
        var departureListener = CreateDeparture(javaScript, navigator, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        await departureListener.AttachAsync();

        // Act: what SessionTransition does for a logout, which deletes the row before it sends the logout request
        await departureListener.DeleteRowAsync();
        Depart(navigator, "logout");

        // Assert
        departureListener.Forgotten.Should().BeTrue();
        javaScript.Calls.Should().Equal("import", "readStoredSubscriptionId", $"DELETE {RowPath}", "forgetDevice");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task DeleteRowAsync_WhenTheDeleteIsRefused_ShouldStillForgetTheBrowserSide(HttpStatusCode statusCode)
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript { StoredSubscriptionId = SavedSubscriptionId };
        var departureListener = CreateDeparture(javaScript, navigator, _ => Task.FromResult(new HttpResponseMessage(statusCode)));
        await departureListener.AttachAsync();

        // Act
        await departureListener.DeleteRowAsync();
        Depart(navigator, "logout");

        // Assert
        departureListener.Forgotten.Should().BeTrue();
        javaScript.Calls.Should().Equal("import", "readStoredSubscriptionId", $"DELETE {RowPath}", "forgetDevice");
    }

    [Fact]
    public async Task DeleteRowAsync_WhenTheDeleteCannotReachTheAccountApi_ShouldStillForgetTheBrowserSide()
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript { StoredSubscriptionId = SavedSubscriptionId };
        var departureListener = CreateDeparture(javaScript, navigator, _ => throw new HttpRequestException("The network is gone."));
        await departureListener.AttachAsync();

        // Act
        await departureListener.DeleteRowAsync();
        Depart(navigator, "logout");

        // Assert
        departureListener.Forgotten.Should().BeTrue();
        javaScript.Calls.Should().Equal("import", "readStoredSubscriptionId", $"DELETE {RowPath}", "forgetDevice");
    }

    [Fact]
    public async Task DeleteRowAsync_WhenTheDeleteDoesNotAnswer_ShouldGiveUpWithinTheTimeoutAndStillForgetTheBrowserSide()
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript { StoredSubscriptionId = SavedSubscriptionId };
        var departureListener = CreateDeparture(javaScript, navigator, async cancellationToken =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("A cancelled delay never completes.");
            }
        );
        await departureListener.AttachAsync();
        var startedAt = TimeProvider.System.GetTimestamp();

        // Act
        await departureListener.DeleteRowAsync();
        Depart(navigator, "logout");

        // Assert
        TimeProvider.System.GetElapsedTime(startedAt).Should().BeLessThan(PushNotificationDeparture.RowDeletionTimeout * 5);
        departureListener.Forgotten.Should().BeTrue();
        javaScript.Calls.Should().Equal("import", "readStoredSubscriptionId", $"DELETE {RowPath}", "forgetDevice");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-subscription-id")]
    public async Task DeleteRowAsync_WhenThisDeviceStoredNoSubscription_ShouldSendNothing(string? storedSubscriptionId)
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript { StoredSubscriptionId = storedSubscriptionId };
        var departureListener = CreateDeparture(javaScript, navigator);
        await departureListener.AttachAsync();

        // Act
        await departureListener.DeleteRowAsync();

        // Assert
        departureListener.Forgotten.Should().BeFalse();
        javaScript.Calls.Should().Equal("import", "readStoredSubscriptionId");
    }

    [Fact]
    public async Task DeleteRowAsync_WhenTheModuleCannotBeImported_ShouldSendNothing()
    {
        // Arrange
        var navigator = new AuthenticationNavigator(new TestNavigationManager());
        var javaScript = new RecordingJavaScript { FailImport = true, StoredSubscriptionId = SavedSubscriptionId };
        var departureListener = CreateDeparture(javaScript, navigator);
        await departureListener.AttachAsync();

        // Act
        await departureListener.DeleteRowAsync();

        // Assert
        javaScript.Calls.Should().Equal("import");
    }

    private static PushNotificationDeparture CreateDeparture(
        RecordingJavaScript javaScript,
        AuthenticationNavigator navigator,
        Func<CancellationToken, Task<HttpResponseMessage>>? respond = null
    )
    {
        var network = new RecordingNetwork(javaScript.Calls, respond ?? (_ => throw new InvalidOperationException("No request was expected.")));
        var pushSubscriptionsClient = new PushSubscriptionsClient(new HttpClient(network) { BaseAddress = new Uri("https://app.dev.localhost:9000/") });
        return new PushNotificationDeparture(javaScript, navigator, pushSubscriptionsClient);
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

        public string? StoredSubscriptionId { get; init; }

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
            return identifier == "readStoredSubscriptionId" ? (TValue)(object?)StoredSubscriptionId! : default!;
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

    // Records each request in the same list as the module calls, so a test reads the order across both
    private sealed class RecordingNetwork(List<string> calls, Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return respond(cancellationToken);
        }
    }
}
