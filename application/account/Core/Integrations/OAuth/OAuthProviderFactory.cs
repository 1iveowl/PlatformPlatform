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

    // The value the AppHost passes for the Entra client id and client secret while those parameters are left disabled
    private const string NotConfiguredPlaceholder = "not-configured";

    private readonly bool _allowMockProvider = GetAllowMockProvider(configuration);

    private readonly bool _isEntraConfigured = IsEntraConfigured(configuration);

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

        // Resolved before the keyed service so an unconfigured Entra provider is never constructed
        if (!useMock && providerType == ExternalProviderType.Entra && !_isEntraConfigured)
        {
            return null;
        }

        var serviceKey = useMock
            ? $"mock-{providerType.ToString().ToLowerInvariant()}"
            : providerType.ToString().ToLowerInvariant();

        return serviceProvider.GetKeyedService<IOAuthProvider>(serviceKey);
    }

    // Both values are required, because the token exchange sends the client secret in a form body where a missing
    // value throws instead of failing cleanly, which is exactly what this guard exists to prevent
    private static bool IsEntraConfigured(IConfiguration configuration)
    {
        return IsConfigured(configuration["OAuth:Entra:ClientId"]) && IsConfigured(configuration["OAuth:Entra:ClientSecret"]);
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
