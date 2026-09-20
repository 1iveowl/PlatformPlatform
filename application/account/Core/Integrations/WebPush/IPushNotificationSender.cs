namespace Account.Integrations.WebPush;

/// <summary>
///     Sends one Web Push message to one browser subscription. The transport is the push service the subscription
///     names; this system never learns which browser or device is behind it.
/// </summary>
public interface IPushNotificationSender
{
    /// <summary>Whether this deployment holds a VAPID key pair to sign with. Without one nothing can be sent.</summary>
    bool IsConfigured { get; }

    Task<PushDeliveryOutcome> SendAsync(PushNotificationTarget target, PushNotificationPayload payload, CancellationToken cancellationToken);
}

/// <summary>What the push service answered: delivered, gone for good, or a failure worth keeping the subscription for.</summary>
public enum PushDeliveryOutcome
{
    Delivered,
    Expired,
    Failed
}

public sealed record PushNotificationTarget(string Endpoint, string PublicKey, string AuthSecret);

// Everything the service worker shows. It holds no one-time code, no token and no personal data: the title and body come
// from the resources and the path is where the application opens, which the worker checks against its own scope again.
public sealed record PushNotificationPayload(string Title, string Body, string Url);
