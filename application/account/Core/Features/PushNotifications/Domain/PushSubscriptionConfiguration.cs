using Account.Features.Users.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain;
using SharedKernel.EntityFramework;

namespace Account.Features.PushNotifications.Domain;

public sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.MapStronglyTypedUuid<PushSubscription, PushSubscriptionId>(p => p.Id);
        builder.MapStronglyTypedLongId<PushSubscription, TenantId>(p => p.TenantId);
        builder.MapStronglyTypedUuid<PushSubscription, UserId>(p => p.UserId);

        // The cap on devices per user is this index and not the count the handler reads: the count can be stale by the
        // time the row is inserted, and this cannot. Declared here as well as in the migration because the test schema is
        // built from this model.
        builder.HasIndex(p => new { p.UserId, p.DeviceSlot }).IsUnique();

        // Deleting the user takes the device subscriptions with it; EF needs the relationship to cascade at runtime
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
