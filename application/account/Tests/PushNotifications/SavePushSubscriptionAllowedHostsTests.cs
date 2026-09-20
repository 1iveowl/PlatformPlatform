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

public sealed class SavePushSubscriptionAllowedHostsTests(ConfiguredHostsPushNotificationsWebApplicationFactory factory)
    : EndpointBaseTest<AccountDbContext>(factory), IClassFixture<ConfiguredHostsPushNotificationsWebApplicationFactory>
{
    private const string RoutesPrefix = "/api/account/users/me/push-subscriptions";

    [Fact]
    public async Task SavePushSubscription_WhenTheEndpointIsOnAConfiguredHost_ShouldStoreIt()
    {
        // Arrange
        var endpoint = $"https://{ConfiguredHostsPushNotificationsWebApplicationFactory.ConfiguredEndpointHost}/harness-device";

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, CreateCommand(endpoint));

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        var pushSubscriptionId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("pushSubscriptionId").GetString()!;
        Connection.ExecuteScalar<string>("SELECT endpoint FROM push_subscriptions WHERE id = @id", [new { id = pushSubscriptionId }]).Should().Be(endpoint);
    }

    [Fact]
    public async Task SavePushSubscription_WhenTheConfiguredHostsDoNotNameADefaultPushService_ShouldReturnBadRequest()
    {
        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, CreateCommand("https://fcm.googleapis.com/fcm/send/google-device"));

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.BadRequest, "The push service address is not one this system sends notifications through.");
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM push_subscriptions", []).Should().Be(0);
    }

    private static SavePushSubscriptionCommand CreateCommand(string endpoint)
    {
        return new SavePushSubscriptionCommand(endpoint, PushNotificationsWebApplicationFactory.SubscriptionPublicKey, PushNotificationsWebApplicationFactory.SubscriptionAuthSecret, "Chrome on Linux", "/blazor/app");
    }
}
