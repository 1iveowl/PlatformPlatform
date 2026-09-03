using JetBrains.Annotations;
using SharedKernel.Domain;
using SharedKernel.StronglyTypedIds;

namespace Account.Features.ExternalAuthentication.Domain;

public sealed class ExternalIdentity : AggregateRoot<ExternalIdentityId>, ITenantScopedEntity
{
    private ExternalIdentity(TenantId tenantId, UserId userId, ExternalProviderType provider, string providerUserId, ExternalIdentityCapabilities capabilities, string issuer, string subject)
        : base(ExternalIdentityId.NewId())
    {
        TenantId = tenantId;
        UserId = userId;
        Provider = provider;
        ProviderUserId = providerUserId;
        Capabilities = capabilities;
        Issuer = issuer;
        Subject = subject;
    }

    public UserId UserId { get; private init; }

    public ExternalProviderType Provider { get; private init; }

    /// <summary>
    ///     The durable provider-specific lookup key, and the only member the unique index on (provider,
    ///     provider_user_id, tenant_id) and the login lookup use. Per provider: Google uses the <c>sub</c> claim;
    ///     Entra uses <c>{tid}:{oid}</c> in lower case with a colon separator, because an <c>oid</c> is unique only
    ///     within its directory; MitID through Idura uses the MitID <c>uuid</c>, which is broker independent. The
    ///     Entra <c>tid</c> is the directory the identity came from and is never the Platform TenantId, which scopes
    ///     the row to a Platform tenant.
    /// </summary>
    public string ProviderUserId { get; private init; }

    public ExternalIdentityCapabilities Capabilities { get; private set; }

    /// <summary>
    ///     The <c>iss</c> claim of the validated token that created the row, kept for audit and never used for
    ///     lookup. Per provider: Google <c>https://accounts.google.com</c>, canonicalized because Google emits the
    ///     issuer in two forms; Entra <c>https://login.microsoftonline.com/{tid}/v2.0</c> for the exact directory
    ///     that issued the token; MitID through Idura the issuer of the customer's broker domain.
    /// </summary>
    public string Issuer { get; private init; }

    /// <summary>
    ///     The <c>sub</c> claim of the validated token that created the row, kept for audit and never used for
    ///     lookup. It equals <see cref="ProviderUserId" /> only for Google. For Entra the subject is pairwise per
    ///     application and directory, and for MitID through Idura it is persistent per Idura Verify tenant, so
    ///     neither is a key.
    /// </summary>
    public string Subject { get; private init; }

    public TenantId TenantId { get; }

    public static ExternalIdentity Create(TenantId tenantId, UserId userId, ExternalProviderType provider, string providerUserId, string issuer, string subject)
    {
        return new ExternalIdentity(tenantId, userId, provider, providerUserId, ExternalIdentityCapabilities.Login, issuer, subject);
    }

    public void AddCapability(ExternalIdentityCapabilities capability)
    {
        Capabilities |= capability;
    }
}

[PublicAPI]
[IdPrefix("exid")]
[JsonConverter(typeof(StronglyTypedIdJsonConverter<string, ExternalIdentityId>))]
public sealed record ExternalIdentityId(string Value) : StronglyTypedUlid<ExternalIdentityId>(Value)
{
    public override string ToString()
    {
        return Value;
    }
}
