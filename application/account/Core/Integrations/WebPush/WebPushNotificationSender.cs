using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Configuration;
using PushServiceSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace Account.Integrations.WebPush;

/// <summary>
///     The Web Push protocol over the push service the subscription names: the payload is encrypted for that browser's
///     key pair (RFC 8291) and the request is signed with this deployment's VAPID key pair (RFC 8292). A push service
///     answering 404 or 410 has forgotten the subscription for good, which is reported as <see cref="PushDeliveryOutcome.Expired" />
///     so the caller can delete it.
/// </summary>
public sealed class WebPushNotificationSender : IPushNotificationSender, IDisposable
{
    private readonly ILogger<WebPushNotificationSender> _logger;
    private readonly PushServiceClient _pushServiceClient;
    private readonly VapidAuthentication? _vapidAuthentication;

    public WebPushNotificationSender(HttpClient httpClient, IConfiguration configuration, ILogger<WebPushNotificationSender> logger)
    {
        _logger = logger;
        _pushServiceClient = new PushServiceClient(httpClient);

        var publicKey = configuration["PushNotifications:VapidPublicKey"];
        var privateKey = configuration["PushNotifications:VapidPrivateKey"];
        if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey)) return;

        _vapidAuthentication = new VapidAuthentication(publicKey, privateKey)
        {
            Subject = configuration["PushNotifications:Subject"] ?? "mailto:no-reply@localhost"
        };
    }

    public void Dispose()
    {
        _vapidAuthentication?.Dispose();
    }

    public bool IsConfigured => _vapidAuthentication is not null;

    public async Task<PushDeliveryOutcome> SendAsync(PushNotificationTarget target, PushNotificationPayload payload, CancellationToken cancellationToken)
    {
        if (_vapidAuthentication is null) return PushDeliveryOutcome.Failed;

        var subscription = new PushServiceSubscription { Endpoint = target.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, target.PublicKey);
        subscription.SetKey(PushEncryptionKeyName.Auth, target.AuthSecret);

        var message = new PushMessage(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)) { Urgency = PushMessageUrgency.Normal };

        try
        {
            await _pushServiceClient.RequestPushMessageDeliveryAsync(subscription, message, _vapidAuthentication, cancellationToken);
            return PushDeliveryOutcome.Delivered;
        }
        catch (PushServiceClientException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushDeliveryOutcome.Expired;
        }
        catch (PushServiceClientException exception)
        {
            _logger.LogWarning("Push service refused a notification with status {StatusCode}", exception.StatusCode);
            return PushDeliveryOutcome.Failed;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Push service could not be reached");
            return PushDeliveryOutcome.Failed;
        }
        catch (CryptographicException exception)
        {
            // Either this deployment's VAPID pair or the subscription's own key pair cannot be used; the subscription is
            // kept, because a key the deployment holds wrongly is not the subscriber's to fix
            _logger.LogWarning(exception, "Push notification could not be encrypted or signed");
            return PushDeliveryOutcome.Failed;
        }
    }
}
