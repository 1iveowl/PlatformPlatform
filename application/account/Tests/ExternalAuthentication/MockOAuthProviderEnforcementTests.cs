using Account.Features.ExternalAuthentication.Domain;
using Account.Integrations.OAuth;
using Account.Integrations.OAuth.Mock;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class MockOAuthProviderEnforcementTests
{
    private const string ConfiguredClientId = "11111111-1111-1111-1111-111111111111";
    private const string ConfiguredClientSecret = "entra-client-secret";
    private const string NotConfiguredPlaceholder = "not-configured";

    [Fact]
    public void MockEmail_ShouldEndWithMockLocalhostDomain()
    {
        // Assert
        MockOAuthProvider.MockEmail.Should().EndWith(OAuthProviderFactory.MockEmailDomain);
    }

    [Fact]
    public void ShouldUseMockProvider_WhenMockProviderDisabled_ShouldReturnFalse()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OAuth:AllowMockProvider"] = "false" })
            .Build();
        var factory = new OAuthProviderFactory(new ServiceCollection().BuildServiceProvider(), configuration);
        var httpContext = new DefaultHttpContext();

        // Act
        var result = factory.ShouldUseMockProvider(httpContext);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldUseMockProvider_WhenMockProviderEnabledButNoCookie_ShouldReturnFalse()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OAuth:AllowMockProvider"] = "true" })
            .Build();
        var factory = new OAuthProviderFactory(new ServiceCollection().BuildServiceProvider(), configuration);
        var httpContext = new DefaultHttpContext();

        // Act
        var result = factory.ShouldUseMockProvider(httpContext);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldUseMockProvider_WhenMockProviderEnabledWithCookie_ShouldReturnTrue()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OAuth:AllowMockProvider"] = "true" })
            .Build();
        var factory = new OAuthProviderFactory(new ServiceCollection().BuildServiceProvider(), configuration);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Append("Cookie", $"{OAuthProviderFactory.UseMockProviderCookieName}=true");

        // Act
        var result = factory.ShouldUseMockProvider(httpContext);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetProvider_WhenEntraClientIdIsMissing_ShouldReturnNull()
    {
        // Arrange
        var factory = CreateProviderFactory(null);

        // Act
        var provider = factory.GetProvider(ExternalProviderType.Entra, false);

        // Assert
        provider.Should().BeNull();
    }

    [Fact]
    public void GetProvider_WhenEntraClientIdIsTheAspirePlaceholder_ShouldReturnNull()
    {
        // Arrange
        var factory = CreateProviderFactory(NotConfiguredPlaceholder);

        // Act
        var provider = factory.GetProvider(ExternalProviderType.Entra, false);

        // Assert
        provider.Should().BeNull();
    }

    [Fact]
    public void GetProvider_WhenEntraClientIdIsWhitespace_ShouldReturnNull()
    {
        // Arrange
        var factory = CreateProviderFactory("   ");

        // Act
        var provider = factory.GetProvider(ExternalProviderType.Entra, false);

        // Assert
        provider.Should().BeNull();
    }

    [Fact]
    public void GetProvider_WhenEntraClientIdAndClientSecretAreConfigured_ShouldResolveTheKeyedProvider()
    {
        // Arrange
        var factory = CreateProviderFactory(ConfiguredClientId);

        // Act
        var provider = factory.GetProvider(ExternalProviderType.Entra, false);

        // Assert
        provider.Should().NotBeNull();
    }

    [Fact]
    public void GetProvider_WhenEntraClientSecretIsMissing_ShouldReturnNull()
    {
        // Arrange
        var factory = CreateProviderFactory(ConfiguredClientId, null);

        // Act
        var provider = factory.GetProvider(ExternalProviderType.Entra, false);

        // Assert
        provider.Should().BeNull();
    }

    [Fact]
    public void GetProvider_WhenEntraClientSecretIsWhitespace_ShouldReturnNull()
    {
        // Arrange
        var factory = CreateProviderFactory(ConfiguredClientId, "   ");

        // Act
        var provider = factory.GetProvider(ExternalProviderType.Entra, false);

        // Assert
        provider.Should().BeNull();
    }

    [Fact]
    public void GetProvider_WhenEntraClientSecretIsTheAspirePlaceholder_ShouldReturnNull()
    {
        // Arrange
        var factory = CreateProviderFactory(ConfiguredClientId, NotConfiguredPlaceholder);

        // Act
        var provider = factory.GetProvider(ExternalProviderType.Entra, false);

        // Assert
        provider.Should().BeNull();
    }

    [Fact]
    public void GetProvider_WhenEntraIsNotConfiguredAndMockIsUsed_ShouldResolveTheMockProvider()
    {
        // Arrange
        var factory = CreateProviderFactory(null, null, true);

        // Act
        var provider = factory.GetProvider(ExternalProviderType.Entra, true);

        // Assert
        provider.Should().NotBeNull();
    }

    [Fact]
    public void GetProvider_WhenGoogleClientIdIsMissing_ShouldStillResolveTheKeyedProvider()
    {
        // Arrange
        var factory = CreateProviderFactory(null, null);

        // Act
        var provider = factory.GetProvider(ExternalProviderType.Google, false);

        // Assert
        provider.Should().NotBeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenTrueValue_ShouldReturnDefaultProfile()
    {
        // Arrange
        var mockProvider = CreateMockProvider("true");
        var tokenResponse = new OAuthTokenResponse("mock-access-token", "mock-id-token:test-nonce", 3600);

        // Act
        var profile = await mockProvider.GetUserProfileAsync(tokenResponse, CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.Email.Should().EndWith(OAuthProviderFactory.MockEmailDomain);
        profile.EmailVerified.Should().BeTrue();
        profile.ProviderUserId.Should().Be(MockOAuthProvider.MockProviderUserId);
        profile.Issuer.Should().Be("https://mock.localhost/google");
        profile.Subject.Should().Be(MockOAuthProvider.MockProviderUserId);
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenCustomEmailPrefix_ShouldReturnMockLocalhostEmail()
    {
        // Arrange
        var mockProvider = CreateMockProvider("customuser");
        var tokenResponse = new OAuthTokenResponse("mock-access-token", "mock-id-token:test-nonce", 3600);

        // Act
        var profile = await mockProvider.GetUserProfileAsync(tokenResponse, CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.Email.Should().Be($"customuser{OAuthProviderFactory.MockEmailDomain}");
        profile.ProviderUserId.Should().Be("mock-google-customuser");
        profile.Issuer.Should().Be("https://mock.localhost/google");
        profile.Subject.Should().Be("mock-google-customuser");
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenNoEmailValue_ShouldReturnProfileWithoutEmail()
    {
        // Arrange
        var mockProvider = CreateMockProvider(MockOAuthProvider.NoEmailValue);
        var tokenResponse = new OAuthTokenResponse("mock-access-token", "mock-id-token:test-nonce", 3600);

        // Act
        var profile = await mockProvider.GetUserProfileAsync(tokenResponse, CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.Email.Should().BeNull();
        profile.EmailVerified.Should().BeFalse();
        profile.ProviderUserId.Should().Be(MockOAuthProvider.MockProviderUserId);
        profile.Issuer.Should().Be("https://mock.localhost/google");
        profile.Subject.Should().Be(MockOAuthProvider.MockProviderUserId);
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenIdentityValueWithEmailPrefix_ShouldFixProviderUserIdAndUseEmailPrefix()
    {
        // Arrange
        var mockProvider = CreateMockProvider($"{MockOAuthProvider.IdentityPrefix}stableidentity:changedemail");
        var tokenResponse = new OAuthTokenResponse("mock-access-token", "mock-id-token:test-nonce", 3600);

        // Act
        var profile = await mockProvider.GetUserProfileAsync(tokenResponse, CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.ProviderUserId.Should().Be("mock-google-stableidentity");
        profile.Email.Should().Be($"changedemail{OAuthProviderFactory.MockEmailDomain}");
        profile.EmailVerified.Should().BeTrue();
        profile.Issuer.Should().Be("https://mock.localhost/google");
        profile.Subject.Should().Be("mock-google-stableidentity");
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenIdentityValueWithoutEmailPrefix_ShouldReturnProfileWithoutEmail()
    {
        // Arrange
        var mockProvider = CreateMockProvider($"{MockOAuthProvider.IdentityPrefix}stableidentity");
        var tokenResponse = new OAuthTokenResponse("mock-access-token", "mock-id-token:test-nonce", 3600);

        // Act
        var profile = await mockProvider.GetUserProfileAsync(tokenResponse, CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.ProviderUserId.Should().Be("mock-google-stableidentity");
        profile.Email.Should().BeNull();
        profile.EmailVerified.Should().BeFalse();
        profile.Issuer.Should().Be("https://mock.localhost/google");
        profile.Subject.Should().Be("mock-google-stableidentity");
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenEntraProvider_ShouldReturnEntraIdentityAndIssuer()
    {
        // Arrange
        var mockProvider = CreateMockProvider("true", ExternalProviderType.Entra);
        var tokenResponse = new OAuthTokenResponse("mock-access-token", "mock-id-token:test-nonce", 3600);

        // Act
        var profile = await mockProvider.GetUserProfileAsync(tokenResponse, CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.ProviderUserId.Should().StartWith("mock-entra-");
        profile.Subject.Should().StartWith("mock-entra-");
        profile.Issuer.Should().Be("https://mock.localhost/entra");
        profile.Email.Should().EndWith(OAuthProviderFactory.MockEmailDomain);
        profile.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenEntraProviderWithIdentityValue_ShouldReturnEntraIdentity()
    {
        // Arrange
        var mockProvider = CreateMockProvider($"{MockOAuthProvider.IdentityPrefix}entrauser", ExternalProviderType.Entra);
        var tokenResponse = new OAuthTokenResponse("mock-access-token", "mock-id-token:test-nonce", 3600);

        // Act
        var profile = await mockProvider.GetUserProfileAsync(tokenResponse, CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.ProviderUserId.Should().Be("mock-entra-entrauser");
        profile.Subject.Should().Be("mock-entra-entrauser");
        profile.Issuer.Should().Be("https://mock.localhost/entra");
        profile.Email.Should().BeNull();
    }

    private static OAuthProviderFactory CreateProviderFactory(string? entraClientId, string? entraClientSecret = ConfiguredClientSecret, bool allowMockProvider = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OAuth:AllowMockProvider"] = allowMockProvider.ToString().ToLowerInvariant(),
                    ["OAuth:Entra:ClientId"] = entraClientId,
                    ["OAuth:Entra:ClientSecret"] = entraClientSecret
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IOAuthProvider>("google", (_, _) => Substitute.For<IOAuthProvider>());
        services.AddKeyedSingleton<IOAuthProvider>("entra", (_, _) => Substitute.For<IOAuthProvider>());
        services.AddKeyedSingleton<IOAuthProvider>("mock-entra", (_, _) => Substitute.For<IOAuthProvider>());

        return new OAuthProviderFactory(services.BuildServiceProvider(), configuration);
    }

    private static MockOAuthProvider CreateMockProvider(string cookieValue, ExternalProviderType providerType = ExternalProviderType.Google)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OAuth:AllowMockProvider"] = "true" })
            .Build();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Append("Cookie", $"{OAuthProviderFactory.UseMockProviderCookieName}={cookieValue}");
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
        return new MockOAuthProvider(providerType, configuration, httpContextAccessor);
    }
}
