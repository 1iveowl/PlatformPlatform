using Account.Features.PushNotifications.Domain;
using JetBrains.Annotations;

namespace Account.Features.PushNotifications.Commands;

[PublicAPI]
public sealed record SavePushSubscriptionResponse(PushSubscriptionId PushSubscriptionId);

// What a test notification reached: how many of the caller's subscriptions the push services accepted, and how many were
// forgotten by their push service and therefore deleted
[PublicAPI]
public sealed record SendTestPushNotificationResponse(int Delivered, int Removed);
