using Account.Features.PushNotifications.Domain;
using Account.Features.PushNotifications.Shared;
using Account.Integrations.WebPush;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using SharedKernel.Cqrs;
using SharedKernel.ExecutionContext;
using SharedKernel.Localization;
using SharedKernel.Telemetry;

namespace Account.Features.PushNotifications.Commands;

[PublicAPI]
public sealed record SendTestPushNotificationCommand : ICommand, IRequest<Result<SendTestPushNotificationResponse>>;

/// <summary>
///     Sends the caller a notification on every device they subscribed with, so they can see that notifications arrive
///     before relying on them. The payload is the title and body of the current culture's resources and the path the
///     subscribing client opens at, and nothing else: no code, no token and no personal data. A push service that has
///     forgotten a subscription answers 404 or 410, and that subscription is deleted rather than retried. A subscription
///     whose push service this deployment no longer sends through is skipped and kept. A user may ask for this once per
///     <see cref="PushNotificationPolicy.TestNotificationInterval" />, so one account cannot decide how many requests this
///     deployment makes to the push services; a refused call sends nothing and removes nothing.
/// </summary>
public sealed class SendTestPushNotificationHandler(
    IPushSubscriptionRepository pushSubscriptionRepository,
    IPushNotificationSender pushNotificationSender,
    PushTestNotificationThrottle throttle,
    IExecutionContext executionContext,
    IConfiguration configuration,
    ITelemetryEventsCollector events
) : IRequestHandler<SendTestPushNotificationCommand, Result<SendTestPushNotificationResponse>>
{
    public async Task<Result<SendTestPushNotificationResponse>> Handle(SendTestPushNotificationCommand command, CancellationToken cancellationToken)
    {
        if (!PushNotificationPolicy.IsEnabled(configuration)) return Result<SendTestPushNotificationResponse>.NotFound("Push notifications are not available.");

        var userInfo = executionContext.UserInfo;
        if (userInfo.Id is null) return Result<SendTestPushNotificationResponse>.Unauthorized("A test notification must be sent from an authenticated session.");

        var subscriptions = await pushSubscriptionRepository.GetByUserAsync(userInfo.Id, cancellationToken);
        if (subscriptions.Length == 0) return Result<SendTestPushNotificationResponse>.BadRequest("This account has no device subscribed to notifications.");

        // The allowance is this account's own and says nothing about any other account's
        if (!throttle.TryBeginSend(userInfo.Id))
        {
            return Result<SendTestPushNotificationResponse>.TooManyRequests("A test notification was sent to this account recently. Please wait a minute before sending another.");
        }

        var allowedEndpointHosts = PushNotificationPolicy.GetAllowedEndpointHosts(configuration);

        var delivered = 0;
        var expired = new List<PushSubscription>();
        foreach (var subscription in subscriptions)
        {
            // A row written before this deployment narrowed its allowlist is counted as undelivered and kept, because
            // only the push service's own answer says a subscription is gone
            if (!PushNotificationPolicy.IsPushServiceEndpoint(subscription.Endpoint, allowedEndpointHosts)) continue;

            var payload = new PushNotificationPayload(AccountStrings.PushTestNotificationTitle, AccountStrings.PushTestNotificationBody, subscription.ApplicationPath);
            var target = new PushNotificationTarget(subscription.Endpoint, subscription.PublicKey, subscription.AuthSecret);
            var outcome = await pushNotificationSender.SendAsync(target, payload, cancellationToken);

            if (outcome == PushDeliveryOutcome.Delivered) delivered++;
            if (outcome == PushDeliveryOutcome.Expired) expired.Add(subscription);
        }

        foreach (var subscription in expired)
        {
            pushSubscriptionRepository.Remove(subscription);
            events.CollectEvent(new PushSubscriptionExpired(subscription.Id));
        }

        events.CollectEvent(new PushTestNotificationSent(delivered, expired.Count));

        return new SendTestPushNotificationResponse(delivered, expired.Count);
    }
}
