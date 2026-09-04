using System.Net;
using System.Security.Cryptography;
using System.Text;
using Account.Features.ExternalAuthentication.Domain;
using Account.Integrations.OAuth;
using Account.Integrations.OAuth.MitId;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.OpenIdConnect;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class MitIdOAuthProviderTests : IDisposable
{
    private const string Domain = "mitid.test.localhost";
    private const string Issuer = $"https://{Domain}";
    private const string DiscoveryUrl = $"{Issuer}/.well-known/openid-configuration";
    private const string JsonWebKeySetUrl = $"{Issuer}/.well-known/jwks";
    private const string AuthorizationEndpoint = $"{Issuer}/oauth2/authorize";
    private const string ClientId = "mitid-client-id";
    private const string PersonIdentifier = "44444444-4444-4444-4444-444444444444";
    private const string SubjectClaimValue = "broker-pseudonym-value";
    private const string NonceClaimValue = "mitid-nonce-value";
    private const string AccessToken = "mitid-access-token";
    private const string SigningKeyId = "mitid-test-signing-key";
    private const string SubstantialUrn = "urn:grn:authn:dk:mitid:substantial";

    private static readonly DateTimeOffset AuthenticationInstant = new(2026, 9, 4, 10, 30, 0, TimeSpan.Zero);

    private readonly List<HttpClient> _httpClients = [];
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _signingKey;

    public MitIdOAuthProviderTests()
    {
        _signingKey = new RsaSecurityKey(_rsa) { KeyId = SigningKeyId };
    }

    public void Dispose()
    {
        foreach (var httpClient in _httpClients)
        {
            httpClient.Dispose();
        }

        _rsa.Dispose();
    }

    [Fact]
    public void BuildAuthorizationUrl_WhenCalled_ShouldRequestOnlyOpenIdAtSubstantialWithAFreshAuthentication()
    {
        // Act
        var authorizationUrl = CreateProvider().BuildAuthorizationUrl("state-token", "code-challenge", NonceClaimValue, $"{Issuer}/callback");

        // Assert
        authorizationUrl.Should().StartWith($"{AuthorizationEndpoint}?");
        authorizationUrl.Should().Contain("scope=openid");
        authorizationUrl.Should().NotContain("ssn");
        authorizationUrl.Should().NotContain("address");
        authorizationUrl.Should().Contain($"acr_values={Uri.EscapeDataString(SubstantialUrn)}");
        authorizationUrl.Should().Contain("code_challenge_method=S256");
        authorizationUrl.Should().Contain("max_age=0");
        authorizationUrl.Should().Contain($"nonce={NonceClaimValue}");
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenValidToken_ShouldReturnProfileKeyedOnThePersonIdentifier()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims());

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.ProviderUserId.Should().Be(PersonIdentifier);
        profile.Subject.Should().Be(SubjectClaimValue);
        profile.Issuer.Should().Be(Issuer);
        profile.Nonce.Should().Be(NonceClaimValue);
        profile.AssuranceLevel.Should().Be(IdentityAssuranceLevel.Substantial);
        profile.AuthenticationInstant.Should().Be(AuthenticationInstant);
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenValidToken_ShouldReportNoPersonalData()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["name"] = "A Person";
        claims["birthdate"] = "1970-01-01";
        claims["cprNumberIdentifier"] = "0101701234";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.Email.Should().BeNull();
        profile.EmailVerified.Should().BeFalse();
        profile.FirstName.Should().BeNull();
        profile.LastName.Should().BeNull();
        profile.AvatarUrl.Should().BeNull();
        profile.Locale.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAssuranceLevelIsHigherThanRequested_ShouldRecordTheLevelReached()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["acr"] = "urn:grn:authn:dk:mitid:high";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.AssuranceLevel.Should().Be(IdentityAssuranceLevel.High);
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAssuranceLevelIsBelowRequested_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["acr"] = "urn:grn:authn:dk:mitid:low";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAuthenticationIsMitIdErhverv_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["acr"] = "urn:grn:authn:dk:mitid:business";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAssuranceLevelUsesTheBrokerLegacyClaim_ShouldReturnProfile()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims.Remove("acr");
        claims["loA"] = "Substantial";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.AssuranceLevel.Should().Be(IdentityAssuranceLevel.Substantial);
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenNoAssuranceLevelClaimIsPresent_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims.Remove("acr");
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAssuranceLevelIsUnrecognised_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["acr"] = "urn:grn:authn:dk:mitid:something-new";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAuthenticationTimeUsesTheBrokerLegacyClaim_ShouldReturnProfile()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims.Remove("auth_time");
        claims["authenticationinstant"] = AuthenticationInstant.ToString("O");
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.AuthenticationInstant.Should().Be(AuthenticationInstant);
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenTheBrokerLegacyClaimCarriesNoOffset_ShouldReadItAsUtc()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims.Remove("auth_time");
        claims["authenticationinstant"] = "2026-09-04T10:30:00";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.AuthenticationInstant.Should().Be(AuthenticationInstant);
    }

    [Fact]
    public void Constructor_WhenTheDomainCarriesAScheme_ShouldThrow()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OAuth:MitId:Domain"] = $"https://{Domain}",
                    ["OAuth:MitId:ClientId"] = ClientId,
                    ["OAuth:MitId:ClientSecret"] = "mitid-client-secret"
                }
            )
            .Build();
        var httpClient = new HttpClient(new OpenIdConnectStubHandler(BuildDiscoveryDocument(), BuildJsonWebKeySet(_rsa), true));
        _httpClients.Add(httpClient);

        // Act
        var createProvider = () => new MitIdOAuthProvider(httpClient, configuration, new OpenIdConnectConfigurationManagerFactory(httpClient), NullLogger<MitIdOAuthProvider>.Instance);

        // Assert
        createProvider.Should().Throw<InvalidOperationException>().WithMessage("*without a scheme or path*");
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenNoAuthenticationTimeClaimIsPresent_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims.Remove("auth_time");
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenPersonIdentifierIsMissing_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims.Remove("uuid");
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenSubjectIsMissing_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims.Remove("sub");
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenIssuerIsAnotherBrokerDomain_ShouldReturnNull()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims(), "https://another-tenant.idura.broker");

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAudienceIsAnotherApplication_ShouldReturnNull()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims(), audience: "another-client-id");

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAuthorizedPartyNamesAnotherApplication_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["azp"] = "another-client-id";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAccessTokenHashIsAbsent_ShouldReturnProfile()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims.Remove("at_hash");
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAccessTokenHashDoesNotMatch_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["at_hash"] = ComputeAccessTokenHash("another-access-token");
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenTokenIsSignedWithAnUnsupportedAlgorithm_ShouldReturnNull()
    {
        // Arrange
        using var otherRsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = SigningKeyId };
        var idToken = CreateIdToken(CreateValidClaims(), algorithm: SecurityAlgorithms.RsaSha512, signingKey: otherKey);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenTokenHasExpired_ShouldReturnNull()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims(), expires: DateTime.UtcNow.AddMinutes(-5));

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenTheDiscoveryDocumentCannotBeRetrieved_ShouldReturnNull()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims());

        // Act
        var profile = await CreateProvider(false).GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenIdTokenIsMissing_ShouldReturnNull()
    {
        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, null, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    private MitIdOAuthProvider CreateProvider(bool discoveryAvailable = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OAuth:MitId:Domain"] = Domain,
                    ["OAuth:MitId:ClientId"] = ClientId,
                    ["OAuth:MitId:ClientSecret"] = "mitid-client-secret"
                }
            )
            .Build();

        var httpClient = new HttpClient(new OpenIdConnectStubHandler(BuildDiscoveryDocument(), BuildJsonWebKeySet(_rsa), discoveryAvailable));
        _httpClients.Add(httpClient);
        var openIdConnectConfigurationManagerFactory = new OpenIdConnectConfigurationManagerFactory(httpClient);

        return new MitIdOAuthProvider(httpClient, configuration, openIdConnectConfigurationManagerFactory, NullLogger<MitIdOAuthProvider>.Instance);
    }

    private static Dictionary<string, object> CreateValidClaims()
    {
        return new Dictionary<string, object>
        {
            ["uuid"] = PersonIdentifier,
            ["sub"] = SubjectClaimValue,
            ["nonce"] = NonceClaimValue,
            ["acr"] = SubstantialUrn,
            ["auth_time"] = AuthenticationInstant.ToUnixTimeSeconds(),
            ["identityscheme"] = "dkmitid",
            ["at_hash"] = ComputeAccessTokenHash(AccessToken)
        };
    }

    private string CreateIdToken(
        Dictionary<string, object> claims,
        string? issuer = null,
        string audience = ClientId,
        string algorithm = SecurityAlgorithms.RsaSha256,
        SecurityKey? signingKey = null,
        DateTime? expires = null
    )
    {
        // Issued well before any expiry a test asks for, so an expired token still has a valid nbf
        var issuedAt = DateTime.UtcNow.AddMinutes(-10);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey ?? _signingKey, algorithm)
        };

        return new JsonWebTokenHandler().CreateToken(tokenDescriptor);
    }

    private static string ComputeAccessTokenHash(string accessToken)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
        return Base64UrlEncoder.Encode(hash[..(hash.Length / 2)]);
    }

    private static string BuildDiscoveryDocument()
    {
        return $$"""
                 {
                   "issuer": "{{Issuer}}",
                   "jwks_uri": "{{JsonWebKeySetUrl}}",
                   "authorization_endpoint": "{{AuthorizationEndpoint}}",
                   "token_endpoint": "{{Issuer}}/oauth2/token",
                   "response_types_supported": ["code"],
                   "subject_types_supported": ["public"],
                   "id_token_signing_alg_values_supported": ["RS256"],
                   "code_challenge_methods_supported": ["S256"]
                 }
                 """;
    }

    private static string BuildJsonWebKeySet(RSA rsa)
    {
        var publicParameters = rsa.ExportParameters(false);
        var modulus = Base64UrlEncoder.Encode(publicParameters.Modulus);
        var exponent = Base64UrlEncoder.Encode(publicParameters.Exponent);

        return $$"""
                 {
                   "keys": [
                     {
                       "kty": "RSA",
                       "use": "sig",
                       "alg": "RS256",
                       "kid": "{{SigningKeyId}}",
                       "n": "{{modulus}}",
                       "e": "{{exponent}}"
                     }
                   ]
                 }
                 """;
    }

    private sealed class OpenIdConnectStubHandler(string discoveryDocument, string jsonWebKeySet, bool discoveryAvailable) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUrl = request.RequestUri!.ToString();
            if (!discoveryAvailable || (requestUrl != DiscoveryUrl && requestUrl != JsonWebKeySetUrl))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var body = requestUrl == DiscoveryUrl ? discoveryDocument : jsonWebKeySet;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
