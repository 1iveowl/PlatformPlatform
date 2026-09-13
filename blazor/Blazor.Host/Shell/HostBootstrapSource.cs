// Spike code (Blazor edition, stage B2): the host's implementation of the bootstrap contract. It serves the bootstrap
// endpoint and prerendering of WebAssembly components, and reads everything per request: identity from the bearer token
// the gateway derived from the session cookies, locale from the token or Accept-Language, configuration from the process.

using System.Security.Claims;
using Blazor.Client.Bootstrap;
using Microsoft.AspNetCore.Antiforgery;

namespace Blazor.Host.Shell;

public sealed class HostBootstrapSource(IHttpContextAccessor httpContextAccessor, HostShell hostShell, IAntiforgery antiforgery) : IBootstrapSource
{
    // The SystemFeatureFlag environment variables declared in application/shared-kernel/SharedKernel/FeatureFlags/FeatureFlags.cs
    private static readonly string[] SystemFeatureFlagKeys =
    [
        "PUBLIC_GOOGLE_OAUTH_ENABLED", "PUBLIC_ENTRA_OAUTH_ENABLED", "PUBLIC_MITID_VERIFICATION_ENABLED", "PUBLIC_MITID_LOGIN_ENABLED", "PUBLIC_SUBSCRIPTION_ENABLED"
    ];

    public Task<BootstrapResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var context = httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No HttpContext.");
        var user = context.User;
        var isAuthenticated = user.Identity?.IsAuthenticated == true;

        var bootstrapUser = isAuthenticated
            ? new BootstrapUser(
                user.FindFirstValue(ClaimTypes.NameIdentifier),
                user.FindFirstValue(ClaimTypes.Email),
                user.FindFirstValue("tenant_id"),
                user.FindFirstValue("tenant_name"),
                user.FindFirstValue(ClaimTypes.Role),
                user.FindFirstValue("session_id")
            )
            : null;

        var flags = SystemFeatureFlagKeys.ToDictionary(key => key, key => bool.TryParse(Environment.GetEnvironmentVariable(key), out var enabled) && enabled);
        var response = new BootstrapResponse(
            isAuthenticated,
            bootstrapUser,
            HostShell.GetLocale(context),
            hostShell.RuntimeEnvironment,
            flags,
            antiforgery.GetAndStoreTokens(context).RequestToken!,
            "host"
        );

        return Task.FromResult(response);
    }
}
