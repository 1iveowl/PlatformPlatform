namespace Blazor.Client.FeatureFlags;

// One switch in a feature flag section: the registry key the override endpoints take, the localized name that is the
// switch's accessible name, the localized description it is described by, and the state the server last reported
public sealed record FeatureFlagRow(string Key, string Name, string Description, bool Enabled);
