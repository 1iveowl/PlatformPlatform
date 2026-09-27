// The account detail's feature flags tab, the React back office's AccountFeatureFlagsTab: the account's tenant-scoped flags,
// read once, split into the account flags (no required plan) and the plan flags (gated by a plan), each shown in its own
// DataList with its own parameter prefix. Both lists page the one response in memory. A flag is named by its localized
// resource where it has one, by the registry's label otherwise, and by its key when the registry does not declare it. The tab
// is read-only: whether a flag is on for the account, and whether a manual override decides that, are shown as text.

using System.Globalization;
using Account.Features.FeatureFlags.Domain;
using Account.Features.FeatureFlags.Queries;
using Blazor.Client.Components.Lists;
using Blazor.Client.FeatureFlags;
using SharedKernel.Domain;
using FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags;

namespace Blazor.Client.BackOffice.Accounts;

public enum AccountFeatureFlagGroup
{
    Account,
    Plan
}

public static class AccountFeatureFlagsSource
{
    public const string AccountFlagsPrefix = "accountFlags";
    public const string PlanFlagsPrefix = "planFlags";

    // No sort keys: the flags keep the registry's order the API returns them in
    public const string DefaultOrderBy = "default";

    public static string ListId(TenantId tenantId, AccountFeatureFlagGroup group)
    {
        return $"back-office-account-{(group == AccountFeatureFlagGroup.Account ? "account" : "plan")}-flags-{tenantId.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    public static IReadOnlyList<TenantFeatureFlagInfo> GetGroup(IReadOnlyList<TenantFeatureFlagInfo> flags, AccountFeatureFlagGroup group)
    {
        return flags.Where(flag => group == AccountFeatureFlagGroup.Plan ? flag.RequiredPlan is not null : flag.RequiredPlan is null).ToArray();
    }

    // The inclusion column shows in the account group only when one of its flags is an A/B test
    public static bool ShowsInclusionColumn(IReadOnlyList<TenantFeatureFlagInfo> flags)
    {
        return flags.Any(flag => flag.IsAbTestEligible);
    }

    public static DataListFetchResult<TenantFeatureFlagInfo> Page(IReadOnlyList<TenantFeatureFlagInfo> flags, DataListRequest request)
    {
        return DataListFetchResult<TenantFeatureFlagInfo>.Success(flags.Skip(request.PageOffset * request.PageSize).Take(request.PageSize).ToArray(), flags.Count);
    }

    public static string NameOf(TenantFeatureFlagInfo flag)
    {
        return FeatureFlagLabels.TryResource(flag.FlagKey, FeatureFlagLabels.NameSuffix) ?? FeatureFlagRegistry.Get(flag.FlagKey)?.Label ?? flag.FlagKey;
    }

    public static string? DescriptionOf(TenantFeatureFlagInfo flag)
    {
        var description = FeatureFlagLabels.TryResource(flag.FlagKey, FeatureFlagLabels.DescriptionSuffix) ?? flag.Description;
        return string.IsNullOrWhiteSpace(description) ? null : description;
    }

    public static string GetStateLabel(TenantFeatureFlagInfo flag)
    {
        return flag.IsEnabled ? BackOfficeStrings.Enabled : BackOfficeStrings.Disabled;
    }

    public static bool IsManualOverride(TenantFeatureFlagInfo flag)
    {
        return flag.Source == FeatureFlagSource.Manual;
    }

    public static string? GetInclusionThreshold(TenantFeatureFlagInfo flag)
    {
        return flag.InclusionThresholdPercentage is { } percentage ? $"{percentage.ToString(CultureInfo.CurrentCulture)}%" : null;
    }
}
