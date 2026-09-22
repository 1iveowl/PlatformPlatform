using Account.Client;
using Account.Features.PushNotifications.Domain;
using Blazor.Client.Preferences;
using FluentAssertions;
using Microsoft.JSInterop;

namespace Blazor.Tests.Client.Preferences;

/// <summary>
///     What the notifications section removes on a visit after the user revoked notifications in the browser settings. A
///     browser leaves a revocation in one of two forms: it no longer holds the subscription, or it still holds it while
///     the
///     permission is denied, which Safari does. Both end with the browser unsubscribed, the stored identifier cleared and
///     the account's row deleted; a subscription under a granted or unanswered permission is left alone.
/// </summary>
public sealed class PushSubscriptionRevocationTests
{
    private const string StoredSubscriptionId = "psub_01KC0000000000000000000001";

    [Fact]
    public async Task ReconcileAsync_WhenThePermissionIsDeniedWhileTheBrowserStillHoldsTheSubscription_ShouldUnsubscribeAndDeleteTheRow()
    {
        // Arrange
        var javaScript = new BrowserJavaScript { HoldsSubscription = true, Permission = "denied", StoredId = StoredSubscriptionId };
        var deleted = new List<PushSubscriptionId>();

        // Act
        await PushSubscriptionRevocation.ReconcileAsync(new PushNotificationBrowser(javaScript), Delete(deleted, ApiCallResult.Success()));

        // Assert
        javaScript.HoldsSubscription.Should().BeFalse();
        deleted.Should().Equal(new PushSubscriptionId(StoredSubscriptionId));
        javaScript.StoredId.Should().BeNull();
        javaScript.Calls.Should().ContainInOrder("unsubscribe", "readStoredSubscriptionId", "storeSubscriptionId");
    }

    [Fact]
    public async Task ReconcileAsync_WhenThePermissionIsDeniedAndThisDeviceStoredNoIdentifier_ShouldUnsubscribeAndDeleteNothing()
    {
        // Arrange
        var javaScript = new BrowserJavaScript { HoldsSubscription = true, Permission = "denied" };
        var deleted = new List<PushSubscriptionId>();

        // Act
        await PushSubscriptionRevocation.ReconcileAsync(new PushNotificationBrowser(javaScript), Delete(deleted, ApiCallResult.Success()));

        // Assert
        javaScript.HoldsSubscription.Should().BeFalse();
        deleted.Should().BeEmpty();
    }

    [Theory]
    [InlineData("granted")]
    [InlineData("default")]
    public async Task ReconcileAsync_WhenThePermissionIsNotDeniedAndTheBrowserHoldsTheSubscription_ShouldLeaveEverything(string permission)
    {
        // Arrange
        var javaScript = new BrowserJavaScript { HoldsSubscription = true, Permission = permission, StoredId = StoredSubscriptionId };
        var deleted = new List<PushSubscriptionId>();

        // Act
        await PushSubscriptionRevocation.ReconcileAsync(new PushNotificationBrowser(javaScript), Delete(deleted, ApiCallResult.Success()));

        // Assert
        javaScript.HoldsSubscription.Should().BeTrue();
        deleted.Should().BeEmpty();
        javaScript.StoredId.Should().Be(StoredSubscriptionId);
        javaScript.Calls.Should().NotContain("unsubscribe");
    }

    [Fact]
    public async Task ReconcileAsync_WhenTheBrowserNoLongerHoldsTheSubscription_ShouldDeleteTheRowThisDeviceStored()
    {
        // Arrange
        var javaScript = new BrowserJavaScript { HoldsSubscription = false, Permission = "granted", StoredId = StoredSubscriptionId };
        var deleted = new List<PushSubscriptionId>();

        // Act
        await PushSubscriptionRevocation.ReconcileAsync(new PushNotificationBrowser(javaScript), Delete(deleted, ApiCallResult.Success()));

        // Assert
        deleted.Should().Equal(new PushSubscriptionId(StoredSubscriptionId));
        javaScript.StoredId.Should().BeNull();
        javaScript.Calls.Should().NotContain("unsubscribe");
    }

    [Fact]
    public async Task ReconcileAsync_WhenTheSurfaceIsLeftDuringTheDelete_ShouldKeepTheIdentifierForTheNextVisit()
    {
        // Arrange
        var javaScript = new BrowserJavaScript { HoldsSubscription = true, Permission = "denied", StoredId = StoredSubscriptionId };
        var deleted = new List<PushSubscriptionId>();

        // Act
        await PushSubscriptionRevocation.ReconcileAsync(new PushNotificationBrowser(javaScript), Delete(deleted, null));

        // Assert: the browser let go of it, so the next visit finds no subscription and deletes the row the identifier names
        javaScript.HoldsSubscription.Should().BeFalse();
        deleted.Should().ContainSingle();
        javaScript.StoredId.Should().Be(StoredSubscriptionId);
    }

    // A null result stands for a departure from the authenticated surface (SessionState.UnlessLeavingAsync)
    private static Func<PushSubscriptionId, Task<ApiCallResult?>> Delete(List<PushSubscriptionId> deleted, ApiCallResult? result)
    {
        return pushSubscriptionId =>
        {
            deleted.Add(pushSubscriptionId);
            return Task.FromResult(result);
        };
    }

    // The push-notifications module as the browser answers it, with the subscription, the permission and the stored
    // identifier as state the calls read and change
    private sealed class BrowserJavaScript : IJSRuntime, IJSObjectReference
    {
        public List<string> Calls { get; } = [];

        public bool HoldsSubscription { get; set; }

        public string Permission { get; init; } = "default";

        public string? StoredId { get; set; }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            try
            {
                return ValueTask.FromResult(Answer<TValue>(identifier, args));
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
                return ValueTask.FromResult(Answer<TValue>(identifier, args));
            }
            catch (JSException exception)
            {
                return ValueTask.FromException<TValue>(exception);
            }
        }

        private TValue Answer<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == "storeSubscriptionId") StoredId = args?[0] as string;
            if (identifier == "unsubscribe") HoldsSubscription = false;

            object? answer = identifier switch
            {
                "import" => this,
                "readSubscription" => HoldsSubscription ? new BrowserPushSubscription("https://push.example.invalid/1", "key", "secret") : null,
                "readPermission" => Permission,
                "readStoredSubscriptionId" => StoredId,
                "storeSubscriptionId" or "unsubscribe" => true,
                _ => throw new JSException($"Unexpected call {identifier}.")
            };

            return (TValue)answer!;
        }
    }
}
