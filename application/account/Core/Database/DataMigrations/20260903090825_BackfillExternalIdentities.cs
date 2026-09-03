using Account.Features.Users.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Database;
using SharedKernel.EntityFramework;
using ExternalIdentity = Account.Features.ExternalAuthentication.Domain.ExternalIdentity;

namespace Account.Database.DataMigrations;

/// <summary>
///     Copies the identities stored in the users.external_identities jsonb column into the external_identities table.
///     Every jsonb entry becomes a login identity with the issuer and subject derived from its provider. The table is
///     unique on tenant, provider and provider user id, so entries that already have such a row are skipped and the
///     migration can run again without duplicating rows. When several users in one tenant carry the same identity, for
///     example a soft-deleted user and the re-invited user who linked the same account, the live user with the lowest
///     id gets the row and the others are skipped.
/// </summary>
public sealed class BackfillExternalIdentities(AccountDbContext accountDbContext) : IDataMigration
{
    public string Id => "20260903090825_BackfillExternalIdentities";

    public TimeSpan Timeout => TimeSpan.FromMinutes(10);

    public async Task<string> ExecuteAsync(CancellationToken cancellationToken)
    {
        var existingIdentities = await accountDbContext.Set<ExternalIdentity>()
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .Select(ei => new { ei.TenantId, ei.Provider, ei.ProviderUserId })
            .ToArrayAsync(cancellationToken);
        var existingKeys = existingIdentities.Select(ei => (ei.TenantId, ei.Provider, ei.ProviderUserId)).ToHashSet();
        var claimedKeys = existingKeys.ToHashSet();

        // The jsonb column is mapped through a value converter and cannot be filtered in SQL, so every user is
        // projected and the entries are read in memory. Soft-deleted users are included so a restored user keeps
        // the identity it had, but live users are processed first so they win a shared key.
        var users = await accountDbContext.Set<User>()
            .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.TenantId, u.DeletedAt, u.ExternalIdentities })
            .ToArrayAsync(cancellationToken);
        var liveUsersFirst = users.OrderBy(u => u.DeletedAt is not null).ToArray();

        var insertedCount = 0;
        var alreadyMigratedCount = 0;
        var claimedByOtherUserCount = 0;
        foreach (var user in liveUsersFirst)
        {
            foreach (var legacyIdentity in user.ExternalIdentities)
            {
                var key = (user.TenantId, legacyIdentity.Provider, legacyIdentity.ProviderUserId);
                if (!claimedKeys.Add(key))
                {
                    if (existingKeys.Contains(key))
                    {
                        alreadyMigratedCount++;
                    }
                    else
                    {
                        claimedByOtherUserCount++;
                    }

                    continue;
                }

                var externalIdentity = ExternalIdentity.Create(user.TenantId, user.Id, legacyIdentity.Provider, legacyIdentity.ProviderUserId);
                await accountDbContext.Set<ExternalIdentity>().AddAsync(externalIdentity, cancellationToken);
                insertedCount++;
            }
        }

        await accountDbContext.SaveChangesAsync(cancellationToken);

        return $"Inserted {insertedCount} external identities, skipped {alreadyMigratedCount} that already had a row and {claimedByOtherUserCount} claimed by another user in the same tenant";
    }
}
