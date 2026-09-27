// The user detail's Feature flags tab's side of DataList, the React back office's UserFeatureFlagsSection read-only: the
// user-scoped flags as GET /api/back-office/users/{id}/feature-flags returns them, read once and paged in memory in the
// registry's order. A flag is named by its localized resource where it has one, by the registry's label otherwise, and by its
// key when the registry does not declare it; whether it is on for the user, and whether a manual override decides that, are
// shown as text.

using System.Globalization;
using Account.Features.FeatureFlags.Domain;
using Account.Features.FeatureFlags.Queries;
using Blazor.Client.Components.Lists;
using Blazor.Client.FeatureFlags;
using SharedKernel.Domain;
using FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags;

namespace Blazor.Client.BackOffice.Users;

public static class UserFeatureFlagsListSource
{
    public const string ParameterPrefix = "flags";

    // No sort keys: the flags keep the registry's order the API returns them in
    public const string DefaultOrderBy = "default";

    public static string ListId(UserId userId)
    {
        return $"back-office-user-flags-{userId.Value}";
    }

    public static DataListFetchResult<UserFeatureFlagInfo> Page(IReadOnlyList<UserFeatureFlagInfo> flags, DataListRequest request)
    {
        return DataListFetchResult<UserFeatureFlagInfo>.Success(flags.Skip(request.PageOffset * request.PageSize).Take(request.PageSize).ToArray(), flags.Count);
    }

    // The inclusion column shows only when one of the flags is an A/B test, as in the React table
    public static bool ShowsInclusionColumn(IReadOnlyList<UserFeatureFlagInfo> flags)
    {
        return flags.Any(flag => flag.IsAbTestEligible);
    }

    public static string NameOf(UserFeatureFlagInfo flag)
    {
        return FeatureFlagLabels.TryResource(flag.FlagKey, FeatureFlagLabels.NameSuffix) ?? FeatureFlagRegistry.Get(flag.FlagKey)?.Label ?? flag.FlagKey;
    }

    public static string? DescriptionOf(UserFeatureFlagInfo flag)
    {
        var description = FeatureFlagLabels.TryResource(flag.FlagKey, FeatureFlagLabels.DescriptionSuffix) ?? flag.Description;
        return string.IsNullOrWhiteSpace(description) ? null : description;
    }

    public static string GetStateLabel(UserFeatureFlagInfo flag)
    {
        return flag.IsEnabled ? BackOfficeStrings.Enabled : BackOfficeStrings.Disabled;
    }

    public static bool IsManualOverride(UserFeatureFlagInfo flag)
    {
        return flag.Source == FeatureFlagSource.Manual;
    }

    public static string? GetInclusionThreshold(UserFeatureFlagInfo flag)
    {
        return flag.InclusionThresholdPercentage is { } percentage ? $"{percentage.ToString(CultureInfo.CurrentCulture)}%" : null;
    }
}
