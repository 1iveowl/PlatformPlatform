using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Authentication.TokenGeneration;
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
        builder.MapStronglyTypedNullableId<ExternalLogin, SessionId, string>(el => el.SessionId);

        // Deliberately no foreign key to users. A composite foreign key on nullable columns is satisfied under
        // PostgreSQL MATCH SIMPLE whenever either column is null, so it would not enforce that a row's tenant is its
        // user's tenant, and a cascade would delete this table's audit rows when a user is purged. The values come
        // from the execution context, so they are correct by construction at write time.
    }
}
