using Account.Database;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain;
using SharedKernel.EntityFramework;
using SharedKernel.Persistence;

namespace Account.Features.ExternalAuthentication.Domain;

public interface IExternalIdentityRepository : IAppendRepository<ExternalIdentity, ExternalIdentityId>
{
    /// <summary>
    ///     Retrieves every external identity for the given provider and provider user id without applying the tenant
    ///     query filter. The external login callback runs without a tenant context and picks the tenant afterwards, the
    ///     same way the cross-tenant email lookup on users does. Ordered by id so callers can rely on the first match.
    /// </summary>
    Task<ExternalIdentity[]> GetByProviderUserIdUnfilteredAsync(ExternalProviderType provider, string providerUserId, CancellationToken cancellationToken);
}

public sealed class ExternalIdentityRepository(AccountDbContext accountDbContext)
    : RepositoryBase<ExternalIdentity, ExternalIdentityId>(accountDbContext), IExternalIdentityRepository
{
    public async Task<ExternalIdentity[]> GetByProviderUserIdUnfilteredAsync(ExternalProviderType provider, string providerUserId, CancellationToken cancellationToken)
    {
        return await DbSet
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Where(ei => ei.Provider == provider && ei.ProviderUserId == providerUserId)
            .OrderBy(ei => ei.Id)
            .ToArrayAsync(cancellationToken);
    }
}
