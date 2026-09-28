// The decisions of the back office's feature flag detail, the React back office's feature-flags/$flagKey page and its
// FeatureFlagInfoSection and DeleteFeatureFlagDialog: which flag the URL's key names, which facts the information section
// shows, which admin action a flag takes and who is offered it, what each confirmation says and what an action's answer
// means. The actions follow the account API's own rules, so the page never offers a call that is certain to be refused:
// activate and deactivate only a kill-switch flag that is neither orphaned nor a stable module, the rollout percentage only
// on such a flag that is also an A/B test, and delete only an orphaned flag that is not deleted yet. Only an identity in the
// admins group is offered any of them, and the account API refuses each for anyone else; hiding is never the only guard.

using System.Globalization;
using Account.Client;
using Account.Features.BackOffice.Queries;
using Account.Features.FeatureFlags.Queries;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.BackOffice.Shell;

namespace Blazor.Client.BackOffice.FeatureFlags;

public enum FeatureFlagAction
{
    Activate,
    Deactivate,
    SetRolloutPercentage,
    Delete
}

public enum FeatureFlagActionOutcome
{
    // The account API stored the change
    Succeeded,

    // The account API refused the request's values (400), such as a percentage outside 0 to 100 or a flag that is not orphaned
    Invalid,

    // The account API refused the identity (403): it is not in the admins group
    Refused,

    // The account API does not know the flag (404): another administrator deleted it, or the registry no longer declares it
    NotFound,

    // Any other failure, a network failure included
    Failed,

    // A 401: the unauthorized handler is already leaving for the platform's login, so nothing is shown
    Leaving
}

public sealed record FeatureFlagFact(string Label, string Value, string TestId);

public static class FeatureFlagDetail
{
    public const string ActionToastTestId = "feature-flag-action-toast";

    public const int MinimumRolloutPercentage = 0;

    public const int MaximumRolloutPercentage = 100;

    // The URL carries the registry key exactly as the React back office does; any other value names no flag
    public static FeatureFlagInfo? Find(IReadOnlyList<FeatureFlagInfo> flags, string flagKey)
    {
        return flags.FirstOrDefault(flag => string.Equals(flag.Key, flagKey, StringComparison.Ordinal));
    }

    public static bool IsDeleted(FeatureFlagInfo flag)
    {
        return flag.DeletedAt is not null;
    }

    // Orphaned and not deleted yet: the registry no longer declares the flag, and only now can it be deleted
    public static bool IsOrphaned(FeatureFlagInfo flag)
    {
        return flag.OrphanedAt is not null && flag.DeletedAt is null;
    }

    public static bool IsPlanFlag(FeatureFlagInfo flag)
    {
        return FeatureFlagsListSource.GroupOf(flag) == FeatureFlagGroup.Plan;
    }

    // Activate and deactivate exist only for a kill-switch flag the registry still declares that is not a stable module; the
    // account API's validator refuses every other flag
    public static bool IsToggleable(FeatureFlagInfo flag)
    {
        return flag is { OrphanedAt: null, DeletedAt: null, IsStableModule: false, IsKillSwitchEnabled: true };
    }

    public static bool HasRolloutPercentage(FeatureFlagInfo flag)
    {
        return IsToggleable(flag) && flag.IsAbTestEligible;
    }

    public static bool IsOffered(MeResponse? me, FeatureFlagInfo flag, FeatureFlagAction action)
    {
        if (!BackOfficeUser.CanRunAdminActions(me)) return false;

        return action switch
        {
            FeatureFlagAction.Activate => IsToggleable(flag) && !flag.IsActive,
            FeatureFlagAction.Deactivate => IsToggleable(flag) && flag.IsActive,
            FeatureFlagAction.SetRolloutPercentage => HasRolloutPercentage(flag),
            FeatureFlagAction.Delete => IsOrphaned(flag),
            _ => false
        };
    }

    // Whatever the answer, the page reads the flags again afterwards, so it shows what the account API stored and never a
    // state the page assumed
    public static FeatureFlagActionOutcome FromResult(ApiCallResult result)
    {
        if (result.IsSuccess) return FeatureFlagActionOutcome.Succeeded;
        if (result.Outcome == ApiCallOutcome.Unauthorized) return FeatureFlagActionOutcome.Leaving;
        if (result.Outcome == ApiCallOutcome.ValidationFailure) return FeatureFlagActionOutcome.Invalid;

        return result.Problem?.StatusCode switch
        {
            400 => FeatureFlagActionOutcome.Invalid,
            403 => FeatureFlagActionOutcome.Refused,
            404 => FeatureFlagActionOutcome.NotFound,
            _ => FeatureFlagActionOutcome.Failed
        };
    }

    public static string GetSucceededMessage(FeatureFlagAction action, string flagName)
    {
        return action switch
        {
            FeatureFlagAction.Activate => BackOfficeStrings.FeatureFlagActivated,
            FeatureFlagAction.Deactivate => BackOfficeStrings.FeatureFlagDeactivated,
            FeatureFlagAction.SetRolloutPercentage => BackOfficeStrings.RolloutPercentageUpdated,
            _ => string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.FeatureFlagDeleted, flagName)
        };
    }

    // A change to a live flag reaches the users with their next access token; a deletion needs no such note
    public static string? GetSucceededDetail(FeatureFlagAction action)
    {
        return action == FeatureFlagAction.Delete ? null : BackOfficeStrings.ChangesReachUsersWithinFiveMinutes;
    }

    // Every confirmation names the flag and its scope
    public static string GetConfirmation(FeatureFlagAction action, FeatureFlagInfo flag)
    {
        var name = FeatureFlagsListSource.NameOf(flag);
        var scope = FeatureFlagsListSource.GetScopeLabel(flag);
        return action switch
        {
            FeatureFlagAction.Activate => string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.ActivateFeatureFlagConfirmation, name, scope),
            FeatureFlagAction.Deactivate => string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.DeactivateFeatureFlagConfirmation, name, scope),
            _ => string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.DeleteFeatureFlagConfirmation, name, flag.Key, scope)
        };
    }

    // The information section's facts, React's metadata lines: the key, the scope, the required plan of a plan flag, when the
    // flag was enabled (or the period it was enabled for), when it was deleted, and an A/B test's rollout and its bucket range
    public static IReadOnlyList<FeatureFlagFact> GetFacts(FeatureFlagInfo flag)
    {
        var facts = new List<FeatureFlagFact>
        {
            new(BackOfficeStrings.FlagKey, flag.Key, "feature-flag-key"),
            new(BackOfficeStrings.FlagScope, FeatureFlagsListSource.GetScopeLabel(flag), "feature-flag-scope")
        };

        if (flag.RequiredPlan is { } requiredPlan) facts.Add(new FeatureFlagFact(BackOfficeStrings.RequiredPlan, AccountDetailFormat.GetRequiredPlanLabel(requiredPlan), "feature-flag-required-plan"));

        if (flag is { EnabledAt: { } enabledAt, DisabledAt: { } disabledAt })
        {
            var period = string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.DatePeriod, AccountFormat.FormatDate(enabledAt), AccountFormat.FormatDate(disabledAt));
            facts.Add(new FeatureFlagFact(BackOfficeStrings.EnabledPeriod, period, "feature-flag-enabled"));
        }
        else if (flag.EnabledAt is { } enabledSince)
        {
            facts.Add(new FeatureFlagFact(BackOfficeStrings.EnabledSince, AccountFormat.FormatDate(enabledSince), "feature-flag-enabled"));
        }

        if (flag.DeletedAt is { } deletedAt) facts.Add(new FeatureFlagFact(BackOfficeStrings.DeletedOn, AccountFormat.FormatDate(deletedAt), "feature-flag-deleted"));

        if (FeatureFlagsListSource.GetRolloutLabel(flag) is { } rollout) facts.Add(new FeatureFlagFact(BackOfficeStrings.Rollout, rollout, "feature-flag-rollout"));

        if (GetRolloutBuckets(flag) is { } buckets) facts.Add(new FeatureFlagFact(BackOfficeStrings.RolloutBuckets, buckets, "feature-flag-rollout-buckets"));

        return facts;
    }

    // The bucket range an A/B test's rollout targets, which wraps past 99 when it starts late in the range
    public static string? GetRolloutBuckets(FeatureFlagInfo flag)
    {
        if (!flag.IsAbTestEligible || flag is not { RolloutBucketStart: { } start, RolloutBucketEnd: { } end, RolloutPercentage: { } percentage }) return null;

        return start <= end
            ? string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.RolloutBucketRange, start, end, percentage)
            : string.Format(CultureInfo.CurrentCulture, BackOfficeStrings.RolloutBucketWrappedRange, start, end, percentage);
    }

    public static string FormatPercentage(int? percentage)
    {
        return (percentage ?? 0).ToString(CultureInfo.InvariantCulture);
    }
}
