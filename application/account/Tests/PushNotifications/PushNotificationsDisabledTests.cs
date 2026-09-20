using System.Net;
using System.Net.Http.Json;
using Account.Database;
using Account.Features.PushNotifications.Domain;
using Account.Features.PushNotifications.Requests;
using SharedKernel.Tests;
using Xunit;

namespace Account.Tests.PushNotifications;

// A deployment without a VAPID key pair has the push-notifications-enabled flag off, and every endpoint of the feature
// answers as if it were not there rather than telling a caller it exists but is closed.
public sealed class PushNotificationsDisabledTests(AccountWebApplicationFactory factory)
    : EndpointBaseTest<AccountDbContext>(factory), IClassFixture<AccountWebApplicationFactory>
{
    private const string RoutesPrefix = "/api/account/users/me/push-subscriptions";

    private const string NotAvailable = "Push notifications are not available.";

    [Fact]
    public async Task GetPushSubscriptions_WhenTheFeatureIsOff_ShouldReturnNotFound()
    {
        // Act
        var response = await AuthenticatedOwnerHttpClient.GetAsync(RoutesPrefix);

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.NotFound, NotAvailable);
    }

    [Fact]
    public async Task SavePushSubscription_WhenTheFeatureIsOff_ShouldReturnNotFound()
    {
        // Arrange
        var command = new SavePushSubscriptionCommand("https://push.example.com/off", PushNotificationsWebApplicationFactory.SubscriptionPublicKey, PushNotificationsWebApplicationFactory.SubscriptionAuthSecret, "Chrome on Linux", "/blazor/app");

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command);

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.NotFound, NotAvailable);
    }

    [Fact]
    public async Task SendTestPushNotification_WhenTheFeatureIsOff_ShouldReturnNotFound()
    {
        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsync($"{RoutesPrefix}/test", null);

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.NotFound, NotAvailable);
    }

    [Fact]
    public async Task DeletePushSubscription_WhenTheFeatureIsOff_ShouldReturnNotFound()
    {
        // Act
        var response = await AuthenticatedOwnerHttpClient.DeleteAsync($"{RoutesPrefix}/{PushSubscriptionId.NewId()}");

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.NotFound, NotAvailable);
    }
}
