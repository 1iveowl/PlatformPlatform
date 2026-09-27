using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace SharedKernel.Authentication.BackOfficeIdentity;

// The back-office identity as the account API's back-office listener forwards it to the Blazor host. The platform
// authentication (or its local mock) in front of the back-office host injects the principal headers, the account API
// authenticates them with its own back-office handler, and the proxy sends the resulting claims and the verdict of the
// admin policy in one header protected by the shared data protection key ring, limited to a short lifetime. The Blazor host
// reads only this header, never the principal headers, so a caller that can reach the Blazor host without passing the
// back-office listener (the app gateway, or anything else in the network) cannot present a back-office identity: it would
// need the key ring. The same key ring already carries the antiforgery tokens between the two processes.
public static class ForwardedBackOfficeIdentity
{
    public const string HeaderName = "X-Back-Office-Identity";

    // The admin verdict of the account API's BackOfficeAdmin policy for this principal, added by the Blazor host to the
    // identity it rebuilds so a page can show the marker; the account API still enforces the policy on every admin call
    public const string AdminClaimType = "back_office_admin";

    // Covers the hop from the back-office listener to the Blazor host; the header is created per request and never leaves the
    // server side, so a longer lifetime would only widen the window for a captured value
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private const string Purpose = "SharedKernel.Authentication.BackOfficeIdentity.ForwardedBackOfficeIdentity.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Protect(IDataProtectionProvider dataProtectionProvider, ClaimsPrincipal principal, bool isAdmin)
    {
        var payload = new ForwardedIdentityPayload(
            principal.Claims.Select(claim => new ForwardedClaim(claim.Type, claim.Value)).ToArray(),
            isAdmin
        );
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return CreateProtector(dataProtectionProvider).Protect(json, Lifetime);
    }

    // Null for a value this key ring did not protect, for another purpose, or longer ago than the lifetime. The claims are
    // recreated with their type and value only, so the identity carries the same name identifier the account API's handler
    // built, and an antiforgery token the Blazor host issues for it validates at the account API.
    public static ClaimsPrincipal? Unprotect(IDataProtectionProvider dataProtectionProvider, string protectedValue, string authenticationType)
    {
        ForwardedIdentityPayload? payload;
        try
        {
            var json = CreateProtector(dataProtectionProvider).Unprotect(protectedValue, out _);
            payload = JsonSerializer.Deserialize<ForwardedIdentityPayload>(json, JsonOptions);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (payload?.Claims is not { Length: > 0 } claims) return null;

        var identityClaims = claims.Select(claim => new Claim(claim.Type, claim.Value)).ToList();
        if (payload.IsAdmin) identityClaims.Add(new Claim(AdminClaimType, "true"));

        var identity = new ClaimsIdentity(identityClaims, authenticationType, ClaimTypes.Name, ClaimTypes.Role);
        return string.IsNullOrWhiteSpace(identity.Name) ? null : new ClaimsPrincipal(identity);
    }

    private static ITimeLimitedDataProtector CreateProtector(IDataProtectionProvider dataProtectionProvider)
    {
        return dataProtectionProvider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
    }

    private sealed record ForwardedIdentityPayload(
        [property: JsonPropertyName("claims")] ForwardedClaim[] Claims,
        [property: JsonPropertyName("isAdmin")] bool IsAdmin
    );

    private sealed record ForwardedClaim(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("value")] string Value
    );
}
