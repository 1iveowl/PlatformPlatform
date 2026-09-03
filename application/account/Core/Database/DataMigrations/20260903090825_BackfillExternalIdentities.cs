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
///     tenant, provider and provider user id, so entries that already have such a row are skipped and the migration
///     can run again without duplicating rows. Entries are grouped by tenant, provider and provider user id before
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
            .Select(ei => new { ei.TenantId, ei.Provider, ei.ProviderUserId })
            .ToArrayAsync(cancellationToken);
        // The idempotency check keys on tenant, provider and provider user id, while the table is also unique on
        // (user_id, provider). A table row for a provider under one provider user id plus a jsonb entry for the same
        // provider under another would abort the migration on the insert. It cannot happen: the jsonb store allowed
        // one entry per provider per user, and a deployment creates the table in the same run, so this set starts empty.
        var existingKeys = existingIdentities.Select(ei => (ei.TenantId, ei.Provider, ei.ProviderUserId)).ToHashSet();

        // The jsonb column is mapped through a value converter and cannot be filtered in SQL, so every user is
        // projected and the entries are read in memory. Soft-deleted users are included so a restored user keeps
        // the identity it had.
        var users = await accountDbContext.Set<User>()
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .OrderBy(u => u.Id)
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
