using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain;
using SharedKernel.EntityFramework;

namespace Account.Features.ExternalAuthentication.Domain;

public sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.MapStronglyTypedId<ExternalLogin, ExternalLoginId, string>(el => el.Id);
        builder.MapStronglyTypedNullableId<ExternalLogin, UserId, string>(el => el.UserId);
        builder.MapStronglyTypedNullableLongId<ExternalLogin, TenantId>(el => el.TenantId);
    }
}
