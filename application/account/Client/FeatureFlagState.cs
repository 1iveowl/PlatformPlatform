using Account.Features.Authentication.Queries;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;

namespace Account.Client;

// The feature flags one user scope sees. System flags and the evaluated tenant and user flags come from bootstrap; the
// x-user-feature-flags response header then replaces the evaluated flags. Evaluation stays on the server: this only
// reflects what the server reported, and a flag it did not report is disabled.
public sealed class FeatureFlagState
{
    private readonly Lock _lock = new();
    private string[] _evaluatedFeatureFlags = [];
    private Dictionary<string, bool> _systemFeatureFlags = new();

    public UserId? UserId { get; private set; }

    public TenantId? TenantId { get; private set; }

    public event Action? Changed;

    public void Initialize(BootstrapResponse bootstrap)
    {
        var user = bootstrap.IsAuthenticated ? bootstrap.User : null;
        Update(new Dictionary<string, bool>(bootstrap.SystemFeatureFlags), Normalize(user?.FeatureFlags ?? []), user?.Id, user?.TenantId);
    }

    public void Reset()
    {
        Update(new Dictionary<string, bool>(), [], null, null);
    }

    // Null means the response did not carry the header and nothing changes; an empty value means no flag is enabled
    public void ApplyHeader(string? headerValue)
    {
        if (headerValue is null) return;

        bool changed;
        lock (_lock)
        {
            // Before an authenticated bootstrap, or after a reset, a stray header must not enable flags for an anonymous scope
            if (UserId is null) return;

            var evaluatedFeatureFlags = Normalize(headerValue.Split(','));
            changed = !evaluatedFeatureFlags.SequenceEqual(_evaluatedFeatureFlags, StringComparer.Ordinal);
            _evaluatedFeatureFlags = evaluatedFeatureFlags;
        }

        if (changed) Changed?.Invoke();
    }

    public bool IsEnabled(FeatureFlagDefinition featureFlag)
    {
        lock (_lock)
        {
            return featureFlag is SystemFeatureFlag
                ? _systemFeatureFlags.TryGetValue(featureFlag.Key, out var enabled) && enabled
                : _evaluatedFeatureFlags.Contains(featureFlag.Key, StringComparer.Ordinal);
        }
    }

    private void Update(Dictionary<string, bool> systemFeatureFlags, string[] evaluatedFeatureFlags, UserId? userId, TenantId? tenantId)
    {
        bool changed;
        lock (_lock)
        {
            changed = userId != UserId
                      || tenantId != TenantId
                      || !evaluatedFeatureFlags.SequenceEqual(_evaluatedFeatureFlags, StringComparer.Ordinal)
                      || !HaveSameEntries(systemFeatureFlags, _systemFeatureFlags);
            _systemFeatureFlags = systemFeatureFlags;
            _evaluatedFeatureFlags = evaluatedFeatureFlags;
            UserId = userId;
            TenantId = tenantId;
        }

        if (changed) Changed?.Invoke();
    }

    private static string[] Normalize(IEnumerable<string> featureFlagKeys)
    {
        return featureFlagKeys.Select(key => key.Trim()).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool HaveSameEntries(Dictionary<string, bool> first, Dictionary<string, bool> second)
    {
        return first.Count == second.Count && first.All(entry => second.TryGetValue(entry.Key, out var enabled) && enabled == entry.Value);
    }
}
