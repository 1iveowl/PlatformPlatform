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

    // A browser needs one subscription per device, so a handful covers a person and anything beyond this is a way of
    // making this system send many requests rather than a way of reading notifications
    public const int MaximumSubscriptionsPerUser = 20;

    public const string AllowedEndpointHostsConfigurationKey = "PushNotifications:AllowedEndpointHosts";

    public const int MaximumKeyLength = 200;

    public const int MaximumDeviceLabelLength = 100;

    public const int MaximumApplicationPathLength = 200;

    // The uncompressed P-256 point a browser subscribes with, and the secret that salts the payload encryption, as
    // RFC 8291 defines them
    public const int PublicKeyLength = 65;

    public const int AuthSecretLength = 16;

    // The push services of the browsers this edition supports. An entry matches its own host and any subdomain of it,
    // which is what the Apple and Windows push services need; the Google and Mozilla ones are single hosts.
    public static readonly string[] DefaultAllowedEndpointHosts =
        ["fcm.googleapis.com", "updates.push.services.mozilla.com", "push.apple.com", "notify.windows.com"];

    public static bool IsEnabled(IConfiguration configuration)
    {
        return SharedKernel.FeatureFlags.FeatureFlags.PushNotifications.IsSystemFeatureFlagEnabled(configuration);
    }

    // The hosts this deployment sends notifications through. A configured value replaces the default set rather than
    // extending it, so an operator reading the configuration sees the whole list and a deployment can narrow as well
    // as widen it.
    public static string[] GetAllowedEndpointHosts(IConfiguration configuration)
    {
        var configuredHosts = configuration[AllowedEndpointHostsConfigurationKey];
        if (string.IsNullOrWhiteSpace(configuredHosts)) return DefaultAllowedEndpointHosts;

        return configuredHosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    // The push service address is chosen by the browser and never by this system, so it is accepted only when it is a
    // plain https address: no user information, no port of its own, and no more characters than a subscription needs.
    public static bool IsPushServiceAddress(string? endpoint)
    {
        if (string.IsNullOrEmpty(endpoint) || endpoint.Length > MaximumEndpointLength) return false;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return false;

        return uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.IsDefaultPort;
    }

    // A plain https address of a push service this deployment knows. Every other address is refused, because the only
    // thing a stored endpoint does is make this system send a request to it.
    public static bool IsPushServiceEndpoint(string? endpoint, string[] allowedHosts)
    {
        if (!IsPushServiceAddress(endpoint)) return false;

        var host = new Uri(endpoint!, UriKind.Absolute).Host;
        return allowedHosts.Any(allowedHost => IsHostOrSubdomainOf(host, allowedHost));
    }

    // An entry matches its own host and any subdomain of it. A host that merely ends in the entry's text, such as
    // 'notfcm.googleapis.com' or 'fcm.googleapis.com.example.net', matches nothing. An entry may be written with a
    // leading '*.', the way a wildcard host is usually named, and means the same as the parent domain on its own.
    private static bool IsHostOrSubdomainOf(string host, string allowedHost)
    {
        var parentDomain = allowedHost.StartsWith("*.", StringComparison.Ordinal) ? allowedHost[2..] : allowedHost;
        if (parentDomain.Length == 0) return false;

        if (string.Equals(host, parentDomain, StringComparison.OrdinalIgnoreCase)) return true;

        return host.Length > parentDomain.Length + 1 && host.EndsWith($".{parentDomain}", StringComparison.OrdinalIgnoreCase);
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
