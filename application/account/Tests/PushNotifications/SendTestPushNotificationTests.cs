using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Account.Database;
using Account.Features.PushNotifications.Requests;
using FluentAssertions;
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
        var endpoint = $"https://push.example.com/test-send/{Guid.NewGuid():N}";
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
        var endpoint = $"https://push.example.com{RecordingPushNotificationSender.ForgottenSubscriptionSegment}{Guid.NewGuid():N}";
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
        var endpoint = $"https://push.example.com{RecordingPushNotificationSender.RefusingSubscriptionSegment}{Guid.NewGuid():N}";
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
    public async Task SendTestPushNotification_WhenTheCallerHasNoSubscription_ShouldReturnBadRequest()
    {
        // Act
        var response = await AuthenticatedMemberHttpClient.PostAsync($"{RoutesPrefix}/test", null);

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.BadRequest, "This account has no device subscribed to notifications.");
    }

    private async Task<string> SubscribeAsync(string endpoint)
    {
        var command = new SavePushSubscriptionCommand(endpoint, PushNotificationsWebApplicationFactory.SubscriptionPublicKey, PushNotificationsWebApplicationFactory.SubscriptionAuthSecret, "Chrome on Linux", "/blazor/app");
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command);
        response.ShouldBeSuccessfulGetRequest();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("pushSubscriptionId").GetString()!;
    }
}
