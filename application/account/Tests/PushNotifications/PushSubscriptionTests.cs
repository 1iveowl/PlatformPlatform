using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Account.Database;
using Account.Features.PushNotifications.Domain;
using Account.Features.PushNotifications.Requests;
using Account.Features.PushNotifications.Shared;
using Account.Features.Subscriptions.Domain;
using Account.Features.Tenants.Domain;
using Account.Features.Users.Domain;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.Tests;
using SharedKernel.Tests.Persistence;
using SharedKernel.Validation;
using Xunit;

namespace Account.Tests.PushNotifications;

public sealed class PushSubscriptionTests(PushNotificationsWebApplicationFactory factory)
    : EndpointBaseTest<AccountDbContext>(factory), IClassFixture<PushNotificationsWebApplicationFactory>
{
    private const string RoutesPrefix = "/api/account/users/me/push-subscriptions";

    // A second valid 16 byte secret, so a resubscribe changes something the row must take over
    private const string SecondAuthSecret = "c2Vjb25kLWF1dGgtc2VjIQ";

    private readonly RecordingPushNotificationSender _pushNotificationSender = factory.PushNotificationSender;

    [Fact]
    public async Task SavePushSubscription_WhenNew_ShouldStoreSubscriptionForTheCallerAndCollectEvent()
    {
        // Arrange
        var command = CreateCommand("subscribe-new");

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        var pushSubscriptionId = await ReadSubscriptionIdAsync(response);
        Connection.RowExists("push_subscriptions", pushSubscriptionId).Should().BeTrue();
        Connection.ExecuteScalar<string>("SELECT user_id FROM push_subscriptions WHERE id = @id", [new { id = pushSubscriptionId }])
            .Should().Be(DatabaseSeeder.Tenant1Owner.Id.ToString());
        Connection.ExecuteScalar<long>("SELECT tenant_id FROM push_subscriptions WHERE id = @id", [new { id = pushSubscriptionId }])
            .Should().Be(DatabaseSeeder.Tenant1.Id.Value);

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("PushSubscriptionCreated");
        TelemetryEventsCollectorSpy.AreAllEventsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task SavePushSubscription_WhenTheSameBrowserSubscribesAgain_ShouldUpdateTheSameRow()
    {
        // Arrange
        var command = CreateCommand("subscribe-again");
        var firstResponse = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command);
        var firstSubscriptionId = await ReadSubscriptionIdAsync(firstResponse);

        // Act
        var secondResponse = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command with { AuthSecret = SecondAuthSecret, DeviceLabel = "Firefox on Linux" });

        // Assert
        secondResponse.ShouldBeSuccessfulGetRequest();
        (await ReadSubscriptionIdAsync(secondResponse)).Should().Be(firstSubscriptionId);
        Connection.ExecuteScalar<string>("SELECT auth_secret FROM push_subscriptions WHERE id = @id", [new { id = firstSubscriptionId }]).Should().Be(SecondAuthSecret);
        Connection.ExecuteScalar<string>("SELECT device_label FROM push_subscriptions WHERE id = @id", [new { id = firstSubscriptionId }]).Should().Be("Firefox on Linux");

        TelemetryEventsCollectorSpy.CollectedEvents.Select(collected => collected.GetType().Name)
            .Should().Equal("PushSubscriptionCreated", "PushSubscriptionUpdated");
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/google-device")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/mozilla-device")]
    [InlineData("https://push.apple.com/apple-device")]
    [InlineData("https://web.push.apple.com/apple-subdomain-device")]
    [InlineData("https://par02p.notify.windows.com/w/windows-subdomain-device")]
    public async Task SavePushSubscription_WhenTheEndpointIsAKnownPushService_ShouldStoreIt(string endpoint)
    {
        // Arrange
        var command = CreateCommand("allowlisted") with { Endpoint = endpoint };

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        var pushSubscriptionId = await ReadSubscriptionIdAsync(response);
        Connection.ExecuteScalar<string>("SELECT endpoint FROM push_subscriptions WHERE id = @id", [new { id = pushSubscriptionId }]).Should().Be(endpoint);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/insecure")]
    [InlineData("not-a-url")]
    [InlineData("https://someone:secret@fcm.googleapis.com/fcm/send/user-information")]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/other-port")]
    public async Task SavePushSubscription_WhenTheEndpointIsNotAPlainHttpsAddress_ShouldReturnBadRequest(string endpoint)
    {
        // Arrange
        var command = CreateCommand("rejected") with { Endpoint = endpoint };

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command);

        // Assert
        await response.ShouldHaveErrorStatusCode(
            HttpStatusCode.BadRequest,
            [new ErrorDetail("Endpoint", "The push service address must be an https address with no user information and no port, of at most 2000 characters.")]
        );
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM push_subscriptions", []).Should().Be(0);
        _pushNotificationSender.SendsTo(endpoint).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://notfcm.googleapis.com/fcm/send/look-alike")]
    [InlineData("https://fcm.googleapis.com.evil.example.net/fcm/send/suffix-only")]
    [InlineData("https://push.apple.com.evil.example.net/suffix-only")]
    [InlineData("https://169.254.169.254/metadata")]
    [InlineData("https://internal-service.local/secret")]
    public async Task SavePushSubscription_WhenTheHostIsNotAKnownPushService_ShouldReturnBadRequestAndSendNothing(string endpoint)
    {
        // Arrange
        var command = CreateCommand("not-a-push-service") with { Endpoint = endpoint };

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command);

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.BadRequest, "The push service address is not one this system sends notifications through.");
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM push_subscriptions", []).Should().Be(0);
        _pushNotificationSender.SendsTo(endpoint).Should().BeEmpty();
    }

    [Fact]
    public async Task SavePushSubscription_WhenTheCallerAlreadyHoldsTheMaximumNumberOfSubscriptions_ShouldReturnBadRequest()
    {
        // Arrange
        InsertSubscriptionsForOwnerUpToTheLimit();

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, CreateCommand("one-device-too-many"));

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.BadRequest, "This account has reached the limit of 20 devices subscribed to notifications.");
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM push_subscriptions", []).Should().Be(PushNotificationPolicy.MaximumSubscriptionsPerUser);
    }

    [Fact]
    public async Task SavePushSubscription_WhenTheCallerIsAtTheMaximumAndResubscribesAKnownDevice_ShouldUpdateThatSubscription()
    {
        // Arrange
        InsertSubscriptionsForOwnerUpToTheLimit();
        var command = CreateCommand("limit-0") with { AuthSecret = SecondAuthSecret };

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        var pushSubscriptionId = await ReadSubscriptionIdAsync(response);
        Connection.ExecuteScalar<string>("SELECT auth_secret FROM push_subscriptions WHERE id = @id", [new { id = pushSubscriptionId }]).Should().Be(SecondAuthSecret);
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM push_subscriptions", []).Should().Be(PushNotificationPolicy.MaximumSubscriptionsPerUser);
    }

    [Fact]
    public async Task SavePushSubscription_WhenAnotherUserOfTheSameTenantIsAtTheMaximum_ShouldStoreTheCallersSubscription()
    {
        // Arrange
        for (var deviceNumber = 0; deviceNumber < PushNotificationPolicy.MaximumSubscriptionsPerUser; deviceNumber++)
        {
            InsertSubscription(DatabaseSeeder.Tenant1.Id, DatabaseSeeder.Tenant1Member.Id, $"https://fcm.googleapis.com/fcm/send/member-limit-{deviceNumber}");
        }

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, CreateCommand("own-first-device"));

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        Connection.ExecuteScalar<long>("SELECT COUNT(*) FROM push_subscriptions WHERE user_id = @userId", [new { userId = DatabaseSeeder.Tenant1Owner.Id.ToString() }]).Should().Be(1);
    }

    [Fact]
    public async Task SavePushSubscription_WhenTheSubscriptionKeysAreNotTheShapeThePushProtocolDefines_ShouldReturnBadRequest()
    {
        // Arrange
        var command = CreateCommand("bad-keys") with { PublicKey = "not-a-point", AuthSecret = "not-a-secret" };

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command);

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.BadRequest, [
                new ErrorDetail("PublicKey", "The subscription key must be a base64url encoded uncompressed P-256 public key."),
                new ErrorDetail("AuthSecret", "The subscription secret must be a base64url encoded 16 byte value.")
            ]
        );
    }

    [Fact]
    public async Task SavePushSubscription_WhenTheApplicationPathLeavesThisOrigin_ShouldReturnBadRequest()
    {
        // Arrange
        var command = CreateCommand("escaping-path") with { ApplicationPath = "//evil.example.com/app" };

        // Act
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, command);

        // Assert
        await response.ShouldHaveErrorStatusCode(
            HttpStatusCode.BadRequest, [new ErrorDetail("ApplicationPath", "The application path must be a rooted path of at most 200 characters.")]
        );
    }

    [Fact]
    public async Task GetPushSubscriptions_WhenAnotherUserHasSubscriptions_ShouldReturnOnlyTheCallersOwn()
    {
        // Arrange
        var ownResponse = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, CreateCommand("owner-list"));
        var ownSubscriptionId = await ReadSubscriptionIdAsync(ownResponse);
        var memberSubscriptionId = InsertSubscription(DatabaseSeeder.Tenant1.Id, DatabaseSeeder.Tenant1Member.Id, "https://fcm.googleapis.com/fcm/send/member-list");

        // Act
        var response = await AuthenticatedOwnerHttpClient.GetAsync(RoutesPrefix);

        // Assert
        response.ShouldBeSuccessfulGetRequest();
        var subscriptions = (await ReadJsonAsync(response)).GetProperty("subscriptions").EnumerateArray()
            .Select(subscription => subscription.GetProperty("id").GetString()).ToArray();
        subscriptions.Should().Contain(ownSubscriptionId);
        subscriptions.Should().NotContain(memberSubscriptionId.ToString());
    }

    [Fact]
    public async Task DeletePushSubscription_WhenItBelongsToTheCaller_ShouldRemoveItAndCollectEvent()
    {
        // Arrange
        var response = await AuthenticatedOwnerHttpClient.PostAsJsonAsync(RoutesPrefix, CreateCommand("owner-delete"));
        var pushSubscriptionId = await ReadSubscriptionIdAsync(response);
        TelemetryEventsCollectorSpy.Reset();

        // Act
        var deleteResponse = await AuthenticatedOwnerHttpClient.DeleteAsync($"{RoutesPrefix}/{pushSubscriptionId}");

        // Assert
        deleteResponse.ShouldHaveEmptyHeaderAndLocationOnSuccess();
        Connection.RowExists("push_subscriptions", pushSubscriptionId).Should().BeFalse();

        TelemetryEventsCollectorSpy.CollectedEvents.Count.Should().Be(1);
        TelemetryEventsCollectorSpy.CollectedEvents[0].GetType().Name.Should().Be("PushSubscriptionDeleted");
    }

    [Fact]
    public async Task DeletePushSubscription_WhenItBelongsToAnotherUserOfTheSameTenant_ShouldReturnForbidden()
    {
        // Arrange
        var memberSubscriptionId = InsertSubscription(DatabaseSeeder.Tenant1.Id, DatabaseSeeder.Tenant1Member.Id, "https://fcm.googleapis.com/fcm/send/member-delete");

        // Act
        var response = await AuthenticatedOwnerHttpClient.DeleteAsync($"{RoutesPrefix}/{memberSubscriptionId}");

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.Forbidden, "A push subscription can only be removed by the user who made it.");
        Connection.RowExists("push_subscriptions", memberSubscriptionId.ToString()).Should().BeTrue();
    }

    [Fact]
    public async Task DeletePushSubscription_WhenItBelongsToAnotherTenant_ShouldReturnNotFound()
    {
        // Arrange
        var (otherTenantId, otherUserId) = InsertOtherTenantWithUser();
        var otherSubscriptionId = InsertSubscription(otherTenantId, otherUserId, "https://fcm.googleapis.com/fcm/send/other-tenant");

        // Act
        var response = await AuthenticatedOwnerHttpClient.DeleteAsync($"{RoutesPrefix}/{otherSubscriptionId}");

        // Assert
        await response.ShouldHaveErrorStatusCode(HttpStatusCode.NotFound, $"Push subscription with ID '{otherSubscriptionId}' not found.");
        Connection.RowExists("push_subscriptions", otherSubscriptionId.ToString()).Should().BeTrue();
    }

    [Fact]
    public async Task GetPushSubscriptions_WhenAnonymous_ShouldReturnUnauthorized()
    {
        // Act
        var response = await AnonymousHttpClient.GetAsync(RoutesPrefix);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private void InsertSubscriptionsForOwnerUpToTheLimit()
    {
        for (var deviceNumber = 0; deviceNumber < PushNotificationPolicy.MaximumSubscriptionsPerUser; deviceNumber++)
        {
            InsertSubscription(DatabaseSeeder.Tenant1.Id, DatabaseSeeder.Tenant1Owner.Id, $"https://fcm.googleapis.com/fcm/send/limit-{deviceNumber}");
        }
    }

    private static SavePushSubscriptionCommand CreateCommand(string name)
    {
        return new SavePushSubscriptionCommand($"https://fcm.googleapis.com/fcm/send/{name}", PushNotificationsWebApplicationFactory.SubscriptionPublicKey, PushNotificationsWebApplicationFactory.SubscriptionAuthSecret, "Chrome on Linux", "/blazor/app");
    }

    private static async Task<string> ReadSubscriptionIdAsync(HttpResponseMessage response)
    {
        return (await ReadJsonAsync(response)).GetProperty("pushSubscriptionId").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private PushSubscriptionId InsertSubscription(TenantId tenantId, UserId userId, string endpoint)
    {
        var pushSubscriptionId = PushSubscriptionId.NewId();
        Connection.Insert("push_subscriptions", [
                ("tenant_id", tenantId.Value),
                ("id", pushSubscriptionId.Value),
                ("user_id", userId.Value),
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

    private (TenantId TenantId, UserId UserId) InsertOtherTenantWithUser()
    {
        var tenantId = TenantId.NewId();
        Connection.Insert("tenants", [
                ("id", tenantId.Value),
                ("created_at", TimeProvider.GetUtcNow()),
                ("modified_at", null),
                ("name", "Other tenant"),
                ("state", nameof(TenantState.Active)),
                ("logo", """{"Url":null,"Version":0}"""),
                ("plan", nameof(SubscriptionPlan.Basis)),
                ("rollout_bucket", 42)
            ]
        );

        var userId = UserId.NewId();
        Connection.Insert("users", [
                ("tenant_id", tenantId.Value),
                ("id", userId.Value),
                ("created_at", TimeProvider.GetUtcNow()),
                ("modified_at", null),
                ("email", "other@platformplatform.net"),
                ("email_confirmed", true),
                ("first_name", "Other"),
                ("last_name", "User"),
                ("title", null),
                ("avatar", """{"Url":null,"Version":0,"IsGravatar":false}"""),
                ("role", nameof(UserRole.Owner)),
                ("locale", "en-US"),
                ("external_identities", "[]"),
                ("rollout_bucket", 42)
            ]
        );

        return (tenantId, userId);
    }
}
