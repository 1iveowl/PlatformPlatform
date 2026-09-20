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

        // Deleting the user takes the device subscriptions with it; EF needs the relationship to cascade at runtime
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
