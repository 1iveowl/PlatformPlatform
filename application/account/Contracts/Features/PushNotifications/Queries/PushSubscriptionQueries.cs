using Account.Features.PushNotifications.Domain;
using JetBrains.Annotations;

namespace Account.Features.PushNotifications.Queries;

[PublicAPI]
public sealed record PushSubscriptionsResponse(PushSubscriptionDetails[] Subscriptions);

[PublicAPI]
public sealed record PushSubscriptionDetails(PushSubscriptionId Id, string DeviceLabel, DateTimeOffset CreatedAt);
