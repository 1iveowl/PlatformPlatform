// The back office's feature flag list, the React back office's feature-flags page: one read of GET
// /api/back-office/feature-flags, with IncludeDeleted when the show-deleted toggle is on, grouped as React groups it (account
// flags, plan flags, user flags and system flags, in that order, leaving out an empty group) and each group shown in its own
// DataList, paged in memory. A flag is named by its localized resource where it has one, by the registry's label otherwise,
// and by its key when the registry no longer declares it, which is the case for an orphaned or deleted flag. A row opens the
// flag's detail page, except in the system group, which React does not link either.

using System.Globalization;
using Account.Features.FeatureFlags.Queries;
using Blazor.Client.Components.Lists;
using Blazor.Client.FeatureFlags;
using SharedKernel.FeatureFlags;
using FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags;

namespace Blazor.Client.BackOffice.FeatureFlags;

public enum FeatureFlagGroup
{
    Account,
    Plan,
    User,
    System
}

public enum FeatureFlagStatus
{
    Active,
    Inactive,
    AlwaysOn,
    Removed,
    Deleted
}

public static class FeatureFlagsListSource
{
    // No sort keys: the flags keep the order the account API returns them in, the registry's first
    public const string DefaultOrderBy = "default";

    public static readonly IReadOnlyList<FeatureFlagGroup> GroupOrder = [FeatureFlagGroup.Account, FeatureFlagGroup.Plan, FeatureFlagGroup.User, FeatureFlagGroup.System];

    // The list id changes with the toggle, so a page cached without the deleted flags is never served with them
    public static string ListId(FeatureFlagGroup group, bool includeDeleted)
    {
        return $"back-office-feature-flags-{ToValue(group)}{(includeDeleted ? "-with-deleted" : "")}";
    }

    public static string ParameterPrefix(FeatureFlagGroup group)
    {
        return $"{ToValue(group)}Flags";
    }

    public static string ToValue(FeatureFlagGroup group)
    {
        return group.ToString().ToLowerInvariant();
    }

    // A plan flag is a tenant flag with a required plan; every other flag belongs to the group of its scope
    public static FeatureFlagGroup GroupOf(FeatureFlagInfo flag)
    {
        return flag.Scope switch
        {
            FeatureFlagScope.Tenant => flag.RequiredPlan is null ? FeatureFlagGroup.Account : FeatureFlagGroup.Plan,
            FeatureFlagScope.User => FeatureFlagGroup.User,
            _ => FeatureFlagGroup.System
        };
    }

    // The groups that have a flag, in React's order
    public static IReadOnlyList<FeatureFlagGroup> GetGroups(IReadOnlyList<FeatureFlagInfo> flags)
    {
        return GroupOrder.Where(group => flags.Any(flag => GroupOf(flag) == group)).ToArray();
    }

    public static IReadOnlyList<FeatureFlagInfo> GetGroup(IReadOnlyList<FeatureFlagInfo> flags, FeatureFlagGroup group)
    {
        return flags.Where(flag => GroupOf(flag) == group).ToArray();
    }

    public static DataListFetchResult<FeatureFlagInfo> Page(IReadOnlyList<FeatureFlagInfo> flags, DataListRequest request)
    {
        return DataListFetchResult<FeatureFlagInfo>.Success(flags.Skip(request.PageOffset * request.PageSize).Take(request.PageSize).ToArray(), flags.Count);
    }

    public static bool HasDetail(FeatureFlagGroup group)
    {
        return group != FeatureFlagGroup.System;
    }

    // The rollout column belongs to the groups whose flags can be A/B tests: plan and system flags are configured only in code
    public static bool ShowsRollout(FeatureFlagGroup group)
    {
        return group is FeatureFlagGroup.Account or FeatureFlagGroup.User;
    }

    public static string DetailUrl(string flagKey)
    {
        return BackOfficeUrls.ToAbsolute($"feature-flags/{Uri.EscapeDataString(flagKey)}");
    }

    public static string NameOf(FeatureFlagInfo flag)
    {
        return FeatureFlagLabels.TryResource(flag.Key, FeatureFlagLabels.NameSuffix) ?? FeatureFlagRegistry.Get(flag.Key)?.Label ?? flag.Key;
    }

    public static string? DescriptionOf(FeatureFlagInfo flag)
    {
        var description = FeatureFlagLabels.TryResource(flag.Key, FeatureFlagLabels.DescriptionSuffix) ?? flag.Description;
        return string.IsNullOrWhiteSpace(description) ? null : description;
    }

    // Deleted before removed, removed before always on: an orphaned row keeps the state it had, which no longer applies
    public static FeatureFlagStatus GetStatus(FeatureFlagInfo flag)
    {
        if (flag.DeletedAt is not null) return FeatureFlagStatus.Deleted;
        if (flag.OrphanedAt is not null) return FeatureFlagStatus.Removed;
        if (flag.IsStableModule) return FeatureFlagStatus.AlwaysOn;
        return flag.IsActive ? FeatureFlagStatus.Active : FeatureFlagStatus.Inactive;
    }

    public static string GetStatusLabel(FeatureFlagStatus status)
    {
        return status switch
        {
            FeatureFlagStatus.Active => BackOfficeStrings.FlagActive,
            FeatureFlagStatus.AlwaysOn => BackOfficeStrings.FlagAlwaysOn,
            FeatureFlagStatus.Removed => BackOfficeStrings.FlagRemoved,
            FeatureFlagStatus.Deleted => BackOfficeStrings.FlagDeleted,
            _ => BackOfficeStrings.FlagInactive
        };
    }

    public static string GetStatusClass(FeatureFlagStatus status)
    {
        return status switch
        {
            FeatureFlagStatus.Active or FeatureFlagStatus.AlwaysOn => "account-status account-status-active",
            FeatureFlagStatus.Removed or FeatureFlagStatus.Deleted => "account-status account-status-canceled",
            _ => "account-status account-status-free"
        };
    }

    // An A/B test's rollout percentage, 0 when none is set; nothing for a flag that is not an A/B test
    public static string? GetRolloutLabel(FeatureFlagInfo flag)
    {
        return flag.IsAbTestEligible ? $"{(flag.RolloutPercentage ?? 0).ToString(CultureInfo.CurrentCulture)}%" : null;
    }

    // The percentage is muted where it does not reach anyone: the flag is inactive or deleted
    public static bool IsRolloutInEffect(FeatureFlagInfo flag)
    {
        return flag is { IsActive: true, DeletedAt: null };
    }

    public static string GetGroupLabel(FeatureFlagGroup group)
    {
        return group switch
        {
            FeatureFlagGroup.Plan => BackOfficeStrings.PlanFlags,
            FeatureFlagGroup.User => BackOfficeStrings.UserFlags,
            FeatureFlagGroup.System => BackOfficeStrings.SystemFlags,
            _ => BackOfficeStrings.AccountFlags
        };
    }

    public static string GetGroupDescription(FeatureFlagGroup group)
    {
        return group switch
        {
            FeatureFlagGroup.Plan => BackOfficeStrings.PlanFlagsListDescription,
            FeatureFlagGroup.User => BackOfficeStrings.UserFlagsListDescription,
            FeatureFlagGroup.System => BackOfficeStrings.SystemFlagsListDescription,
            _ => BackOfficeStrings.AccountFlagsListDescription
        };
    }

    // The flag's scope as a confirmation and the detail name it
    public static string GetScopeLabel(FeatureFlagInfo flag)
    {
        return GroupOf(flag) switch
        {
            FeatureFlagGroup.Plan => BackOfficeStrings.PlanFlagScope,
            FeatureFlagGroup.User => BackOfficeStrings.UserFlagScope,
            FeatureFlagGroup.System => BackOfficeStrings.SystemFlagScope,
            _ => BackOfficeStrings.AccountFlagScope
        };
    }
}
