using Account.Features.PushNotifications.Commands;
using Account.Features.PushNotifications.Domain;
using Account.Features.PushNotifications.Queries;
using Account.Features.PushNotifications.Requests;

namespace Account.Client;

// The caller's own push subscriptions. Every call is scoped to the signed-in user by the account API; this client never
// names a user.
public sealed class PushSubscriptionsClient(HttpClient httpClient)
{
    private readonly AccountApiTransport _transport = new(httpClient);

    public Task<ApiCallResult<PushSubscriptionsResponse>> GetSubscriptionsAsync(CancellationToken cancellationToken)
    {
        return _transport.GetAsync<PushSubscriptionsResponse>(AccountApiRoutes.PushSubscriptions, cancellationToken);
    }

    public Task<ApiCallResult<SavePushSubscriptionResponse>> SaveSubscriptionAsync(SavePushSubscriptionCommand command, CancellationToken cancellationToken)
    {
        return _transport.SendAsync<SavePushSubscriptionCommand, SavePushSubscriptionResponse>(HttpMethod.Post, AccountApiRoutes.PushSubscriptions, command, cancellationToken);
    }

    public Task<ApiCallResult> DeleteSubscriptionAsync(PushSubscriptionId pushSubscriptionId, CancellationToken cancellationToken)
    {
        return _transport.SendAsync(HttpMethod.Delete, AccountApiRoutes.PushSubscription(pushSubscriptionId), cancellationToken);
    }

    public Task<ApiCallResult<SendTestPushNotificationResponse>> SendTestNotificationAsync(CancellationToken cancellationToken)
    {
        return _transport.SendAsync<SendTestPushNotificationResponse>(HttpMethod.Post, AccountApiRoutes.TestPushNotification, cancellationToken);
    }
}
