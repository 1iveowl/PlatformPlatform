using Account.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain;
using SharedKernel.EntityFramework;
using SharedKernel.Persistence;

namespace Account.Features.ExternalAuthentication.Domain;

public interface IExternalIdentityRepository : ICrudRepository<ExternalIdentity, ExternalIdentityId>
{
    /// <summary>
    ///     Retrieves every external identity for the given provider and provider user id without applying the tenant
    ///     query filter. The external login callback runs without a tenant context and picks the tenant afterwards, the
    ///     same way the cross-tenant email lookup on users does.
    /// </summary>
    Task<ExternalIdentity[]> GetByProviderUserIdUnfilteredAsync(ExternalProviderType provider, string providerUserId, CancellationToken cancellationToken);

    /// <summary>
    ///     Retrieves the external identity a user holds for the given provider without applying the tenant query
    ///     filter. The external login callback runs without a tenant context, so the user's own tenant is not the
    ///     current one. A user holds at most one identity per provider, enforced by the unique index on (user_id,
    ///     provider).
    /// </summary>
    Task<ExternalIdentity?> GetByUserIdAndProviderUnfilteredAsync(UserId userId, ExternalProviderType provider, CancellationToken cancellationToken);

    /// <summary>
    ///     Retrieves every external identity a user holds without applying the tenant query filter. The back office
    ///     runs without a tenant context, and a user's own identities are already scoped by the user id.
    /// </summary>
    Task<ExternalIdentity[]> GetByUserIdUnfilteredAsync(UserId userId, CancellationToken cancellationToken);
}

public sealed class ExternalIdentityRepository(AccountDbContext accountDbContext)
    : RepositoryBase<ExternalIdentity, ExternalIdentityId>(accountDbContext), IExternalIdentityRepository
{
    public async Task<ExternalIdentity[]> GetByProviderUserIdUnfilteredAsync(ExternalProviderType provider, string providerUserId, CancellationToken cancellationToken)
    {
        return await DbSet
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Where(ei => ei.Provider == provider && ei.ProviderUserId == providerUserId)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<ExternalIdentity?> GetByUserIdAndProviderUnfilteredAsync(UserId userId, ExternalProviderType provider, CancellationToken cancellationToken)
    {
        return await DbSet
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Where(ei => ei.UserId == userId && ei.Provider == provider)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ExternalIdentity[]> GetByUserIdUnfilteredAsync(UserId userId, CancellationToken cancellationToken)
    {
        return await DbSet
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Where(ei => ei.UserId == userId)
            .ToArrayAsync(cancellationToken);
    }
}
