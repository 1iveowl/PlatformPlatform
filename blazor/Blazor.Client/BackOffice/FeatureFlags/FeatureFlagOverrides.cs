// The decisions of the flag detail's override lists, the React back office's TenantOverrideRow, UserOverrideRow,
// OverrideSwitch and stateFilter: which state filter the URL names, who is offered an override, what one switch press does,
// and what each answer says. Only an identity in the admins group is offered a write, only on a flag the registry still
// declares, and the account API refuses the write for anyone else; hiding is never the only guard.

using System.Globalization;
using Account.Features.BackOffice.Queries;
using Account.Features.FeatureFlags.Domain;
using Account.Features.FeatureFlags.Queries;
using Blazor.Client.BackOffice.Shell;
using SharedKernel.FeatureFlags;

namespace Blazor.Client.BackOffice.FeatureFlags;

// The state toggle of an override list's toolbar. The URL leaves it out for Enabled, React's default, and writes All to show
// every row.
public enum FeatureFlagStateFilter
{
    All,
    Enabled,
    Disabled
}

public enum FeatureFlagOverrideChange
{
    Enable,
    Disable,
    Remove
}

public static class FeatureFlagOverrides
{
    public const string ActionToastTestId = "feature-flag-override-toast";

    public const FeatureFlagStateFilter DefaultStateFilter = FeatureFlagStateFilter.Enabled;

    public static readonly IReadOnlyList<FeatureFlagStateFilter> StateFilters = [FeatureFlagStateFilter.All, FeatureFlagStateFilter.Enabled, FeatureFlagStateFilter.Disabled];

    // An unknown value falls back to the default, as a missing one does
    public static FeatureFlagStateFilter ParseState(string? value)
    {
        return StateFilters.Cast<FeatureFlagStateFilter?>().FirstOrDefault(filter => string.Equals(filter.ToString(), value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? DefaultStateFilter;
    }

    // The URL value of a state: none for the default
    public static string? FormatState(FeatureFlagStateFilter state)
    {
        return state == DefaultStateFilter ? null : state.ToString();
    }

    public static FeatureFlagAudienceState? ToApiState(FeatureFlagStateFilter state)
    {
        return state switch
        {
            FeatureFlagStateFilter.Enabled => FeatureFlagAudienceState.Enabled,
            FeatureFlagStateFilter.Disabled => FeatureFlagAudienceState.Disabled,
            _ => null
        };
    }

    public static string GetStateLabel(FeatureFlagStateFilter state)
    {
        return state switch
        {
            FeatureFlagStateFilter.Enabled => BackOfficeStrings.Enabled,
            FeatureFlagStateFilter.Disabled => BackOfficeStrings.Disabled,
            _ => BackOfficeStrings.All
        };
    }

    // A flag the registry still declares has override lists; a plan-managed flag has only its read-only tenants list
    public static bool HasOverrideLists(FeatureFlagInfo flag)
    {
        return !FeatureFlagDetail.IsDeleted(flag) && flag.OrphanedAt is null;
    }

    public static bool CanSetOverrides(MeResponse? me, FeatureFlagInfo flag)
    {
        return BackOfficeUser.CanRunAdminActions(me) && HasOverrideLists(flag) && !FeatureFlagDetail.IsPlanFlag(flag);
    }

    public static bool IsManual(FeatureFlagSource source)
    {
        return source == FeatureFlagSource.Manual;
    }

    // One press of a row's switch. On an A/B test a manual override that already matches the rollout's own answer is removed,
    // which gives React's cycle of flip, flip back, clear; on any other flag the override is the only way to turn the flag on
    // for a row, so every press sets it.
    public static FeatureFlagOverrideChange GetSwitchChange(bool isAbTest, FeatureFlagSource source, bool isEnabled, bool defaultEnabled)
    {
        if (isAbTest && IsManual(source) && isEnabled == defaultEnabled) return FeatureFlagOverrideChange.Remove;
        return isEnabled ? FeatureFlagOverrideChange.Disable : FeatureFlagOverrideChange.Enable;
    }

    public static string GetSucceededMessage(FeatureFlagOverrideChange change, string flagName, string rowName)
    {
        return change switch
        {
            FeatureFlagOverrideChange.Enable => string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.FeatureFlagEnabledFor, flagName, rowName),
            FeatureFlagOverrideChange.Disable => string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.FeatureFlagDisabledFor, flagName, rowName),
            _ => string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.OverrideRemovedFor, rowName)
        };
    }

    // The date the row's override last changed, React's Last changed column
    public static DateTimeOffset? GetLastChanged(DateTimeOffset? overrideEnabledAt, DateTimeOffset? overrideDisabledAt)
    {
        return overrideDisabledAt ?? overrideEnabledAt;
    }

    public static string? FormatInclusionThreshold(int? inclusionThresholdPercentage)
    {
        return inclusionThresholdPercentage is { } percentage ? $"{percentage.ToString(CultureInfo.CurrentCulture)}%" : null;
    }
}
