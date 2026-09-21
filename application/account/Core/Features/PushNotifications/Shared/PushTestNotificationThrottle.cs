using Microsoft.Extensions.Caching.Memory;
using SharedKernel.Domain;

namespace Account.Features.PushNotifications.Shared;

/// <summary>
///     How often one user may have this deployment send them a test notification. One call sends to every device that user
///     has subscribed, so without a limit a single account decides how many requests this deployment makes to the push
///     services, and a push service that starts refusing is refusing for every user of this deployment and not only for
///     the one who caused it. The allowance is per user and is spent whether the send succeeded or not, because the
///     outbound requests were made either way.
///     The state is this process's, not the database's: a deployment on several instances therefore allows one call per
///     instance in the interval, which still bounds the requests to the number of instances times the devices of one user.
/// </summary>
public sealed class PushTestNotificationThrottle : IDisposable
{
    private readonly MemoryCache _lastSendPerUser = new(new MemoryCacheOptions());

    private readonly Lock _syncLock = new();

    public void Dispose()
    {
        _lastSendPerUser.Dispose();
    }

    /// <summary>Whether this user may send now; a false answer means the previous send is still inside the interval.</summary>
    public bool TryBeginSend(UserId userId)
    {
        lock (_syncLock)
        {
            if (_lastSendPerUser.TryGetValue(userId.Value, out _)) return false;

            _lastSendPerUser.Set(userId.Value, true, PushNotificationPolicy.TestNotificationInterval);
            return true;
        }
    }

    /// <summary>Forgets every allowance, so a test that shares a host with other tests starts from an unspent one.</summary>
    public void Clear()
    {
        lock (_syncLock)
        {
            _lastSendPerUser.Clear();
        }
    }
}
