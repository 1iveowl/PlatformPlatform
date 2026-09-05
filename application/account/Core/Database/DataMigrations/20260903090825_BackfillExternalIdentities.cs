using Account.Features.Users.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Database;
using SharedKernel.EntityFramework;
using ExternalIdentity = Account.Features.ExternalAuthentication.Domain.ExternalIdentity;

namespace Account.Database.DataMigrations;

/// <summary>
///     Backfills Google identities without replacing API bindings. A single live legacy holder wins over deleted
///     holders; contested live holders are left for resolution. Conflicts on either unique key are skipped atomically.
/// </summary>
public sealed class BackfillExternalIdentities(AccountDbContext accountDbContext, ILogger<BackfillExternalIdentities> logger) : IDataMigration
{
    // The jsonb column only ever held Google entries, which is the one issuer this migration can derive
    private const string GoogleIssuer = "https://accounts.google.com";

    public string Id => "20260903090825_BackfillExternalIdentities";

    public TimeSpan Timeout => TimeSpan.FromMinutes(10);

    public async Task<string> ExecuteAsync(CancellationToken cancellationToken)
    {
        var existingIdentities = await accountDbContext.Set<ExternalIdentity>()
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Select(ei => new { ei.TenantId, ei.Provider, ei.ProviderUserId, ei.UserId })
            .ToArrayAsync(cancellationToken);
        var existingKeys = existingIdentities.Select(ei => (ei.TenantId, ei.Provider, ei.ProviderUserId)).ToHashSet();
        var existingHolders = existingIdentities.Select(ei => (ei.UserId, ei.Provider)).ToHashSet();

        // The value-converted JSON cannot be filtered through LINQ. Keep the projection bounded to legacy holders,
        // including deleted users so restoration preserves their identity.
        var users = await accountDbContext.Set<User>()
            .FromSqlRaw("SELECT * FROM users WHERE external_identities <> '[]'")
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .Select(u => new { u.Id, u.TenantId, u.DeletedAt, u.ExternalIdentities })
            .ToArrayAsync(cancellationToken);

        // The deprecated jsonb store allowed one entry per provider per user, so a user holds at most one entry in
        // each group
        var identityGroups = users
            .SelectMany(u => u.ExternalIdentities.Select(ei => new { u.Id, u.TenantId, u.DeletedAt, ei.Provider, ei.ProviderUserId }))
            .GroupBy(entry => (entry.TenantId, entry.Provider, entry.ProviderUserId));

        var insertedCount = 0;
        var alreadyMigratedCount = 0;
        var preferredLiveUserCount = 0;
        var contestedCount = 0;
        foreach (var identityGroup in identityGroups)
        {
            var (tenantId, provider, providerUserId) = identityGroup.Key;
            if (existingKeys.Contains(identityGroup.Key))
            {
                alreadyMigratedCount++;
                continue;
            }

            var holders = identityGroup.ToArray();
            var liveHolders = holders.Where(entry => entry.DeletedAt is null).ToArray();
            if (liveHolders.Length > 1)
            {
                logger.LogWarning(
                    "Skipped external identity '{ProviderUserId}' for provider '{Provider}' in tenant '{TenantId}' because {LiveUserCount} live users hold it: {LiveUserIds}",
                    providerUserId, provider, tenantId, liveHolders.Length, string.Join(", ", liveHolders.Select(entry => entry.Id))
                );
                contestedCount++;
                continue;
            }

            var holder = liveHolders.SingleOrDefault() ?? holders.OrderBy(entry => entry.Id.Value).First();
            if (existingHolders.Contains((holder.Id, provider)))
            {
                alreadyMigratedCount++;
                continue;
            }

            var externalIdentity = ExternalIdentity.Create(tenantId, holder.Id, provider, providerUserId, GoogleIssuer, providerUserId);
            var inserted = await accountDbContext.Database.ExecuteSqlInterpolatedAsync($"""
                                                                                        INSERT INTO external_identities (tenant_id, id, user_id, created_at, provider, provider_user_id, capabilities, issuer, subject)
                                                                                        VALUES ({tenantId.Value}, {externalIdentity.Id.Value}, {holder.Id.Value}, {externalIdentity.CreatedAt},
                                                                                                {provider.ToString()}, {providerUserId}, {externalIdentity.Capabilities.ToString()}, {GoogleIssuer}, {providerUserId})
                                                                                        ON CONFLICT DO NOTHING;
                                                                                        """, cancellationToken
            );
            if (inserted == 0)
            {
                alreadyMigratedCount++;
                continue;
            }

            insertedCount++;
            if (liveHolders.Length == 1 && holders.Length > 1) preferredLiveUserCount++;
        }

        await accountDbContext.SaveChangesAsync(cancellationToken);

        return $"Inserted {insertedCount} external identities, skipped {alreadyMigratedCount} that already had a row, gave {preferredLiveUserCount} to a live user over a soft-deleted one and skipped {contestedCount} contested between live users in the same tenant";
    }
}
