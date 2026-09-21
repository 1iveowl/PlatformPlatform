using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using Account.Integrations.WebPush;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace Account.Tests.PushNotifications;

public sealed class WebPushNotificationSenderTests
{
    private const string PushServiceEndpoint = "https://fcm.googleapis.com/fcm/send/sender-device";

    [Fact]
    public async Task SendAsync_WhenThePushServiceAnswersWithARedirect_ShouldNotFollowItAndReportUndelivered()
    {
        // Arrange
        var redirect = new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://169.254.169.254/metadata") } };
        var pushService = new RecordingHttpMessageHandler(redirect);
        using var sender = CreateSender(pushService);

        // Act
        var outcome = await sender.SendAsync(CreateTarget(), CreatePayload(), CancellationToken.None);

        // Assert
        outcome.Should().Be(PushDeliveryOutcome.Failed);
        pushService.RequestedUris.Should().Equal(new Uri(PushServiceEndpoint));
    }

    [Fact]
    public async Task SendAsync_WhenThePushServiceAcceptsTheNotification_ShouldReportDelivered()
    {
        // Arrange
        var pushService = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created));
        using var sender = CreateSender(pushService);

        // Act
        var outcome = await sender.SendAsync(CreateTarget(), CreatePayload(), CancellationToken.None);

        // Assert
        outcome.Should().Be(PushDeliveryOutcome.Delivered);
        pushService.RequestedUris.Should().Equal(new Uri(PushServiceEndpoint));
    }

    [Theory]
    // The push service has forgotten this subscription for good, and only these two answers say so
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task SendAsync_WhenThePushServiceHasForgottenTheSubscription_ShouldReportExpired(HttpStatusCode statusCode)
    {
        // Arrange
        var pushService = new RecordingHttpMessageHandler(new HttpResponseMessage(statusCode));
        using var sender = CreateSender(pushService);

        // Act
        var outcome = await sender.SendAsync(CreateTarget(), CreatePayload(), CancellationToken.None);

        // Assert
        outcome.Should().Be(PushDeliveryOutcome.Expired);
    }

    [Fact]
    public async Task SendAsync_WhenThePushServiceRefuses_ShouldReportUndeliveredAndKeepTheSubscription()
    {
        // Arrange
        var pushService = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var sender = CreateSender(pushService);

        // Act
        var outcome = await sender.SendAsync(CreateTarget(), CreatePayload(), CancellationToken.None);

        // Assert
        outcome.Should().Be(PushDeliveryOutcome.Failed);
    }

    [Fact]
    public async Task SendAsync_WhenThePushServiceNeverAnswers_ShouldReportUndeliveredRatherThanThrow()
    {
        // Arrange
        var pushService = new NeverAnsweringHttpMessageHandler();
        using var sender = CreateSender(pushService, TimeSpan.FromMilliseconds(100));

        // Act
        var outcome = await sender.SendAsync(CreateTarget(), CreatePayload(), CancellationToken.None);

        // Assert: the caller did not cancel anything, so this is the client's own timeout and not a request going away
        outcome.Should().Be(PushDeliveryOutcome.Failed);
    }

    [Fact]
    public async Task SendAsync_WhenTheCallerCancels_ShouldLetTheCancellationThrough()
    {
        // Arrange
        var pushService = new NeverAnsweringHttpMessageHandler();
        using var sender = CreateSender(pushService);
        using var callerCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        OperationCanceledException? cancellation = null;
        try
        {
            await sender.SendAsync(CreateTarget(), CreatePayload(), callerCancellation.Token);
        }
        catch (OperationCanceledException exception)
        {
            cancellation = exception;
        }

        // Assert: a cancelled request is the request going away, which is not an outcome of the notification
        cancellation.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_WhenTheSubscriptionKeyCannotBeUsed_ShouldReportUndeliveredRatherThanThrow()
    {
        // Arrange
        var pushService = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created));
        using var sender = CreateSender(pushService);
        var target = new PushNotificationTarget(PushServiceEndpoint, "bm90LWEtcDI1Ni1wb2ludA", PushNotificationsWebApplicationFactory.SubscriptionAuthSecret);

        // Act
        var outcome = await sender.SendAsync(target, CreatePayload(), CancellationToken.None);

        // Assert
        outcome.Should().Be(PushDeliveryOutcome.Failed);
        pushService.RequestedUris.Should().BeEmpty();
    }

    private static WebPushNotificationSender CreateSender(HttpMessageHandler pushService, TimeSpan? timeout = null)
    {
        using var vapidKeyPair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var vapidParameters = vapidKeyPair.ExportParameters(true);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PushNotifications:VapidPublicKey"] = Base64Url.EncodeToString([0x04, .. vapidParameters.Q.X!, .. vapidParameters.Q.Y!]),
                ["PushNotifications:VapidPrivateKey"] = Base64Url.EncodeToString(vapidParameters.D!),
                ["PushNotifications:Subject"] = "mailto:no-reply@platformplatform.net"
            }
        ).Build();

        var httpClient = new HttpClient(pushService);
        if (timeout is not null) httpClient.Timeout = timeout.Value;

        return new WebPushNotificationSender(httpClient, configuration, Substitute.For<ILogger<WebPushNotificationSender>>());
    }

    private static PushNotificationTarget CreateTarget()
    {
        return new PushNotificationTarget(PushServiceEndpoint, PushNotificationsWebApplicationFactory.SubscriptionPublicKey, PushNotificationsWebApplicationFactory.SubscriptionAuthSecret);
    }

    private static PushNotificationPayload CreatePayload()
    {
        return new PushNotificationPayload("Test notification", "Notifications are working on this device.", "/blazor/app");
    }

    // A push service that accepts the request and never answers, which is what the client's own timeout is measured against
    private sealed class NeverAnsweringHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }

    private sealed class RecordingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        private readonly List<Uri> _requestedUris = [];

        public Uri[] RequestedUris => _requestedUris.ToArray();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requestedUris.Add(request.RequestUri!);
            return Task.FromResult(response);
        }
    }
}
