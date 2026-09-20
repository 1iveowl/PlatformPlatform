using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Account.Database;
using Account.Features.PushNotifications.Domain;
using Account.Features.PushNotifications.Requests;
using Account.Integrations.WebPush;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Tests;
using SharedKernel.Tests.Persistence;
using Xunit;

namespace Account.Tests.PushNotifications;

public sealed class SendTestPushNotificationTests(PushNotificationsWebApplicationFactory factory)
    : EndpointBaseTest<AccountDbContext>(factory), IClassFixture<PushNotificationsWebApplicationFactory>
{
    private const string RoutesPrefix = "/api/account/users/me/push-subscriptions";

    private readonly RecordingPushNotificationSender _pushNotificationSender = factory.PushNotificationSender;

    [Fact]
    public async Task SendTestPushNotification_WhenTheCallerHasASubscription_ShouldSendOneNotificationWithNoPersonalData()
    {
        // Arrange
        var endpoint = $"https://fcm.googleapis.com/fcm/send/test-send-{Guid.NewGuid():N}";
        await SubscribeAsync(endpoint);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsync($"{RoutesPrefix}/test", null);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("delivered").GetInt32().Should().Be(1);
        body.GetProperty("removed").GetInt32().Should().Be(0);

        var sends = _pushNotificationSender.SendsTo(endpoint);
        sends.Length.Should().Be(1);
        sends[0].Payload.Title.Should().Be("Test notification");
        sends[0].Payload.Body.Should().Be("Notifications are working on this device.");
        sends[0].Payload.Url.Should().Be("/blazor/app");

        TelemetryEventsCollectorSpy.CollectedEvents.Select(collected => collected.GetType().Name).Should().Equal("PushTestNotificationSent");
    }

    [Fact]
    public async Task SendTestPushNotification_WhenThePushServiceHasForgottenTheSubscription_ShouldDeleteItAndCollectEvent()
    {
        // Arrange
        var endpoint = $"https://fcm.googleapis.com{RecordingPushNotificationSender.ForgottenSubscriptionSegment}{Guid.NewGuid():N}";
        var pushSubscriptionId = await SubscribeAsync(endpoint);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsync($"{RoutesPrefix}/test", null);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("delivered").GetInt32().Should().Be(0);
        body.GetProperty("removed").GetInt32().Should().Be(1);
        Connection.RowExists("push_subscriptions", pushSubscriptionId).Should().BeFalse();

        TelemetryEventsCollectorSpy.CollectedEvents.Select(collected => collected.GetType().Name)
            .Should().Equal("PushSubscriptionExpired", "PushTestNotificationSent");
    }

    [Fact]
    public async Task SendTestPushNotification_WhenThePushServiceRefuses_ShouldKeepTheSubscription()
    {
        // Arrange
        var endpoint = $"https://fcm.googleapis.com{RecordingPushNotificationSender.RefusingSubscriptionSegment}{Guid.NewGuid():N}";
        var pushSubscriptionId = await SubscribeAsync(endpoint);

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsync($"{RoutesPrefix}/test", null);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("delivered").GetInt32().Should().Be(0);
        body.GetProperty("removed").GetInt32().Should().Be(0);
        Connection.RowExists("push_subscriptions", pushSubscriptionId).Should().BeTrue();
    }

    [Fact]
    public async Task SendTestPushNotification_WhenAStoredEndpointIsNoLongerAKnownPushService_ShouldSkipItAndKeepIt()
    {
        // Arrange
        const string endpoint = "https://internal-service.local/secret";
        var pushSubscriptionId = InsertSubscriptionForOwner(endpoint);

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsync($"{RoutesPrefix}/test", null);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("delivered").GetInt32().Should().Be(0);
        body.GetProperty("removed").GetInt32().Should().Be(0);
        Connection.RowExists("push_subscriptions", pushSubscriptionId.ToString()).Should().BeTrue();
        _pushNotificationSender.SendsTo(endpoint).Should().BeEmpty();
    }

    [Fact]
    public void PushNotificationSender_ShouldUseAnHttpClientThatDoesNotFollowRedirects()
    {
        // Act
        var handler = factory.Services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(IPushNotificationSender));

        // Assert
        var primaryHandler = handler;
        while (primaryHandler is DelegatingHandler delegatingHandler)
        {
            primaryHandler = delegatingHandler.InnerHandler!;
        }

        primaryHandler.Should().BeOfType<SocketsHttpHandler>().Which.AllowAutoRedirect.Should().BeFalse();
    }

    [Fact]
    public async Task SendTestPushNotification_WhenTheCallerHasNoSubscription_ShouldReturnBadRequest()
    {
        // Act
        var response = await AuthenticatedMemberHttpClient.PostAsync($"{RoutesPrefix}/test", null);

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.BadRequest, "This account has no device subscribed to notifications.");
    }

    private PushSubscriptionId InsertSubscriptionForOwner(string endpoint)
    {
        var pushSubscriptionId = PushSubscriptionId.NewId();
        Connection.Insert("push_subscriptions", [
                ("tenant_id", DatabaseSeeder.Tenant1.Id.Value),
                ("id", pushSubscriptionId.Value),
                ("user_id", DatabaseSeeder.Tenant1Owner.Id.Value),
                ("created_at", TimeProvider.GetUtcNow()),
                ("modified_at", null),
                ("endpoint", endpoint),
                ("public_key", PushNotificationsWebApplicationFactory.SubscriptionPublicKey),
                ("auth_secret", PushNotificationsWebApplicationFactory.SubscriptionAuthSecret),
                ("device_label", "Chrome on Linux"),
                ("application_path", "/blazor/app")
            ]
        );
        return pushSubscriptionId;
    }

    private async Task<string> SubscribeAsync(string endpoint)
    {
        var command = new SavePushSubscriptionCommand(endpoint, PushNotificationsWebApplicationFactory.SubscriptionPublicKey, PushNotificationsWebApplicationFactory.SubscriptionAuthSecret, "Chrome on Linux", "/blazor/app");
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command);
        response.ShouldBeSuccessfulGetRequest();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("pushSubscriptionId").GetString()!;
    }
}
