using Account.Features.Users.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Database;
using SharedKernel.EntityFramework;
using ExternalIdentity = Account.Features.ExternalAuthentication.Domain.ExternalIdentity;

namespace Account.Database.DataMigrations;

/// <summary>
///     Copies the identities stored in the users.external_identities jsonb column into the external_identities table.
///     Every jsonb entry becomes a login identity with the canonical Google issuer and the provider user id as
///     subject, which is all the column can yield and matches what the token would have said. The table is unique on
///     tenant, provider and provider user id and on user and provider, so an entry whose key or whose holder already
///     has a row is skipped and the migration can run again without duplicating rows. Entries are grouped by tenant,
///     provider and provider user id before
///     anything is inserted, so every holder of a key is known before it is assigned: a key held by one live user
///     goes to that user even when soft-deleted users hold it too, a key held only by soft-deleted users goes to the
///     soft-deleted user with the lowest id, and a key held by two or more live users in the same tenant gets no row
///     at all and is logged for manual resolution.
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
        // The table is unique on (tenant, provider, provider user id) and on (user, provider), and a row can exist
        // under either before this runs: a login on the new API between the schema migration and this run links
        // the user under the provider user id the token carried, which differs from the jsonb entry when the
        // account changed at the provider. Both keys are skipped rather than inserted, so that login can never
        // abort the migration.
        var existingKeys = existingIdentities.Select(ei => (ei.TenantId, ei.Provider, ei.ProviderUserId)).ToHashSet();
        var existingHolders = existingIdentities.Select(ei => (ei.UserId, ei.Provider)).ToHashSet();

        // The jsonb column is mapped through a value converter, so LINQ cannot look inside it; the one filter that
        // matters, leaving out the users who hold no legacy identity at all, is a plain SQL comparison that
        // PostgreSQL evaluates as jsonb and SQLite as text. Only those users are projected, four columns each, and
        // their entries are grouped in memory because the contested-key policy below needs every holder of a key
        // before it can decide. Soft-deleted users are included so a restored user keeps the identity it had.
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

            if (liveHolders.Length == 1 && holders.Length > 1)
            {
                preferredLiveUserCount++;
            }

            var externalIdentity = ExternalIdentity.Create(tenantId, holder.Id, provider, providerUserId, GoogleIssuer, providerUserId);
            await accountDbContext.Set<ExternalIdentity>().AddAsync(externalIdentity, cancellationToken);
            insertedCount++;
        }

        await accountDbContext.SaveChangesAsync(cancellationToken);

        return $"Inserted {insertedCount} external identities, skipped {alreadyMigratedCount} that already had a row, gave {preferredLiveUserCount} to a live user over a soft-deleted one and skipped {contestedCount} contested between live users in the same tenant";
    }
}
