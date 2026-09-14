using JetBrains.Annotations;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;

namespace Account.Features.Authentication.Queries;

// What any client needs before it renders: who the caller is, the locale, the public runtime configuration and the
// system-scope feature flags. Independent of how the client renders, and never carries an authentication credential.
[PublicAPI]
public sealed record BootstrapResponse(
    bool IsAuthenticated,
    BootstrapUser? User,
    string Locale,
    IReadOnlyDictionary<string, string> RuntimeConfiguration,
    IReadOnlyDictionary<string, bool> SystemFeatureFlags,
    string AntiforgeryToken
);

// The identity fields of the access token a client displays or branches on. Feature flags are the user's and tenant's
// evaluated flags from the token.
[PublicAPI]
public sealed record BootstrapUser(
    UserId Id,
    TenantId TenantId,
    string? Role,
    string? Email,
    string? FirstName,
    string? LastName,
    string? Title,
    string? AvatarUrl,
    string? TenantName,
    string? TenantLogoUrl,
    string? SubscriptionPlan,
    bool IsInternalUser,
    string[] FeatureFlags
);

// The explicit allowlist of public configuration, shared by every producer of the bootstrap contract so they expose
// the same keys: the public and CDN URLs, the application version and the environment key of each declared
// SystemFeatureFlag. Any other environment variable, including an undeclared PUBLIC_* key, is never read.
[PublicAPI]
public static class BootstrapConfiguration
{
    public const string PublicUrlKey = "PUBLIC_URL";
    public const string CdnUrlKey = "CDN_URL";
    public const string ApplicationVersionKey = "APPLICATION_VERSION";

    private static readonly SystemFeatureFlag[] SystemFlags = FeatureFlags.GetAll().OfType<SystemFeatureFlag>().ToArray();

    public static IReadOnlyDictionary<string, string> CreateRuntimeConfiguration(Func<string, string?> getEnvironmentVariable, string applicationVersion)
    {
        var configuration = new Dictionary<string, string>
        {
            [PublicUrlKey] = getEnvironmentVariable(PublicUrlKey) ?? string.Empty,
            [CdnUrlKey] = getEnvironmentVariable(CdnUrlKey) ?? string.Empty,
            [ApplicationVersionKey] = applicationVersion
        };

        foreach (var flag in SystemFlags)
        {
            configuration[flag.FrontendEnvVar] = getEnvironmentVariable(flag.FrontendEnvVar) ?? "false";
        }

        return configuration;
    }

    public static IReadOnlyDictionary<string, bool> CreateSystemFeatureFlags(Func<string, string?> getEnvironmentVariable)
    {
        return SystemFlags.ToDictionary(flag => flag.Key, flag => bool.TryParse(getEnvironmentVariable(flag.FrontendEnvVar), out var enabled) && enabled);
    }
}
