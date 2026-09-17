// The state and the text of one feature flag section, so the component only calls the client and renders. The section is
// hidden until the configurable list has been read and stays hidden when the list is empty, which is what an administrator
// deactivating a flag globally leaves behind. One toggle runs at a time: while it does, every switch in the section is
// disabled, and the switches are set from the list read after the change, never from the click, so a refused or a lost
// change leaves the state the server reports.

using System.Globalization;

namespace Blazor.Client.FeatureFlags;

public sealed class FeatureFlagSection(FeatureFlagSectionScope scope)
{
    public const string TenantSectionTestId = "account-features";

    public const string UserSectionTestId = "preferences-feature-flags";

    public const string TenantToastTestId = "feature-updated-toast";

    public const string UserToastTestId = "preference-updated-toast";

    private bool _isLoaded;

    public FeatureFlagSectionScope Scope => scope;

    public IReadOnlyList<FeatureFlagRow> Rows { get; private set; } = [];

    public string? PendingKey { get; private set; }

    public bool IsVisible => _isLoaded && Rows.Count > 0;

    public bool IsBusy => PendingKey is not null;

    public string Heading => scope == FeatureFlagSectionScope.Tenant ? AccountStrings.Features : AccountStrings.FeaturePreferences;

    public string Description => scope == FeatureFlagSectionScope.Tenant ? AccountStrings.FeaturesDescription : AccountStrings.FeaturePreferencesDescription;

    public string ToastTitle => scope == FeatureFlagSectionScope.Tenant ? AccountStrings.FeatureUpdated : AccountStrings.PreferenceUpdated;

    public string ToastTestId => scope == FeatureFlagSectionScope.Tenant ? TenantToastTestId : UserToastTestId;

    public string SectionTestId => scope == FeatureFlagSectionScope.Tenant ? TenantSectionTestId : UserSectionTestId;

    public string HeadingId => $"{SectionTestId}-heading";

    // The flag name followed by the note that the change takes up to five minutes to reach every user
    public static string UpdatedDetail(string flagName)
    {
        return string.Format(CultureInfo.CurrentCulture, AccountStrings.FeatureFlagUpdatedDetail, flagName);
    }

    // Keeps the order the API returned and drops a key the registry does not declare
    public void Load(IEnumerable<(string FlagKey, bool Enabled)> flags)
    {
        Rows = flags.Select(flag => FeatureFlagLabels.TryCreateRow(flag.FlagKey, flag.Enabled)).OfType<FeatureFlagRow>().ToArray();
        _isLoaded = true;
    }

    // False while another toggle runs or for a row this section does not show, so a second click starts nothing
    public bool TryBeginToggle(string flagKey)
    {
        if (IsBusy || Rows.All(row => row.Key != flagKey)) return false;

        PendingKey = flagKey;
        return true;
    }

    public void EndToggle()
    {
        PendingKey = null;
    }

    // The account this section belongs to is being left, so it shows nothing that could reach the next one
    public void Reset()
    {
        Rows = [];
        PendingKey = null;
        _isLoaded = false;
    }
}
