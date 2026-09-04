using Account.Features.ExternalAuthentication.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Configuration;

namespace Account.Integrations.OAuth;

public sealed class OAuthProviderFactory(IServiceProvider serviceProvider, IConfiguration configuration)
{
    public const string UseMockProviderCookieName = "__Test_Use_Mock_Provider";
    public const string MockEmailDomain = "@mock.localhost";

    // The value the AppHost passes for a provider's settings while its parameters are left disabled
    private const string NotConfiguredPlaceholder = "not-configured";

    private readonly bool _allowMockProvider = GetAllowMockProvider(configuration);

    /// <summary>
    ///     Which providers have enough configuration to be constructed at all. Google is absent on purpose: it predates
    ///     this guard and throws from its own field initializer when its section is missing. Every provider added since
    ///     is resolved through here first, so an unconfigured one is refused rather than constructed.
    /// </summary>
    private readonly Dictionary<ExternalProviderType, bool> _isProviderConfigured = new()
    {
        [ExternalProviderType.Entra] = IsConfigured(configuration, "OAuth:Entra:ClientId", "OAuth:Entra:ClientSecret"),
        [ExternalProviderType.MitId] = IsConfigured(configuration, "OAuth:MitId:Domain", "OAuth:MitId:ClientId", "OAuth:MitId:ClientSecret")
    };

    public bool ShouldUseMockProvider(HttpContext httpContext)
    {
        if (!_allowMockProvider)
        {
            return false;
        }

        return httpContext.Request.Cookies.ContainsKey(UseMockProviderCookieName);
    }

    public IOAuthProvider? GetProvider(ExternalProviderType providerType, bool useMock)
    {
        if (useMock && !_allowMockProvider)
        {
            return null;
        }

        // Resolved before the keyed service so an unconfigured provider is never constructed
        if (!useMock && _isProviderConfigured.TryGetValue(providerType, out var isConfigured) && !isConfigured)
        {
            return null;
        }

        var serviceKey = useMock
            ? $"mock-{providerType.ToString().ToLowerInvariant()}"
            : providerType.ToString().ToLowerInvariant();

        return serviceProvider.GetKeyedService<IOAuthProvider>(serviceKey);
    }

    // Every value is required, because the token exchange sends the client secret in a form body where a missing
    // value throws instead of failing cleanly, which is exactly what this guard exists to prevent
    private static bool IsConfigured(IConfiguration configuration, params string[] configurationKeys)
    {
        return configurationKeys.All(configurationKey => IsConfigured(configuration[configurationKey]));
    }

    private static bool IsConfigured(string? configurationValue)
    {
        return !string.IsNullOrWhiteSpace(configurationValue) && configurationValue != NotConfiguredPlaceholder;
    }

    private static bool GetAllowMockProvider(IConfiguration configuration)
    {
        var allowMockProvider = configuration.GetValue<bool>("OAuth:AllowMockProvider");

        if (allowMockProvider && SharedInfrastructureConfiguration.IsRunningInAzure)
        {
            throw new InvalidOperationException("Mock OAuth provider cannot be enabled in Azure environments.");
        }

        return allowMockProvider;
    }
}
