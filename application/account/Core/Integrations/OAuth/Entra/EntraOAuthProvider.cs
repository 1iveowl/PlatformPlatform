using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Account.Features.ExternalAuthentication.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.OpenIdConnect;

namespace Account.Integrations.OAuth.Entra;

internal sealed record EntraOAuthConfiguration(string ClientId, string ClientSecret);

public sealed class EntraOAuthProvider(HttpClient httpClient, IConfiguration configuration, OpenIdConnectConfigurationManagerFactory openIdConnectConfigurationManagerFactory, ILogger<EntraOAuthProvider> logger) : IOAuthProvider
{
    private const string AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
    private const string TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
    private const string DiscoveryUrl = "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration";

    /// <summary>
    ///     The common authority serves one discovery document for every directory, so it publishes the issuer as this
    ///     literal template rather than as a URL. No token ever carries it as its <c>iss</c>, and the signing keys are
    ///     labelled with either this template or one concrete per-directory issuer.
    /// </summary>
    private const string IssuerTemplate = "https://login.microsoftonline.com/{tenantid}/v2.0";

    private const string TenantIdPlaceholder = "{tenantid}";

    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly EntraOAuthConfiguration _configuration = configuration.GetSection("OAuth:Entra").Get<EntraOAuthConfiguration>()
                                                              ?? throw new InvalidOperationException("OAuth:Entra configuration is missing.");

    public ExternalProviderType ProviderType => ExternalProviderType.Entra;

    public string BuildAuthorizationUrl(string stateToken, string codeChallenge, string nonce, string redirectUri)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = _configuration.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = stateToken,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["nonce"] = nonce,
            ["prompt"] = "select_account"
        };

        var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"{AuthorizationEndpoint}?{queryString}";
    }

    public async Task<OAuthTokenResponse?> ExchangeCodeForTokensAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
    {
        try
        {
            var tokenRequest = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("grant_type", "authorization_code"),
                    new KeyValuePair<string, string>("code", code),
                    new KeyValuePair<string, string>("client_id", _configuration.ClientId),
                    new KeyValuePair<string, string>("client_secret", _configuration.ClientSecret),
                    new KeyValuePair<string, string>("redirect_uri", redirectUri),
                    new KeyValuePair<string, string>("code_verifier", codeVerifier)
                ]
            );

            var response = await httpClient.PostAsync(TokenEndpoint, tokenRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await LogTokenExchangeError(response, cancellationToken);
                return null;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<EntraTokenResponse>(cancellationToken);
            if (tokenResponse is null) return null;

            return new OAuthTokenResponse(tokenResponse.AccessToken, tokenResponse.IdToken, tokenResponse.ExpiresIn);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<OAuthUserProfile?> GetUserProfileAsync(OAuthTokenResponse tokenResponse, CancellationToken cancellationToken)
    {
        if (tokenResponse.IdToken is null) return null;

        if (!TokenHandler.CanReadToken(tokenResponse.IdToken)) return null;

        // The issuer is per directory, so the token's own tid decides which issuer is valid for this token
        var unvalidatedToken = TokenHandler.ReadJsonWebToken(tokenResponse.IdToken);
        var directoryId = ParseGuidClaim(unvalidatedToken, "tid");
        if (directoryId is null)
        {
            logger.LogWarning("Entra ID token validation failed: the tid claim is missing or is not a directory id");
            return null;
        }

        var expectedIssuer = IssuerTemplate.Replace(TenantIdPlaceholder, directoryId);

        var configurationManager = openIdConnectConfigurationManagerFactory.GetOrCreate(DiscoveryUrl);

        OpenIdConnectConfiguration openIdConfiguration;
        try
        {
            openIdConfiguration = await configurationManager.GetConfigurationAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            // The configuration manager wraps every discovery and key set retrieval failure in this exception
            logger.LogWarning(exception, "Entra ID token validation failed: the discovery document could not be retrieved");
            return null;
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = [expectedIssuer],
            ValidateAudience = true,
            ValidAudiences = [_configuration.ClientId],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(10), // Entra runs on NTP-synced clocks, so minimal skew is safe
            IssuerSigningKeys = openIdConfiguration.SigningKeys,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        };

        var validationResult = await TokenHandler.ValidateTokenAsync(tokenResponse.IdToken, validationParameters);

        if (!validationResult.IsValid)
        {
            logger.LogWarning("Entra ID token validation failed: {Reason}", validationResult.Exception?.GetType().Name);
            return null;
        }

        var token = (JsonWebToken)validationResult.SecurityToken;

        if (!ValidateSigningKeyIssuer(openIdConfiguration, token.Kid, expectedIssuer))
        {
            return null;
        }

        if (!ValidateAccessTokenHash(token, tokenResponse.AccessToken))
        {
            return null;
        }

        var authorizedParty = token.Claims.FirstOrDefault(c => c.Type == "azp")?.Value;
        if (!string.IsNullOrEmpty(authorizedParty) && authorizedParty != _configuration.ClientId)
        {
            logger.LogWarning("Entra ID token validation failed: the azp claim names another application");
            return null;
        }

        var objectId = ParseGuidClaim(token, "oid");
        if (objectId is null)
        {
            logger.LogWarning("Entra ID token validation failed: the oid claim is missing or is not an object id");
            return null;
        }

        var subject = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        if (string.IsNullOrEmpty(subject))
        {
            logger.LogWarning("Entra ID token validation failed: the sub claim is missing");
            return null;
        }

        // An oid is unique only within its directory, so the durable key is the directory and the object together
        var providerUserId = $"{directoryId}:{objectId}";

        var (email, emailVerified) = GetVerifiedEmail(token);

        return new OAuthUserProfile(
            providerUserId,
            email,
            emailVerified,
            token.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value,
            token.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value,
            null,
            null,
            token.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value,
            expectedIssuer,
            subject
        );
    }

    /// <summary>
    ///     The email claim is optional, mutable and set by a directory administrator, so it is trusted only when the
    ///     token also carries xms_edov, Microsoft's attestation that the email's domain is verified for the user's own
    ///     directory or that this is a personal Microsoft account. Without it no email is reported at all, because the
    ///     callback validator rejects an unverified email before the identity lookup runs.
    /// </summary>
    private static (string? Email, bool EmailVerified) GetVerifiedEmail(JsonWebToken token)
    {
        var email = token.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
        if (string.IsNullOrEmpty(email)) return (null, false);

        if (!token.TryGetPayloadValue<bool>("xms_edov", out var emailDomainOwnerVerified) || !emailDomainOwnerVerified)
        {
            return (null, false);
        }

        return (email, true);
    }

    /// <summary>
    ///     Every key in the common key set is labelled with an issuer, either the template that matches any directory or
    ///     one concrete per-directory issuer. A key pinned to one directory may not sign a token from another. The check
    ///     fails closed when the key cannot be identified: signature validation accepts a token without a kid by trying
    ///     every key, which would otherwise skip the per-key pin that this check exists to enforce.
    /// </summary>
    private bool ValidateSigningKeyIssuer(OpenIdConnectConfiguration openIdConfiguration, string keyId, string validatedIssuer)
    {
        var signingKey = string.IsNullOrEmpty(keyId) ? null : openIdConfiguration.JsonWebKeySet?.Keys.FirstOrDefault(key => key.Kid == keyId);
        if (signingKey is null)
        {
            logger.LogWarning("Entra ID token validation failed: the signing key could not be identified");
            return false;
        }

        if (!signingKey.AdditionalData.TryGetValue("issuer", out var signingKeyIssuer)) return true;

        var signingKeyIssuerValue = signingKeyIssuer?.ToString();
        if (signingKeyIssuerValue == IssuerTemplate || signingKeyIssuerValue == validatedIssuer) return true;

        logger.LogWarning("Entra ID token validation failed: the signing key is pinned to another directory");
        return false;
    }

    /// <summary>
    ///     Entra does not emit at_hash in ID tokens issued by the token endpoint, so an absent claim is the normal case.
    ///     A present claim must still match, and the access token is only hashed, never parsed.
    /// </summary>
    private bool ValidateAccessTokenHash(JsonWebToken idToken, string accessToken)
    {
        var accessTokenHash = idToken.Claims.FirstOrDefault(c => c.Type == "at_hash")?.Value;
        if (string.IsNullOrEmpty(accessTokenHash)) return true;

        if (idToken.Alg != SecurityAlgorithms.RsaSha256)
        {
            logger.LogWarning("Entra ID token validation failed: at_hash cannot be validated for algorithm '{Algorithm}'", idToken.Alg);
            return false;
        }

        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
        var expectedHash = Base64UrlEncoder.Encode(hash[..(hash.Length / 2)]);
        if (accessTokenHash == expectedHash) return true;

        logger.LogWarning("Entra ID token validation failed: the at_hash claim does not match the access token");
        return false;
    }

    private static string? ParseGuidClaim(JsonWebToken token, string claimType)
    {
        var claimValue = token.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;
        return Guid.TryParse(claimValue, out var parsedValue) ? parsedValue.ToString() : null;
    }

    private async Task LogTokenExchangeError(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int maxBodyLength = 500;
        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(errorBody);
            var error = document.RootElement.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : null;
            var errorDescription = document.RootElement.TryGetProperty("error_description", out var descriptionElement) ? descriptionElement.GetString() : null;

            logger.LogWarning("Entra token exchange failed with status '{StatusCode}', error '{Error}': {ErrorDescription}", response.StatusCode, error, errorDescription);
        }
        catch (JsonException)
        {
            var truncatedBody = errorBody.Length > maxBodyLength ? errorBody[..maxBodyLength] : errorBody;
            logger.LogWarning("Entra token exchange failed with status '{StatusCode}': {ErrorBody}", response.StatusCode, truncatedBody);
        }
    }

    private sealed record EntraTokenResponse(
        [property: JsonPropertyName("access_token")]
        string AccessToken,
        [property: JsonPropertyName("id_token")]
        string? IdToken,
        [property: JsonPropertyName("expires_in")]
        int ExpiresIn,
        [property: JsonPropertyName("token_type")]
        string TokenType,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("refresh_token")]
        string? RefreshToken
    );
}
