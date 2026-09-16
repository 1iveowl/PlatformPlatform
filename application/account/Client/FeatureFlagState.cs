using Account.Features.Authentication.Queries;
using SharedKernel.Domain;
using SharedKernel.FeatureFlags;

namespace Account.Client;

// The feature flags one user scope sees. System flags and the evaluated tenant and user flags come from bootstrap; the
// x-user-feature-flags response header then replaces the evaluated flags. Evaluation stays on the server: this only
// reflects what the server reported, and a flag it did not report is disabled.
//
// Generation identifies the identity the flags belong to. It moves on every change of user or tenant and on every reset,
// so a header from a response whose request was sent under an earlier identity is ignored instead of repopulating it.
public sealed class FeatureFlagState
{
    private readonly Lock _lock = new();
    private string[] _evaluatedFeatureFlags = [];
    private long _generation;
    private Dictionary<string, bool> _systemFeatureFlags = new();

    public UserId? UserId { get; private set; }

    public TenantId? TenantId { get; private set; }

    public long Generation
    {
        get
        {
            lock (_lock)
            {
                return _generation;
            }
        }
    }

    public event Action? Changed;

    public void Initialize(BootstrapResponse bootstrap)
    {
        Replace(bootstrap)();
    }

    public void Reset()
    {
        Replace(null)();
    }

    // Replaces identity and flags without raising Changed, null resetting them, and returns the notification the caller
    // raises once the rest of its state is committed, so no subscriber sees the flags of one bootstrap beside another's state
    public Action Replace(BootstrapResponse? bootstrap)
    {
        var user = bootstrap?.IsAuthenticated == true ? bootstrap.User : null;
        var systemFeatureFlags = bootstrap is null ? new Dictionary<string, bool>() : new Dictionary<string, bool>(bootstrap.SystemFeatureFlags);
        var evaluatedFeatureFlags = Normalize(user?.FeatureFlags ?? []);

        bool changed;
        lock (_lock)
        {
            var identityChanged = user?.Id != UserId || user?.TenantId != TenantId;
            if (bootstrap is null || identityChanged) _generation++;

            changed = identityChanged
                      || !evaluatedFeatureFlags.SequenceEqual(_evaluatedFeatureFlags, StringComparer.Ordinal)
                      || !HaveSameEntries(systemFeatureFlags, _systemFeatureFlags);
            _systemFeatureFlags = systemFeatureFlags;
            _evaluatedFeatureFlags = evaluatedFeatureFlags;
            UserId = user?.Id;
            TenantId = user?.TenantId;
        }

        return changed ? RaiseChanged : static () => { };
    }

    // Null means the response did not carry the header and nothing changes; an empty value means no flag is enabled. The
    // generation is the one read when the request was sent.
    public void ApplyHeader(string? headerValue, long generation)
    {
        if (headerValue is null) return;

        bool changed;
        lock (_lock)
        {
            // Before an authenticated bootstrap, after a reset or once the identity moved on, a stray header must not change flags
            if (UserId is null || generation != _generation) return;

            var evaluatedFeatureFlags = Normalize(headerValue.Split(','));
            changed = !evaluatedFeatureFlags.SequenceEqual(_evaluatedFeatureFlags, StringComparer.Ordinal);
            _evaluatedFeatureFlags = evaluatedFeatureFlags;
        }

        if (changed) RaiseChanged();
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

    private void RaiseChanged()
    {
        Changed?.Invoke();
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
