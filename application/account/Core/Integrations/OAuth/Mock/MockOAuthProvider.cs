using Account.Features.ExternalAuthentication.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Account.Integrations.OAuth.Mock;

/// <summary>
///     Stands in for a real provider when OAuth:AllowMockProvider is true and the __Test_Use_Mock_Provider cookie is
///     present. One instance is registered per provider type under the key "mock-{provider}" with the provider in
///     lower case, and every provider user id it returns has the shape "mock-{provider}-{suffix}". The cookie value
///     shapes the returned profile:
///     "true" returns the default profile with MockEmail and MockProviderUserId.
///     "fail:{mode}" returns the default profile and simulates the failure {mode}, one of access_denied,
///     token_exchange or email_not_verified.
///     "noemail" returns the default provider user id and a profile without an email.
///     "identity:{identityPrefix}:{emailPrefix}" fixes the provider user id to "mock-{provider}-{identityPrefix}" and
///     sets the email to "{emailPrefix}@mock.localhost", so a changed email at the provider can be simulated.
///     "identity:{identityPrefix}" without the email part returns the same provider user id and no email.
///     Any other value "{emailPrefix}" gives the email "{emailPrefix}@mock.localhost" and the provider user id
///     "mock-{provider}-{emailPrefix}".
/// </summary>
public sealed class MockOAuthProvider(ExternalProviderType providerType, IConfiguration configuration, IHttpContextAccessor httpContextAccessor) : IOAuthProvider
{
    public const string MockEmail = $"mockuser{OAuthProviderFactory.MockEmailDomain}";
    public const string MockFirstName = "Mock";
    public const string MockLastName = "User";
    public const string FailurePrefix = "fail:";
    public const string NoEmailValue = "noemail";
    public const string IdentityPrefix = "identity:";
    private const string DefaultProviderUserIdSuffix = "user-id-12345";

    // The default provider user id of the Google mock, which the API tests drive through the Google endpoints
    public static readonly string MockProviderUserId = BuildProviderUserId(ExternalProviderType.Google, DefaultProviderUserIdSuffix);

    private readonly bool _isEnabled = configuration.GetValue<bool>("OAuth:AllowMockProvider");

    public ExternalProviderType ProviderType => providerType;

    public string BuildAuthorizationUrl(string stateToken, string codeChallenge, string nonce, string redirectUri)
    {
        if (!_isEnabled)
        {
            throw new InvalidOperationException("Mock OAuth provider is not enabled.");
        }

        var failureMode = GetFailureMode(GetCookieValue());
        if (failureMode == "access_denied")
        {
            return $"{redirectUri}?error=access_denied&error_description=The+user+denied+access&state={Uri.EscapeDataString(stateToken)}";
        }

        return $"{redirectUri}?code=mock-authorization-code:{Uri.EscapeDataString(nonce)}&state={Uri.EscapeDataString(stateToken)}";
    }

    public Task<OAuthTokenResponse?> ExchangeCodeForTokensAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
    {
        if (!_isEnabled)
        {
            throw new InvalidOperationException("Mock OAuth provider is not enabled.");
        }

        var failureMode = GetFailureMode(GetCookieValue());
        if (failureMode == "token_exchange")
        {
            return Task.FromResult<OAuthTokenResponse?>(null);
        }

        var nonce = ExtractNonceFromMockCode(code);
        var mockTokenResponse = new OAuthTokenResponse(
            "mock-access-token",
            $"mock-id-token:{nonce}",
            3600
        );

        return Task.FromResult<OAuthTokenResponse?>(mockTokenResponse);
    }

    public Task<OAuthUserProfile?> GetUserProfileAsync(OAuthTokenResponse tokenResponse, CancellationToken cancellationToken)
    {
        if (!_isEnabled)
        {
            throw new InvalidOperationException("Mock OAuth provider is not enabled.");
        }

        var cookieValue = GetCookieValue();
        var failureMode = GetFailureMode(cookieValue);
        var (providerUserId, email) = GetProviderUserIdAndEmail(cookieValue);
        var emailVerified = email is not null && failureMode != "email_not_verified";
        var nonce = ExtractNonceFromMockIdToken(tokenResponse.IdToken);

        return Task.FromResult<OAuthUserProfile?>(new OAuthUserProfile(
                providerUserId,
                email,
                emailVerified,
                MockFirstName,
                MockLastName,
                null,
                "en",
                nonce
            )
        );
    }

    private (string ProviderUserId, string? Email) GetProviderUserIdAndEmail(string? cookieValue)
    {
        if (cookieValue is null || cookieValue == "true" || cookieValue.StartsWith(FailurePrefix, StringComparison.Ordinal))
        {
            return (BuildProviderUserId(providerType, DefaultProviderUserIdSuffix), MockEmail);
        }

        if (cookieValue == NoEmailValue)
        {
            return (BuildProviderUserId(providerType, DefaultProviderUserIdSuffix), null);
        }

        if (cookieValue.StartsWith(IdentityPrefix, StringComparison.Ordinal))
        {
            var parts = cookieValue[IdentityPrefix.Length..].Split(':', 2);
            var identityEmail = parts.Length == 2 ? BuildEmail(parts[1]) : null;
            return (BuildProviderUserId(providerType, parts[0]), identityEmail);
        }

        return (BuildProviderUserId(providerType, cookieValue), BuildEmail(cookieValue));
    }

    private static string BuildProviderUserId(ExternalProviderType providerType, string suffix)
    {
        return $"mock-{providerType.ToString().ToLowerInvariant()}-{suffix}";
    }

    private static string BuildEmail(string emailPrefix)
    {
        return $"{emailPrefix}{OAuthProviderFactory.MockEmailDomain}";
    }

    private string? GetCookieValue()
    {
        return httpContextAccessor.HttpContext!.Request.Cookies[OAuthProviderFactory.UseMockProviderCookieName];
    }

    private static string? GetFailureMode(string? cookieValue)
    {
        if (cookieValue is null || !cookieValue.StartsWith(FailurePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return cookieValue[FailurePrefix.Length..];
    }

    private static string? ExtractNonceFromMockCode(string code)
    {
        var separatorIndex = code.IndexOf(':');
        return separatorIndex >= 0 ? Uri.UnescapeDataString(code[(separatorIndex + 1)..]) : null;
    }

    private static string? ExtractNonceFromMockIdToken(string? idToken)
    {
        if (idToken is null) return null;
        var separatorIndex = idToken.IndexOf(':');
        return separatorIndex >= 0 ? idToken[(separatorIndex + 1)..] : null;
    }
}
