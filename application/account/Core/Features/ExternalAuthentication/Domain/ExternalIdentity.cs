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

    /// <summary>
    ///     The level of assurance of the most recent successful identity verification, or null when the identity has
    ///     never been verified. Only the verification callback writes this and the three fields below.
    /// </summary>
    public IdentityAssuranceLevel? AssuranceLevel { get; private set; }

    /// <summary>
    ///     When this system recorded the verification.
    /// </summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>
    ///     When the identity provider reports the person actually authenticated. It answers a different question from
    ///     <see cref="VerifiedAt" /> and is what the freshness of a verification is measured against, because a
    ///     provider replaying a cached session would otherwise produce a recent row from an old authentication.
    /// </summary>
    public DateTimeOffset? AuthenticatedAt { get; private set; }

    /// <summary>
    ///     The flow that produced the verification. The broker retains nothing of its own, so the evidence trail has to
    ///     point at a row this system owns: from here to the external login row, which holds the provider, the flow
    ///     type, the result and the timestamps, and on to the telemetry event.
    /// </summary>
    public ExternalLoginId? VerifiedByExternalLoginId { get; private set; }

    public UserId UserId { get; }

    public ExternalProviderType Provider { get; }

    /// <summary>
    ///     The durable provider-specific lookup key, and the only provider-supplied value the unique index on
    ///     (provider, provider_user_id, tenant_id) and the login lookup use. Per provider: Google uses the <c>sub</c> claim;
    ///     Entra uses <c>{tid}:{oid}</c> in lower case with a colon separator, because an <c>oid</c> is unique only
    ///     within its directory; MitID through Idura uses the MitID <c>uuid</c>, which is broker independent. The
    ///     Entra <c>tid</c> is the directory the identity came from and is never the Platform TenantId, which scopes
    ///     the row to a Platform tenant.
    /// </summary>
    public string ProviderUserId { get; private init; }

    /// <summary>
    ///     What this identity may be used for, stored as the flag names joined by a comma. Only a row with Login
    ///     resolves a returning user at login.
    /// </summary>
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

    /// <summary>
    ///     Creates an identity that has been verified but may not be used to log in. The Login capability is granted by
    ///     a successful login, never by a verification, so the database never asserts a permission the product has not
    ///     granted.
    /// </summary>
    public static ExternalIdentity CreateForVerification(
        TenantId tenantId,
        UserId userId,
        ExternalProviderType provider,
        string providerUserId,
        string issuer,
        string subject,
        IdentityAssuranceLevel assuranceLevel,
        DateTimeOffset verifiedAt,
        DateTimeOffset authenticatedAt,
        ExternalLoginId verifiedByExternalLoginId
    )
    {
        var externalIdentity = new ExternalIdentity(tenantId, userId, provider, providerUserId, ExternalIdentityCapabilities.None, issuer, subject);
        externalIdentity.RecordVerification(assuranceLevel, verifiedAt, authenticatedAt, verifiedByExternalLoginId);
        return externalIdentity;
    }

    /// <summary>
    ///     Records the outcome of a successful identity verification, replacing any earlier one. Re-verifying with the
    ///     same identity is how a verification is kept fresh.
    /// </summary>
    public void RecordVerification(IdentityAssuranceLevel assuranceLevel, DateTimeOffset verifiedAt, DateTimeOffset authenticatedAt, ExternalLoginId verifiedByExternalLoginId)
    {
        AssuranceLevel = assuranceLevel;
        VerifiedAt = verifiedAt;
        AuthenticatedAt = authenticatedAt;
        VerifiedByExternalLoginId = verifiedByExternalLoginId;
        Capabilities |= ExternalIdentityCapabilities.Verification;

        AddDomainEvent(new UserIdentityVerifiedEvent(TenantId, UserId, Provider, assuranceLevel));
    }

    /// <summary>
    ///     Clears the verification evidence, leaving the identity in place. Used by the back office when a person has
    ///     bound the wrong identity, because a different provider user id is otherwise refused forever.
    /// </summary>
    public void RevokeVerification()
    {
        AssuranceLevel = null;
        VerifiedAt = null;
        AuthenticatedAt = null;
        VerifiedByExternalLoginId = null;
        Capabilities &= ~ExternalIdentityCapabilities.Verification;
    }

    public void AddLoginCapability()
    {
        Capabilities |= ExternalIdentityCapabilities.Login;
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
