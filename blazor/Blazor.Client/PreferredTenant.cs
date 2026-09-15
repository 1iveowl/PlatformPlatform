using SharedKernel.Domain;

namespace Blazor.Client;

// The tenant a visitor last chose, kept in a cookie so the static login verification page can send it as the preferred
// tenant when the login completes. It is an untrusted convenience hint, never identity or authorization: the account API
// only honours it when the user has an active membership in that tenant, and otherwise picks the first one. The cookie
// holds a tenant id and nothing else; no token is ever kept in browser storage. It is not HttpOnly, so the WebAssembly
// client can write it after a tenant switch.
public static class PreferredTenant
{
    public const string CookieName = "preferred-tenant";

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(365);

    public static string Format(TenantId tenantId)
    {
        return tenantId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static TenantId? Parse(string? cookieValue)
    {
        return long.TryParse(cookieValue, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0
            ? new TenantId(value)
            : null;
    }
}
