// The host's server-side adapter for the account API's bootstrap contract, used only while prerendering a WebAssembly
// component. It produces the same contract from the same inputs: identity from the claims of the access token the
// gateway derived from the session cookies (the claim names AccessTokenGenerator writes and UserInfo reads), the locale
// from the token or Accept-Language, and configuration through the contract's own allowlist.

using System.Reflection;
using System.Security.Claims;
using Account.Features.Authentication.Queries;
using Blazor.Client.Bootstrap;
using Microsoft.AspNetCore.Antiforgery;
using SharedKernel.Domain;

namespace Blazor.Host.Shell;

public sealed class HostBootstrapSource(IHttpContextAccessor httpContextAccessor, HostShell hostShell, IAntiforgery antiforgery) : IBootstrapSource
{
    private const string FeatureFlagsClaimName = "feature_flags";

    private static readonly string ApplicationVersion =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? string.Empty;

    public Task<BootstrapResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var context = httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No HttpContext.");
        var principal = context.User;
        var isAuthenticated = principal.Identity?.IsAuthenticated == true;

        var response = new BootstrapResponse(
            isAuthenticated,
            isAuthenticated ? CreateUser(principal) : null,
            HostShell.GetLocale(context),
            BootstrapConfiguration.CreateRuntimeConfiguration(Environment.GetEnvironmentVariable, ApplicationVersion),
            BootstrapConfiguration.CreateSystemFeatureFlags(Environment.GetEnvironmentVariable),
            antiforgery.GetAndStoreTokens(context).RequestToken!
        );

        return Task.FromResult(response);
    }

    // Prerendering holds no client transport state: the page's forms carry their own antiforgery pair and the feature flag
    // state is filled in the browser, so nothing is stored and nothing is notified
    public Action Apply(BootstrapResponse? bootstrap)
    {
        return static () => { };
    }

    private BootstrapUser CreateUser(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email);
        return new BootstrapUser(
            new UserId(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
            new TenantId(long.Parse(principal.FindFirstValue("tenant_id")!)),
            principal.FindFirstValue(ClaimTypes.Role),
            email,
            FindOptionalClaim(principal, ClaimTypes.GivenName),
            FindOptionalClaim(principal, ClaimTypes.Surname),
            FindOptionalClaim(principal, "title"),
            FindOptionalClaim(principal, "avatar_url"),
            FindOptionalClaim(principal, "tenant_name"),
            FindOptionalClaim(principal, "tenant_logo_url"),
            FindOptionalClaim(principal, "subscription_plan"),
            email?.EndsWith(hostShell.InternalEmailDomain, StringComparison.OrdinalIgnoreCase) == true,
            (FindOptionalClaim(principal, FeatureFlagsClaimName)?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? []).Distinct().Order(StringComparer.Ordinal).ToArray()
        );
    }

    // The access token carries an empty string for an unset optional claim
    private static string? FindOptionalClaim(ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirstValue(claimType);
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
