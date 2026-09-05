using System.Globalization;
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

namespace Account.Integrations.OAuth.MitId;

internal sealed record MitIdOAuthConfiguration(string Domain, string ClientId, string ClientSecret);

/// <summary>
///     Danish MitID reached through the Idura broker. Unlike Google, whose issuer is fixed, and Entra, whose issuer is
///     derived per directory from a shared discovery document, a broker serves one issuer per customer domain, so the
///     issuer and every endpoint come from configuration.
///     This provider only ever verifies an identity. It returns no email and no profile fields: a MitID token carries
///     the person's name, birth date and age, and a broker configured to collect it also carries the Danish CPR
///     number. None of that is read, so none of it can be stored.
/// </summary>
public sealed class MitIdOAuthProvider(HttpClient httpClient, IConfiguration configuration, OpenIdConnectConfigurationManagerFactory openIdConnectConfigurationManagerFactory, ILogger<MitIdOAuthProvider> logger) : IOAuthProvider
{
    /// <summary>
    ///     The level of assurance requested for every MitID authentication. Substantial is the level MitID issues to an
    ///     ordinary citizen authenticating with the MitID app, and it is the level the identity row records.
    /// </summary>
    public const IdentityAssuranceLevel RequestedAssuranceLevel = ExternalAuthenticationPolicy.RequiredAssuranceLevel;

    private const string AssuranceLevelUrnPrefix = "urn:grn:authn:dk:mitid:";

    private const string StandardAssuranceLevelClaim = "acr";
    private const string LegacyAssuranceLevelClaim = "loA";
    private const string StandardAuthenticationTimeClaim = "auth_time";
    private const string LegacyAuthenticationTimeClaim = "authenticationinstant";

    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly MitIdOAuthConfiguration _configuration = ReadConfiguration(configuration);

    private string Issuer => $"https://{_configuration.Domain}";

    private string AuthorizationEndpoint => $"{Issuer}/oauth2/authorize";

    private string TokenEndpoint => $"{Issuer}/oauth2/token";

    private string DiscoveryUrl => $"{Issuer}/.well-known/openid-configuration";

    public ExternalProviderType ProviderType => ExternalProviderType.MitId;

    public string BuildAuthorizationUrl(string stateToken, string codeChallenge, string nonce, string redirectUri)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = _configuration.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["response_mode"] = "query",
            // Only openid. The ssn scope returns the Danish CPR number and the address scope triggers a CPR lookup
            ["scope"] = "openid",
            ["state"] = stateToken,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["nonce"] = nonce,
            // Forces a fresh authentication rather than reusing a broker session, and obliges the provider to emit
            // auth_time, which is what the freshness of a verification is measured against
            ["max_age"] = "0",
            ["acr_values"] = ToAssuranceLevelUrn(RequestedAssuranceLevel)
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

            var tokenResponse = await response.Content.ReadFromJsonAsync<MitIdTokenResponse>(cancellationToken);
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

        var configurationManager = openIdConnectConfigurationManagerFactory.GetOrCreate(DiscoveryUrl);

        OpenIdConnectConfiguration openIdConfiguration;
        try
        {
            openIdConfiguration = await configurationManager.GetConfigurationAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            // The configuration manager wraps every discovery and key set retrieval failure in this exception
            logger.LogWarning(exception, "MitID token validation failed: the discovery document could not be retrieved");
            return null;
        }

        WarnWhenAuthorizationEndpointHasMoved(openIdConfiguration);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = [Issuer],
            ValidateAudience = true,
            ValidAudiences = [_configuration.ClientId],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(10),
            IssuerSigningKeys = openIdConfiguration.SigningKeys,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        };

        var validationResult = await TokenHandler.ValidateTokenAsync(tokenResponse.IdToken, validationParameters);

        if (!validationResult.IsValid)
        {
            logger.LogWarning("MitID token validation failed: {Reason}", validationResult.Exception?.GetType().Name);
            return null;
        }

        var token = (JsonWebToken)validationResult.SecurityToken;

        if (!ValidateAccessTokenHash(token, tokenResponse.AccessToken))
        {
            return null;
        }

        var authorizedParty = token.Claims.FirstOrDefault(c => c.Type == "azp")?.Value;
        if (!string.IsNullOrEmpty(authorizedParty) && authorizedParty != _configuration.ClientId)
        {
            logger.LogWarning("MitID token validation failed: the azp claim names another application");
            return null;
        }

        // The MitID uuid is the Person-ID. It is broker independent, unlike sub, which is a pseudonym per broker tenant
        var providerUserId = token.Claims.FirstOrDefault(c => c.Type == "uuid")?.Value;
        if (string.IsNullOrEmpty(providerUserId))
        {
            // Claim names only, never values: a MitID token carries the person's name and date of birth. The broker
            // decides whether claims arrive as short names or as URIs, so naming what did arrive turns a
            // misconfiguration that otherwise looks like a generic authentication failure into a one line diagnosis.
            logger.LogWarning(
                "MitID token validation failed: the uuid claim is missing. The token carries these claims: {ClaimTypes}",
                string.Join(", ", token.Claims.Select(c => c.Type).Distinct().Order())
            );
            return null;
        }

        var subject = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        if (string.IsNullOrEmpty(subject))
        {
            logger.LogWarning("MitID token validation failed: the sub claim is missing");
            return null;
        }

        var assuranceLevel = ReadAssuranceLevel(token);
        if (assuranceLevel is null)
        {
            return null;
        }

        // acr_values is a hint in OpenID Connect rather than a requirement, so a provider is free to satisfy an
        // authorization request at a lower level. That is not refused here: the profile carries the level actually
        // reached, and the verification handler refuses anything below the requirement with its own outcome, so the
        // person is told the verification was not strong enough rather than that authentication failed.

        var authenticationInstant = ReadAuthenticationInstant(token);
        if (authenticationInstant is null)
        {
            return null;
        }

        return new OAuthUserProfile(
            providerUserId,
            null,
            false,
            null,
            null,
            null,
            null,
            token.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value,
            Issuer,
            subject,
            assuranceLevel,
            authenticationInstant
        );
    }

    private static string ToAssuranceLevelUrn(IdentityAssuranceLevel assuranceLevel)
    {
        return $"{AssuranceLevelUrnPrefix}{assuranceLevel.ToString().ToLowerInvariant()}";
    }

    /// <summary>
    ///     The broker's public reference lists only the user-specific claims and says the issued token carries further
    ///     technical fields it does not name, so the standard claim is read first and the broker's own legacy claim
    ///     second. An unrecognised shape is logged rather than guessed at, because a provider in this system once
    ///     shipped a claim read against its documented type instead of its observed one and no test could see it.
    ///     A MitID Erhverv authentication lands here as an unmapped value and is refused, which is intended: business
    ///     identities return a different uuid for the same person.
    ///     Observed against the sandbox on 2026-09-04: the broker emits no <c>acr</c> claim and the value arrives in
    ///     <c>loA</c>, so the fallback is the live path rather than a precaution. The standard name is still read
    ///     first, so a broker that later adopts it needs no change here.
    /// </summary>
    private IdentityAssuranceLevel? ReadAssuranceLevel(JsonWebToken token)
    {
        foreach (var claimType in (string[])[StandardAssuranceLevelClaim, LegacyAssuranceLevelClaim])
        {
            var claimValue = token.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;
            if (string.IsNullOrEmpty(claimValue)) continue;

            var levelName = claimValue.StartsWith(AssuranceLevelUrnPrefix, StringComparison.OrdinalIgnoreCase)
                ? claimValue[AssuranceLevelUrnPrefix.Length..]
                : claimValue;

            if (Enum.TryParse<IdentityAssuranceLevel>(levelName, true, out var assuranceLevel))
            {
                return assuranceLevel;
            }

            // The value is one of the broker's assurance level identifiers, not something that identifies a person, and
            // an unrecognised level is exactly the case where the value is needed to see what changed
            logger.LogWarning("MitID token validation failed: the '{ClaimType}' claim carries the unrecognised value '{ClaimValue}'", claimType, claimValue);
            return null;
        }

        logger.LogWarning("MitID token validation failed: no assurance level claim was found");
        return null;
    }

    /// <summary>
    ///     When the person actually authenticated, which is what a verification's freshness is measured against.
    ///     The authorization request sends max_age=0, which under the OpenID Connect specification obliges a provider
    ///     to emit auth_time. Observed against the sandbox on 2026-09-04: this broker does not, and the value arrives
    ///     in the ISO 8601 <c>authenticationinstant</c> claim instead, so the fallback is the live path. The standard
    ///     name is still read first, so a broker that later conforms needs no change here. A legacy value without an
    ///     offset is read as UTC, which is what the broker documents; reading it as host local time would shift the
    ///     instant by the host's offset and either refuse every verification as stale or record one in the future.
    /// </summary>
    private DateTimeOffset? ReadAuthenticationInstant(JsonWebToken token)
    {
        if (token.TryGetPayloadValue<long>(StandardAuthenticationTimeClaim, out var authenticationTime))
        {
            return DateTimeOffset.FromUnixTimeSeconds(authenticationTime);
        }

        var legacyValue = token.Claims.FirstOrDefault(c => c.Type == LegacyAuthenticationTimeClaim)?.Value;
        if (!string.IsNullOrEmpty(legacyValue) && DateTimeOffset.TryParse(legacyValue, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var legacyInstant))
        {
            return legacyInstant;
        }

        logger.LogWarning("MitID token validation failed: no authentication time claim was found even though max_age was requested");
        return null;
    }

    /// <summary>
    ///     The domain is a bare host name such as <c>tenant.idura.broker</c>, because the issuer and every endpoint are
    ///     built from it. A scheme or path would only surface later as an opaque discovery failure, so it is refused here.
    /// </summary>
    private static MitIdOAuthConfiguration ReadConfiguration(IConfiguration configuration)
    {
        var mitIdConfiguration = configuration.GetSection("OAuth:MitId").Get<MitIdOAuthConfiguration>()
                                 ?? throw new InvalidOperationException("OAuth:MitId configuration is missing.");

        if (string.IsNullOrWhiteSpace(mitIdConfiguration.Domain) || mitIdConfiguration.Domain.Contains("://", StringComparison.Ordinal) || mitIdConfiguration.Domain.Contains('/'))
        {
            throw new InvalidOperationException("OAuth:MitId:Domain must be a host name without a scheme or path, for example 'tenant.idura.broker'.");
        }

        return mitIdConfiguration;
    }

    /// <summary>
    ///     The authorization endpoint is derived from the configured domain, because the interface builds the URL
    ///     synchronously and cannot await discovery. The discovery document is already in hand here, so a divergence is
    ///     reported rather than left to surface as an opaque browser error.
    /// </summary>
    private void WarnWhenAuthorizationEndpointHasMoved(OpenIdConnectConfiguration openIdConfiguration)
    {
        if (string.IsNullOrEmpty(openIdConfiguration.AuthorizationEndpoint)) return;
        if (openIdConfiguration.AuthorizationEndpoint == AuthorizationEndpoint) return;

        logger.LogError("MitID authorization endpoint has moved: the discovery document publishes '{PublishedEndpoint}' but requests are sent to '{DerivedEndpoint}'", openIdConfiguration.AuthorizationEndpoint, AuthorizationEndpoint);
    }

    /// <summary>
    ///     The broker does not document at_hash either way, so an absent claim is accepted and a present one must
    ///     match. The access token is only hashed, never parsed, because no request is made with it.
    /// </summary>
    private bool ValidateAccessTokenHash(JsonWebToken idToken, string accessToken)
    {
        var accessTokenHash = idToken.Claims.FirstOrDefault(c => c.Type == "at_hash")?.Value;
        if (string.IsNullOrEmpty(accessTokenHash)) return true;

        if (idToken.Alg != SecurityAlgorithms.RsaSha256)
        {
            logger.LogWarning("MitID token validation failed: at_hash cannot be validated for algorithm '{Algorithm}'", idToken.Alg);
            return false;
        }

        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
        var expectedHash = Base64UrlEncoder.Encode(hash[..(hash.Length / 2)]);
        if (accessTokenHash == expectedHash) return true;

        logger.LogWarning("MitID token validation failed: the at_hash claim does not match the access token");
        return false;
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

            logger.LogWarning("MitID token exchange failed with status '{StatusCode}', error '{Error}': {ErrorDescription}", response.StatusCode, error, errorDescription);
        }
        catch (JsonException)
        {
            var truncatedBody = errorBody.Length > maxBodyLength ? errorBody[..maxBodyLength] : errorBody;
            logger.LogWarning("MitID token exchange failed with status '{StatusCode}': {ErrorBody}", response.StatusCode, truncatedBody);
        }
    }

    private sealed record MitIdTokenResponse(
        [property: JsonPropertyName("access_token")]
        string AccessToken,
        [property: JsonPropertyName("id_token")]
        string? IdToken,
        [property: JsonPropertyName("expires_in")]
        int ExpiresIn,
        [property: JsonPropertyName("token_type")]
        string TokenType,
        [property: JsonPropertyName("scope")] string Scope
    );
}
