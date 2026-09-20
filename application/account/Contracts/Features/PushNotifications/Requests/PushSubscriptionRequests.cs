using JetBrains.Annotations;

namespace Account.Features.PushNotifications.Requests;

// What the browser's PushSubscription holds: the push service address to deliver to and the two keys the payload is
// encrypted with. The device label is what the user reads in the list and carries no identity beyond the browser and
// platform it was made on, and the application path is where this client opens when a notification is clicked.
[PublicAPI]
public sealed record SavePushSubscriptionCommand(string Endpoint, string PublicKey, string AuthSecret, string DeviceLabel, string ApplicationPath);
