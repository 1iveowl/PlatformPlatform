using Account.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain;
using SharedKernel.Persistence;

namespace Account.Features.PushNotifications.Domain;

public interface IPushSubscriptionRepository : ICrudRepository<PushSubscription, PushSubscriptionId>
{
    Task<PushSubscription[]> GetByUserAsync(UserId userId, CancellationToken cancellationToken);

    Task<PushSubscription?> GetByEndpointAsync(UserId userId, string endpoint, CancellationToken cancellationToken);

    Task<int> CountByUserAsync(UserId userId, CancellationToken cancellationToken);
}

public sealed class PushSubscriptionRepository(AccountDbContext accountDbContext)
    : RepositoryBase<PushSubscription, PushSubscriptionId>(accountDbContext), IPushSubscriptionRepository
{
    // Oldest first, so the list the user reads and the order a test notification is sent in are the same every time
    public async Task<PushSubscription[]> GetByUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        return await DbSet.Where(p => p.UserId == userId).OrderBy(p => p.Id).ToArrayAsync(cancellationToken);
    }

    public async Task<PushSubscription?> GetByEndpointAsync(UserId userId, string endpoint, CancellationToken cancellationToken)
    {
        return await DbSet.FirstOrDefaultAsync(p => p.UserId == userId && p.Endpoint == endpoint, cancellationToken);
    }

    public async Task<int> CountByUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        return await DbSet.CountAsync(p => p.UserId == userId, cancellationToken);
    }
}
