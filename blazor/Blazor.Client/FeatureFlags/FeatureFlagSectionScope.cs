namespace Blazor.Client.FeatureFlags;

// Which configurable flags a section shows and which override endpoint its switches call: the flags a tenant owner
// configures in account settings, or the flags a user configures in their preferences
public enum FeatureFlagSectionScope
{
    Tenant,
    User
}
