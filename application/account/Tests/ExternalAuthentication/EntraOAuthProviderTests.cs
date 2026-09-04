using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Account.Integrations.OAuth;
using Account.Integrations.OAuth.Entra;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.OpenIdConnect;
using Xunit;

namespace Account.Tests.ExternalAuthentication;

public sealed class EntraOAuthProviderTests : IDisposable
{
    private const string ClientId = "11111111-1111-1111-1111-111111111111";
    private const string WorkDirectoryId = "22222222-2222-2222-2222-222222222222";
    private const string PersonalAccountDirectoryId = "9188040d-6c67-4c5b-b112-36a304b66dad";
    private const string ObjectId = "33333333-3333-3333-3333-333333333333";
    private const string SubjectClaimValue = "pairwise-subject-value";
    private const string NonceClaimValue = "entra-nonce-value";
    private const string AccessToken = "entra-access-token";
    private const string SigningKeyId = "entra-test-signing-key";
    private const string IssuerTemplate = "https://login.microsoftonline.com/{tenantid}/v2.0";
    private const string DiscoveryUrl = "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration";
    private const string JsonWebKeySetUrl = "https://login.microsoftonline.com/common/discovery/v2.0/keys";

    private readonly List<HttpClient> _httpClients = [];
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _signingKey;

    public EntraOAuthProviderTests()
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
    public async Task GetUserProfileAsync_WhenValidWorkDirectoryToken_ShouldReturnProfileKeyedOnDirectoryAndObject()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims());

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.ProviderUserId.Should().Be($"{WorkDirectoryId}:{ObjectId}");
        profile.Issuer.Should().Be($"https://login.microsoftonline.com/{WorkDirectoryId}/v2.0");
        profile.Subject.Should().Be(SubjectClaimValue);
        profile.Nonce.Should().Be(NonceClaimValue);
        profile.AvatarUrl.Should().BeNull();
        profile.Locale.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenPersonalAccountToken_ShouldReturnProfileKeyedOnPersonalAccountDirectory()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims(PersonalAccountDirectoryId));

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.ProviderUserId.Should().Be($"{PersonalAccountDirectoryId}:{ObjectId}");
        profile.Issuer.Should().Be($"https://login.microsoftonline.com/{PersonalAccountDirectoryId}/v2.0");
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenClaimsUseUpperCaseGuids_ShouldLowerCaseProviderUserId()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["tid"] = WorkDirectoryId.ToUpperInvariant();
        claims["oid"] = ObjectId.ToUpperInvariant();
        var idToken = CreateIdToken(claims, $"https://login.microsoftonline.com/{WorkDirectoryId}/v2.0");

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.ProviderUserId.Should().Be($"{WorkDirectoryId}:{ObjectId}");
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenIssuerBelongsToAnotherDirectory_ShouldReturnNull()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims(), $"https://login.microsoftonline.com/{PersonalAccountDirectoryId}/v2.0");

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenIssuerIsTheLiteralTemplate_ShouldReturnNull()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims(), IssuerTemplate);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenSigningKeyIsPinnedToAnotherDirectory_ShouldReturnNull()
    {
        // Arrange
        var provider = CreateProvider($"https://login.microsoftonline.com/{PersonalAccountDirectoryId}/v2.0");
        var idToken = CreateIdToken(CreateValidClaims());

        // Act
        var profile = await provider.GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenSigningKeyIsPinnedToTheTokenDirectory_ShouldReturnProfile()
    {
        // Arrange
        var provider = CreateProvider($"https://login.microsoftonline.com/{PersonalAccountDirectoryId}/v2.0");
        var idToken = CreateIdToken(CreateValidClaims(PersonalAccountDirectoryId));

        // Act
        var profile = await provider.GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.ProviderUserId.Should().Be($"{PersonalAccountDirectoryId}:{ObjectId}");
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenTokenHasNoKeyId_ShouldReturnNull()
    {
        // Arrange
        var provider = CreateProvider($"https://login.microsoftonline.com/{PersonalAccountDirectoryId}/v2.0");
        var unidentifiedSigningKey = new RsaSecurityKey(_rsa);
        var idToken = CreateIdToken(CreateValidClaims(), signingKey: unidentifiedSigningKey);

        // Act
        var profile = await provider.GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenSignedByAKeyOutsideTheKeySet_ShouldReturnNull()
    {
        // Arrange
        using var foreignRsa = RSA.Create(2048);
        var foreignSigningKey = new RsaSecurityKey(foreignRsa) { KeyId = SigningKeyId };
        var idToken = CreateIdToken(CreateValidClaims(), signingKey: foreignSigningKey);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenTokenHasExpired_ShouldReturnNull()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims(), expires: DateTime.UtcNow.AddMinutes(-1));

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenTenantIdClaimIsMissing_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims.Remove("tid");
        var idToken = CreateIdToken(claims, $"https://login.microsoftonline.com/{WorkDirectoryId}/v2.0");

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenTenantIdClaimIsNotAGuid_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["tid"] = "contoso.onmicrosoft.com";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAudienceIsAnotherApplication_ShouldReturnNull()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims(), audience: "44444444-4444-4444-4444-444444444444");

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAccessTokenHashIsTampered_ShouldReturnNull()
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
        profile.ProviderUserId.Should().Be($"{WorkDirectoryId}:{ObjectId}");
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenSignedWithAnotherAlgorithm_ShouldReturnNull()
    {
        // Arrange
        var idToken = CreateIdToken(CreateValidClaims(), algorithm: SecurityAlgorithms.RsaSha512);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenObjectIdClaimIsMissing_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims.Remove("oid");
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenAuthorizedPartyIsAnotherApplication_ShouldReturnNull()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["azp"] = "44444444-4444-4444-4444-444444444444";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenEmailHasNoDomainOwnerVerification_ShouldReturnProfileWithoutEmail()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["email"] = "unverified@contoso.com";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.Email.Should().BeNull();
        profile.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenEmailDomainOwnerVerificationIsFalse_ShouldReturnProfileWithoutEmail()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["email"] = "unverified@contoso.com";
        claims["xms_edov"] = false;
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.Email.Should().BeNull();
        profile.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenEmailDomainOwnerVerificationIsTrue_ShouldReturnVerifiedEmail()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["email"] = "verified@contoso.com";
        claims["xms_edov"] = true;
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.Email.Should().Be("verified@contoso.com");
        profile.EmailVerified.Should().BeTrue();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    public async Task GetUserProfileAsync_WhenEmailDomainOwnerVerificationIsAPositiveString_ShouldReturnVerifiedEmail(string claimValue)
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["email"] = "verified@contoso.com";
        claims["xms_edov"] = claimValue;
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.Email.Should().Be("verified@contoso.com");
        profile.EmailVerified.Should().BeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("yes")]
    public async Task GetUserProfileAsync_WhenEmailDomainOwnerVerificationIsANegativeString_ShouldReturnProfileWithoutEmail(string claimValue)
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["email"] = "unverified@contoso.com";
        claims["xms_edov"] = claimValue;
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.Email.Should().BeNull();
        profile.EmailVerified.Should().BeFalse();
    }

    // Entra emits xms_edov as a boolean for work and school accounts and as a string for personal Microsoft accounts.
    // These two cases pin the minted tokens to those shapes, so the cases above exercise the string path for real
    // rather than a boolean the handler happened to coerce.
    [Fact]
    public void CreateIdToken_WhenEmailDomainOwnerVerificationIsABoolean_ShouldWriteAJsonBoolean()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["xms_edov"] = true;

        // Act
        var claim = ReadPayloadClaim(CreateIdToken(claims), "xms_edov");

        // Assert
        claim.ValueKind.Should().Be(JsonValueKind.True);
    }

    [Fact]
    public void CreateIdToken_WhenEmailDomainOwnerVerificationIsAString_ShouldWriteAJsonString()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["xms_edov"] = "1";

        // Act
        var claim = ReadPayloadClaim(CreateIdToken(claims), "xms_edov");

        // Assert
        claim.ValueKind.Should().Be(JsonValueKind.String);
        claim.Value.Should().Be("1");
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenOnlyPreferredUsernameIsPresent_ShouldReturnProfileWithoutEmail()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["preferred_username"] = "someone@contoso.com";
        claims["xms_edov"] = true;
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.Email.Should().BeNull();
        profile.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenGivenNameAndFamilyNameArePresent_ShouldMapThemToFirstAndLastName()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["given_name"] = "Ada";
        claims["family_name"] = "Lovelace";
        claims["name"] = "Ada King Lovelace";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.FirstName.Should().Be("Ada");
        profile.LastName.Should().Be("Lovelace");
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenOnlyTheNameClaimIsPresent_ShouldNotDeriveFirstAndLastName()
    {
        // Arrange
        var claims = CreateValidClaims();
        claims["name"] = "Ada King Lovelace";
        var idToken = CreateIdToken(claims);

        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, idToken, 3600), CancellationToken.None);

        // Assert
        profile.Should().NotBeNull();
        profile.FirstName.Should().BeNull();
        profile.LastName.Should().BeNull();
    }

    [Fact]
    public async Task GetUserProfileAsync_WhenIdTokenIsMissing_ShouldReturnNull()
    {
        // Act
        var profile = await CreateProvider().GetUserProfileAsync(new OAuthTokenResponse(AccessToken, null, 3600), CancellationToken.None);

        // Assert
        profile.Should().BeNull();
    }

    [Fact]
    public void BuildAuthorizationUrl_WhenCalled_ShouldTargetTheCommonAuthorityWithPkceAndSelectAccount()
    {
        // Act
        var authorizationUrl = CreateProvider().BuildAuthorizationUrl("state-token", "code-challenge", NonceClaimValue, "https://localhost/callback");

        // Assert
        authorizationUrl.Should().StartWith("https://login.microsoftonline.com/common/oauth2/v2.0/authorize?");
        authorizationUrl.Should().Contain($"client_id={ClientId}");
        authorizationUrl.Should().Contain("scope=openid%20email%20profile");
        authorizationUrl.Should().Contain("code_challenge_method=S256");
        authorizationUrl.Should().Contain("code_challenge=code-challenge");
        authorizationUrl.Should().Contain("state=state-token");
        authorizationUrl.Should().Contain($"nonce={NonceClaimValue}");
        authorizationUrl.Should().Contain("prompt=select_account");
    }

    private EntraOAuthProvider CreateProvider(string signingKeyIssuer = IssuerTemplate)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OAuth:Entra:ClientId"] = ClientId,
                    ["OAuth:Entra:ClientSecret"] = "entra-client-secret"
                }
            )
            .Build();

        var httpClient = new HttpClient(new OpenIdConnectStubHandler(BuildDiscoveryDocument(), BuildJsonWebKeySet(_rsa, signingKeyIssuer)));
        _httpClients.Add(httpClient);
        var openIdConnectConfigurationManagerFactory = new OpenIdConnectConfigurationManagerFactory(httpClient);

        return new EntraOAuthProvider(httpClient, configuration, openIdConnectConfigurationManagerFactory, NullLogger<EntraOAuthProvider>.Instance);
    }

    private static Dictionary<string, object> CreateValidClaims(string directoryId = WorkDirectoryId)
    {
        return new Dictionary<string, object>
        {
            ["tid"] = directoryId,
            ["oid"] = ObjectId,
            ["sub"] = SubjectClaimValue,
            ["nonce"] = NonceClaimValue,
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
        var directoryId = claims.TryGetValue("tid", out var tenantIdClaim) ? tenantIdClaim.ToString() : PersonalAccountDirectoryId;

        // Issued well before any expiry a test asks for, so an expired token still has a valid nbf
        var issuedAt = DateTime.UtcNow.AddMinutes(-10);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? $"https://login.microsoftonline.com/{directoryId}/v2.0",
            Audience = audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expires ?? DateTime.UtcNow.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey ?? _signingKey, algorithm)
        };

        return new JsonWebTokenHandler().CreateToken(tokenDescriptor);
    }

    private static (JsonValueKind ValueKind, string? Value) ReadPayloadClaim(string idToken, string claimType)
    {
        var payloadSegment = idToken.Split('.')[1];
        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(payloadSegment));
        var claim = payload.RootElement.GetProperty(claimType);
        return (claim.ValueKind, claim.ValueKind == JsonValueKind.String ? claim.GetString() : null);
    }

    private static string ComputeAccessTokenHash(string accessToken)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
        return Base64UrlEncoder.Encode(hash[..(hash.Length / 2)]);
    }

    // The common authority publishes the issuer as a literal template that no token ever carries, which is why the
    // provider fills the template with the token's own tid instead of trusting the discovery document
    private static string BuildDiscoveryDocument()
    {
        return $$"""
                 {
                   "issuer": "{{IssuerTemplate}}",
                   "jwks_uri": "{{JsonWebKeySetUrl}}",
                   "authorization_endpoint": "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
                   "token_endpoint": "https://login.microsoftonline.com/common/oauth2/v2.0/token",
                   "response_types_supported": ["code"],
                   "subject_types_supported": ["pairwise"],
                   "id_token_signing_alg_values_supported": ["RS256"]
                 }
                 """;
    }

    private static string BuildJsonWebKeySet(RSA rsa, string keyIssuer)
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
                       "e": "{{exponent}}",
                       "issuer": "{{keyIssuer}}"
                     }
                   ]
                 }
                 """;
    }

    private sealed class OpenIdConnectStubHandler(string discoveryDocument, string jsonWebKeySet) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestUrl = request.RequestUri!.ToString();
            if (requestUrl != DiscoveryUrl && requestUrl != JsonWebKeySetUrl)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var body = requestUrl == DiscoveryUrl ? discoveryDocument : jsonWebKeySet;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
