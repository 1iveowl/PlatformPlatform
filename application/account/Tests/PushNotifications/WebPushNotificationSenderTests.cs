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

    private static WebPushNotificationSender CreateSender(HttpMessageHandler pushService)
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

        return new WebPushNotificationSender(new HttpClient(pushService), configuration, Substitute.For<ILogger<WebPushNotificationSender>>());
    }

    private static PushNotificationTarget CreateTarget()
    {
        return new PushNotificationTarget(PushServiceEndpoint, PushNotificationsWebApplicationFactory.SubscriptionPublicKey, PushNotificationsWebApplicationFactory.SubscriptionAuthSecret);
    }

    private static PushNotificationPayload CreatePayload()
    {
        return new PushNotificationPayload("Test notification", "Notifications are working on this device.", "/blazor/app");
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
