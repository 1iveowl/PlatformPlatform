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

    // The value the AppHost passes for OAuth__Entra__ClientId while the Entra parameters are left disabled
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

    private static bool IsEntraConfigured(IConfiguration configuration)
    {
        var clientId = configuration["OAuth:Entra:ClientId"];
        return !string.IsNullOrWhiteSpace(clientId) && clientId != NotConfiguredPlaceholder;
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
