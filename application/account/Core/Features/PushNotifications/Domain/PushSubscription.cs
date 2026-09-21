using SharedKernel.Domain;

namespace Account.Features.PushNotifications.Domain;

/// <summary>
///     One browser's Web Push subscription, owned by the user who made it. The endpoint is the push service address the
///     notification is delivered to; the two keys encrypt the payload for that browser and no other. A subscription the
///     push service no longer knows is deleted rather than kept, so the table holds only addresses that can still be
///     delivered to.
/// </summary>
public sealed class PushSubscription : AggregateRoot<PushSubscriptionId>, ITenantScopedEntity
{
    private PushSubscription(TenantId tenantId, UserId userId, int deviceSlot, string endpoint, string publicKey, string authSecret, string deviceLabel, string applicationPath)
        : base(PushSubscriptionId.NewId())
    {
        TenantId = tenantId;
        UserId = userId;
        DeviceSlot = deviceSlot;
        Endpoint = endpoint;
        PublicKey = publicKey;
        AuthSecret = authSecret;
        DeviceLabel = deviceLabel;
        ApplicationPath = applicationPath;
    }

    public UserId UserId { get; private init; }

    /// <summary>
    ///     Which of this user's device places this subscription holds, from zero to one below the maximum. A unique index
    ///     on (user_id, device_slot) is what caps the number of devices: the slot is chosen from the ones the user is not
    ///     holding, so two saves racing at the limit choose the same one and the database admits only the first.
    /// </summary>
    public int DeviceSlot { get; private init; }

    public string Endpoint { get; private init; }

    public string PublicKey { get; private set; }

    public string AuthSecret { get; private set; }

    public string DeviceLabel { get; private set; }

    /// <summary>
    ///     Where the client that made this subscription opens, as a rooted path of this origin. One account API serves
    ///     more than one client, so the destination belongs to the subscription rather than to the sender, and the
    ///     service worker checks it against its own scope again before opening anything.
    /// </summary>
    public string ApplicationPath { get; private set; }

    public TenantId TenantId { get; }

    public static PushSubscription Create(TenantId tenantId, UserId userId, int deviceSlot, string endpoint, string publicKey, string authSecret, string deviceLabel, string applicationPath)
    {
        return new PushSubscription(tenantId, userId, deviceSlot, endpoint, publicKey, authSecret, deviceLabel, applicationPath);
    }

    // The same browser resubscribing keeps its row: the endpoint is the identity, the keys and the rest are what change
    public void Update(string publicKey, string authSecret, string deviceLabel, string applicationPath)
    {
        PublicKey = publicKey;
        AuthSecret = authSecret;
        DeviceLabel = deviceLabel;
        ApplicationPath = applicationPath;
    }
}
