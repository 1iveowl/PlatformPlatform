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
    ///     What this deployment uses MitID for. Google and Entra need no such setting, because one set of credentials
    ///     serves exactly one purpose there and configuring the provider is the same statement as enabling it. MitID
    ///     verifies and logs in from one client id, so the configuration has to say which of the two is wanted, and
    ///     saying it only to the user interface would leave the endpoint open in a deployment that switched it off.
    /// </summary>
    private readonly Dictionary<ExternalLoginType, bool> _isMitIdFlowEnabled = new()
    {
        [ExternalLoginType.Login] = configuration["OAuth:MitId:LoginEnabled"] == "true",
        [ExternalLoginType.Verification] = configuration["OAuth:MitId:VerificationEnabled"] == "true"
    };

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

    /// <summary>
    ///     Whether this deployment permits the flow at all, which is a separate question from whether the product
    ///     supports it. <see cref="Account.Features.ExternalAuthentication.Domain.ExternalAuthenticationPolicy" />
    ///     answers the second and stays a constant of the code; this answers the first and comes from configuration.
    /// </summary>
    public bool IsFlowEnabled(ExternalProviderType providerType, ExternalLoginType loginType)
    {
        if (providerType != ExternalProviderType.MitId) return true;

        return _isMitIdFlowEnabled.GetValueOrDefault(loginType);
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
