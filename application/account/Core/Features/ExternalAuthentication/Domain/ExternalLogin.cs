using System.Security;
using JetBrains.Annotations;
using SharedKernel.Authentication.TokenGeneration;
using SharedKernel.Domain;
using SharedKernel.StronglyTypedIds;

namespace Account.Features.ExternalAuthentication.Domain;

public sealed class ExternalLogin : AggregateRoot<ExternalLoginId>
{
    public const int ValidForSeconds = 300;

    private ExternalLogin(
        ExternalLoginType type,
        ExternalProviderType providerType,
        string codeVerifier,
        string nonce,
        string browserFingerprint,
        bool usedMockProvider,
        UserId? userId,
        TenantId? tenantId,
        SessionId? sessionId
    )
        : base(ExternalLoginId.NewId())
    {
        Type = type;
        ProviderType = providerType;
        CodeVerifier = codeVerifier;
        Nonce = nonce;
        BrowserFingerprint = browserFingerprint;
        UsedMockProvider = usedMockProvider;
        UserId = userId;
        TenantId = tenantId;
        SessionId = sessionId;
    }

    public ExternalLoginType Type { get; private init; }

    public ExternalProviderType ProviderType { get; private init; }

    public string? Email { get; private set; }

    public string CodeVerifier { get; private init; }

    public string Nonce { get; private init; }

    // Stored for forensic analysis only; validation uses the cookie copy for CSRF binding
    public string BrowserFingerprint { get; private init; }

    public ExternalLoginResult? LoginResult { get; private set; }

    /// <summary>
    ///     Whether the flow was started against the mock provider. The mock is selected per request from a cookie, so
    ///     without recording the choice a flow started against the real provider could be completed against the mock,
    ///     where the caller chooses the provider user id. For a verification flow that would be forgery of a national
    ///     identity key, so the callback refuses a flow whose provider selection has changed.
    /// </summary>
    public bool UsedMockProvider { get; private init; }

    /// <summary>
    ///     The account this flow belongs to, and the only way a completed flow can be traced back to a person. A
    ///     verification flow is bound to its user when it starts, because it writes to an account that is known
    ///     before the provider replies. A login or signup flow starts unbound and records the account it resolved
    ///     when it completes, which is the only handle the back office has on a flow whose provider supplied no
    ///     email.
    /// </summary>
    public UserId? UserId { get; private set; }

    /// <summary>
    ///     The tenant of <see cref="UserId" />. Deliberately not an <c>ITenantScopedEntity</c>: the callback arrives
    ///     without a tenant context, and the query filter would then hide every row.
    /// </summary>
    public TenantId? TenantId { get; private set; }

    /// <summary>
    ///     The session that started the flow. Recorded for forensics; the binding check uses <see cref="UserId" />.
    /// </summary>
    public SessionId? SessionId { get; private init; }

    public bool IsConsumed => LoginResult is not null;

    public bool IsExpired(DateTimeOffset now)
    {
        if (CreatedAt > now)
        {
            throw new SecurityException($"ExternalLogin '{Id}' has CreatedAt in the future. Possible data tampering.");
        }

        return now > CreatedAt.AddSeconds(ValidForSeconds);
    }

    public static ExternalLogin Create(
        ExternalLoginType type,
        ExternalProviderType providerType,
        string codeVerifier,
        string nonce,
        string browserFingerprint,
        bool usedMockProvider,
        UserId? userId = null,
        TenantId? tenantId = null,
        SessionId? sessionId = null
    )
    {
        // The provider and flow policy is a domain invariant, so no handler can create a flow the policy forbids
        if (!ExternalAuthenticationPolicy.IsFlowSupported(providerType, type))
        {
            throw new UnreachableException($"Provider '{providerType}' does not support the '{type}' flow.");
        }

        if (ExternalAuthenticationPolicy.RequiresAuthenticatedUser(type) && (userId is null || tenantId is null))
        {
            throw new UnreachableException($"The '{type}' flow must be bound to a user and a tenant.");
        }

        if (!ExternalAuthenticationPolicy.RequiresAuthenticatedUser(type) && userId is not null)
        {
            throw new UnreachableException($"The '{type}' flow must not be bound to a user.");
        }

        return new ExternalLogin(type, providerType, codeVerifier, nonce, browserFingerprint, usedMockProvider, userId, tenantId, sessionId);
    }

    /// <summary>
    ///     Records the account a login or signup flow resolved, so the flow can be found by user as well as by email.
    ///     A verification flow was bound at the start and passes the same user again, which changes nothing; a flow
    ///     that tried to record a different user would mean account resolution had contradicted the binding.
    /// </summary>
    public void RecordResolvedUser(UserId userId, TenantId tenantId)
    {
        if (UserId is not null && UserId != userId)
        {
            throw new UnreachableException($"The external login is bound to user '{UserId}' and cannot resolve to user '{userId}'.");
        }

        UserId = userId;
        TenantId = tenantId;
    }

    public void MarkCompleted(string? email)
    {
        if (LoginResult is not null)
        {
            throw new UnreachableException("The external login has already been completed.");
        }

        Email = email;
        LoginResult = ExternalLoginResult.Success;
    }

    public void MarkFailed(ExternalLoginResult loginResult)
    {
        if (loginResult == ExternalLoginResult.Success)
        {
            throw new UnreachableException("Cannot mark a login as failed with a success result.");
        }

        if (LoginResult is not null)
        {
            throw new UnreachableException("The external login has already been completed.");
        }

        LoginResult = loginResult;
    }
}

[PublicAPI]
[IdPrefix("exlog")]
[JsonConverter(typeof(StronglyTypedIdJsonConverter<string, ExternalLoginId>))]
public sealed record ExternalLoginId(string Value) : StronglyTypedUlid<ExternalLoginId>(Value)
{
    public override string ToString()
    {
        return Value;
    }
}
