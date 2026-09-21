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

    [Fact]
    public async Task SendTestPushNotification_WhenOneDeviceTimesOut_ShouldStillReachTheRestAndDeleteTheExpiredOnes()
    {
        // Arrange: the order rows are sent in is the order they were made, so the device that never answers is the first
        var timingOutEndpoint = $"https://fcm.googleapis.com{RecordingPushNotificationSender.TimingOutSubscriptionSegment}{Guid.NewGuid():N}";
        var forgottenEndpoint = $"https://fcm.googleapis.com{RecordingPushNotificationSender.ForgottenSubscriptionSegment}{Guid.NewGuid():N}";
        var reachableEndpoint = $"https://fcm.googleapis.com/fcm/send/after-the-timeout-{Guid.NewGuid():N}";
        await SubscribeAsync(timingOutEndpoint);
        var forgottenSubscriptionId = await SubscribeAsync(forgottenEndpoint);
        await SubscribeAsync(reachableEndpoint);

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsync($"{RoutesPrefix}/test", null);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("delivered").GetInt32().Should().Be(1);
        body.GetProperty("removed").GetInt32().Should().Be(1);
        _pushNotificationSender.SendsTo(reachableEndpoint).Length.Should().Be(1);
        Connection.RowExists("push_subscriptions", forgottenSubscriptionId).Should().BeFalse();
    }

    [Fact]
    public async Task SendTestPushNotification_WhenCalledAgainWithinTheInterval_ShouldRefuseItAndSendNothing()
    {
        // Arrange
        var endpoint = $"https://fcm.googleapis.com/fcm/send/throttled-{Guid.NewGuid():N}";
        var pushSubscriptionId = await SubscribeAsync(endpoint);
        (await AuthenticatedOwnerHttpClient.PostAsync($"{RoutesPrefix}/test", null)).ShouldBeSuccessfulGetRequest();
        var sendsAfterTheFirstCall = _pushNotificationSender.SendsTo(endpoint).Length;

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsync($"{RoutesPrefix}/test", null);

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.TooManyRequests, "A test notification was sent to this account recently. Please wait a minute before sending another.");
        _pushNotificationSender.SendsTo(endpoint).Length.Should().Be(sendsAfterTheFirstCall);
        Connection.RowExists("push_subscriptions", pushSubscriptionId).Should().BeTrue();
    }

    [Fact]
    public async Task SendTestPushNotification_WhenAnotherUserWasRefused_ShouldStillBeAllowed()
    {
        // Arrange
        var ownerEndpoint = $"https://fcm.googleapis.com/fcm/send/owner-allowance-{Guid.NewGuid():N}";
        await SubscribeAsync(ownerEndpoint);
        (await AuthenticatedOwnerHttpClient.PostAsync($"{RoutesPrefix}/test", null)).ShouldBeSuccessfulGetRequest();
        (await AuthenticatedOwnerHttpClient.PostAsync($"{RoutesPrefix}/test", null)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var memberEndpoint = $"https://fcm.googleapis.com/fcm/send/member-allowance-{Guid.NewGuid():N}";
        var memberCommand = new SavePushSubscriptionCommand(memberEndpoint, PushNotificationsWebApplicationFactory.SubscriptionPublicKey, PushNotificationsWebApplicationFactory.SubscriptionAuthSecret, "Chrome on Linux", "/blazor/app");
        (await AuthenticatedMemberHttpClient.PostAsJsonAsync(RoutesPrefix, memberCommand)).ShouldBeSuccessfulGetRequest();

        // Act
        var response = await AuthenticatedMemberHttpClient.PostAsync($"{RoutesPrefix}/test", null);

        // Assert: the allowance is per user, and a refusal says nothing about any other account
        response.ShouldBeSuccessfulGetRequest();
        _pushNotificationSender.SendsTo(memberEndpoint).Length.Should().Be(1);
    }

    private PushSubscriptionId InsertSubscriptionForOwner(string endpoint, int deviceSlot = 0)
    {
        var pushSubscriptionId = PushSubscriptionId.NewId();
        Connection.Insert("push_subscriptions", [
                ("tenant_id", DatabaseSeeder.Tenant1.Id.Value),
                ("id", pushSubscriptionId.Value),
                ("user_id", DatabaseSeeder.Tenant1Owner.Id.Value),
                ("device_slot", deviceSlot),
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
