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

        // Identities belong to their user and are removed with it when the user is hard-deleted
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ei => ei.UserId)
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
