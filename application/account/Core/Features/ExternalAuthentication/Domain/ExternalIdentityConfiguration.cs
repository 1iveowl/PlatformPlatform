using Account.Features.Users.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain;
using SharedKernel.EntityFramework;

namespace Account.Features.ExternalAuthentication.Domain;

public sealed class ExternalIdentityConfiguration : IEntityTypeConfiguration<ExternalIdentity>
{
    public void Configure(EntityTypeBuilder<ExternalIdentity> builder)
    {
        builder.MapStronglyTypedUuid<ExternalIdentity, ExternalIdentityId>(ei => ei.Id);
        builder.MapStronglyTypedLongId<ExternalIdentity, TenantId>(ei => ei.TenantId);
        builder.MapStronglyTypedUuid<ExternalIdentity, UserId>(ei => ei.UserId);

        // Identities belong to their user and are removed with it when the user is hard-deleted. The key is
        // composite because two independent foreign keys, one on tenant_id and one on user_id, permit a row whose
        // tenant names one tenant while its user belongs to another, and this table decides which account a person
        // is logged into. Pointing (tenant_id, user_id) at the user's own (TenantId, Id) makes that row
        // unrepresentable instead of merely unwritten by the current writers. The user keeps Id as its primary
        // key; (TenantId, Id) is an alternate key, which restricts no existing data because Id is already unique.
        builder.HasOne<User>()
            .WithMany()
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .HasForeignKey(ei => new { ei.TenantId, ei.UserId })
            .OnDelete(DeleteBehavior.Cascade);

        // Unique indexes are normally left to the migration, but Entity Framework needs these two at runtime: it
        // orders a delete before an insert that reuses the same unique values only when the model declares the
        // index as unique, and the login handler recycles a soft-deleted user's row that way. Declaring them also
        // puts them in the SQLite schema the tests build from this model. The one remaining non-unique index, on
        // tenant_id, stays migration-only.
        builder.HasIndex(ei => new { ei.UserId, ei.Provider }).IsUnique();
        builder.HasIndex(ei => new { ei.Provider, ei.ProviderUserId, ei.TenantId }).IsUnique();
    }
}
