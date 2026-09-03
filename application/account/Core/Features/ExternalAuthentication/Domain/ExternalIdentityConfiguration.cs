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
    }
}
