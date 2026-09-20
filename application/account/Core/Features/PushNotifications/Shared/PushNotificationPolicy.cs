using Microsoft.Extensions.Configuration;
using SharedKernel.FeatureFlags;

namespace Account.Features.PushNotifications.Shared;

/// <summary>
///     What this deployment permits and what a subscription may carry. A deployment without a VAPID key pair has push
///     notifications turned off, and every endpoint of the feature answers as if it did not exist rather than telling a
///     caller it is there but closed.
/// </summary>
public static class PushNotificationPolicy
{
    public const int MaximumEndpointLength = 2000;

    public const int MaximumKeyLength = 200;

    public const int MaximumDeviceLabelLength = 100;

    public const int MaximumApplicationPathLength = 200;

    // The uncompressed P-256 point a browser subscribes with, and the secret that salts the payload encryption, as
    // RFC 8291 defines them
    public const int PublicKeyLength = 65;

    public const int AuthSecretLength = 16;

    public static bool IsEnabled(IConfiguration configuration)
    {
        return SharedKernel.FeatureFlags.FeatureFlags.PushNotifications.IsSystemFeatureFlagEnabled(configuration);
    }

    // The push service address is chosen by the browser and never by this system, so nothing beyond a well formed
    // absolute https address can be assumed
    public static bool IsPushServiceEndpoint(string? endpoint)
    {
        if (string.IsNullOrEmpty(endpoint) || endpoint.Length > MaximumEndpointLength) return false;

        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }

    // The browser's public key, which the payload is encrypted for. A value that is not an uncompressed P-256 point is
    // refused here rather than deep inside the encryption, where it would be an error of this system rather than of the
    // request that carried it.
    public static bool IsSubscriptionPublicKey(string? publicKey)
    {
        return TryDecode(publicKey, PublicKeyLength, out var decoded) && decoded[0] == 0x04;
    }

    public static bool IsSubscriptionAuthSecret(string? authSecret)
    {
        return TryDecode(authSecret, AuthSecretLength, out _);
    }

    // Base64url as the push protocol writes it, and padded base64 as a client may send it instead
    private static bool TryDecode(string? value, int expectedLength, out byte[] decoded)
    {
        decoded = [];
        if (string.IsNullOrEmpty(value) || value.Length > MaximumKeyLength) return false;

        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');

        var buffer = new byte[expectedLength + 3];
        if (!Convert.TryFromBase64String(normalized, buffer, out var written) || written != expectedLength) return false;

        decoded = buffer[..written];
        return true;
    }

    // Where the client opens, as a rooted path of this origin. An absolute address, a protocol-relative address and a
    // backslash are all refused, so a stored path can never send a browser to another origin
    public static bool IsApplicationPath(string? applicationPath)
    {
        if (string.IsNullOrEmpty(applicationPath) || applicationPath.Length > MaximumApplicationPathLength) return false;

        return applicationPath.StartsWith('/') && !applicationPath.StartsWith("//", StringComparison.Ordinal) && !applicationPath.Contains('\\');
    }
}
