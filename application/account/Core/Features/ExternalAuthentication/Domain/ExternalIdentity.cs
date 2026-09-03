using JetBrains.Annotations;
using SharedKernel.Domain;
using SharedKernel.StronglyTypedIds;

namespace Account.Features.ExternalAuthentication.Domain;

public sealed class ExternalIdentity : AggregateRoot<ExternalIdentityId>, ITenantScopedEntity
{
    private const string GoogleIssuer = "https://accounts.google.com";

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

    public string ProviderUserId { get; private init; }

    public ExternalIdentityCapabilities Capabilities { get; private init; }

    // AssuranceLevel, VerifiedAt and EvidenceReference are reserved for identity verification. Login identities
    // never set them, so only Entity Framework materializes them.
    [UsedImplicitly]
    public ExternalIdentityAssuranceLevel? AssuranceLevel { get; private set; }

    [UsedImplicitly]
    public DateTimeOffset? VerifiedAt { get; private set; }

    public string Issuer { get; private init; }

    public string Subject { get; private init; }

    [UsedImplicitly]
    public string? EvidenceReference { get; private set; }

    public TenantId TenantId { get; }

    public static ExternalIdentity Create(TenantId tenantId, UserId userId, ExternalProviderType provider, string providerUserId)
    {
        var issuer = provider switch
        {
            ExternalProviderType.Google => GoogleIssuer,
            _ => throw new UnreachableException($"No issuer is known for provider '{provider}'.")
        };

        // Google identifies a user by the sub claim, which is also the provider user id
        return new ExternalIdentity(tenantId, userId, provider, providerUserId, ExternalIdentityCapabilities.Login, issuer, providerUserId);
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
