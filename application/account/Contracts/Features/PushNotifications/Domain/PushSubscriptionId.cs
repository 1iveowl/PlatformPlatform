using JetBrains.Annotations;

namespace Account.Features.PushNotifications.Domain;

[PublicAPI]
[IdPrefix("psub")]
[JsonConverter(typeof(StronglyTypedIdJsonConverter<string, PushSubscriptionId>))]
public sealed record PushSubscriptionId(string Value) : StronglyTypedUlid<PushSubscriptionId>(Value)
{
    public override string ToString()
    {
        return Value;
    }
}
