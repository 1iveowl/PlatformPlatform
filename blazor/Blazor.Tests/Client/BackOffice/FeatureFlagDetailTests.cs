using System.ComponentModel.DataAnnotations;
using Account.Client;
using Account.Features.BackOffice.Queries;
using Account.Features.FeatureFlags.Queries;
using Blazor.Client.BackOffice.FeatureFlags;
using Blazor.Tests.Client.Lists;
using FluentAssertions;
using SharedKernel.FeatureFlags;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.BackOffice;

// The decisions of the back office's feature flag detail: the flag the URL's key names, which admin action a flag takes and
// that only an identity in the admins group is offered one, delete only for an orphaned flag, the percentage checked as a whole
// number from 0 to 100 before the call, each action's answer mapped to its outcome, and confirmations that name the flag and
// its scope
public sealed class FeatureFlagDetailTests
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    private static readonly MeResponse Admin = new("Admin", "admin@example.com", true, ["admins"]);

    private static readonly MeResponse User = new("User", "user@example.com", false, ["users"]);

    private static readonly DateTimeOffset OrphanedAt = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    // A kill-switch A/B test the registry declares, inactive, as the reconciler creates it
    private static readonly FeatureFlagInfo AbTest = FeatureFlagsListSourceTests.CreateFlag("experimental-ui") with
    {
        Scope = FeatureFlagScope.User, IsAbTestEligible = true, IsKillSwitchEnabled = true
    };

    [Fact]
    public void Find_ShouldMatchTheRegistryKeyExactly()
    {
        // Arrange
        FeatureFlagInfo[] flags = [AbTest, FeatureFlagsListSourceTests.CreateFlag("sso")];

        // Act & Assert
        FeatureFlagDetail.Find(flags, "experimental-ui").Should().BeSameAs(AbTest);
        FeatureFlagDetail.Find(flags, "Experimental-UI").Should().BeNull();
        FeatureFlagDetail.Find(flags, "unknown-flag").Should().BeNull();
    }

    [Fact]
    public void IsOffered_WhenTheIdentityIsNotAnAdmin_ShouldOfferNoAction()
    {
        // Arrange
        var orphaned = AbTest with { OrphanedAt = OrphanedAt };

        // Act & Assert
        foreach (var action in Enum.GetValues<FeatureFlagAction>())
        {
            FeatureFlagDetail.IsOffered(User, AbTest, action).Should().BeFalse();
            FeatureFlagDetail.IsOffered(User, AbTest with { IsActive = true }, action).Should().BeFalse();
            FeatureFlagDetail.IsOffered(User, orphaned, action).Should().BeFalse();
            FeatureFlagDetail.IsOffered(null, AbTest, action).Should().BeFalse();
        }
    }

    [Fact]
    public void IsOffered_WhenAnAdminOpensAKillSwitchABTest_ShouldOfferTheToggleForItsStateAndThePercentage()
    {
        // Act & Assert
        FeatureFlagDetail.IsOffered(Admin, AbTest, FeatureFlagAction.Activate).Should().BeTrue();
        FeatureFlagDetail.IsOffered(Admin, AbTest, FeatureFlagAction.Deactivate).Should().BeFalse();
        FeatureFlagDetail.IsOffered(Admin, AbTest with { IsActive = true }, FeatureFlagAction.Activate).Should().BeFalse();
        FeatureFlagDetail.IsOffered(Admin, AbTest with { IsActive = true }, FeatureFlagAction.Deactivate).Should().BeTrue();
        FeatureFlagDetail.IsOffered(Admin, AbTest, FeatureFlagAction.SetRolloutPercentage).Should().BeTrue();
        FeatureFlagDetail.IsOffered(Admin, AbTest, FeatureFlagAction.Delete).Should().BeFalse();
    }

    [Fact]
    public void IsOffered_WhenTheFlagIsNoKillSwitchOrAStableModuleOrNoABTest_ShouldOfferOnlyWhatTheAccountApiAccepts()
    {
        // Act & Assert
        FeatureFlagDetail.IsOffered(Admin, AbTest with { IsKillSwitchEnabled = false }, FeatureFlagAction.Activate).Should().BeFalse();
        FeatureFlagDetail.IsOffered(Admin, AbTest with { IsKillSwitchEnabled = false }, FeatureFlagAction.SetRolloutPercentage).Should().BeFalse();
        FeatureFlagDetail.IsOffered(Admin, AbTest with { IsStableModule = true }, FeatureFlagAction.Activate).Should().BeFalse();
        FeatureFlagDetail.IsOffered(Admin, AbTest with { IsAbTestEligible = false }, FeatureFlagAction.Activate).Should().BeTrue();
        FeatureFlagDetail.IsOffered(Admin, AbTest with { IsAbTestEligible = false }, FeatureFlagAction.SetRolloutPercentage).Should().BeFalse();
    }

    [Fact]
    public void IsOffered_ShouldOfferDeleteOnlyForAnOrphanedFlagThatIsNotDeletedYet()
    {
        // Arrange
        var orphaned = AbTest with { OrphanedAt = OrphanedAt, IsActive = true };
        var deleted = orphaned with { DeletedAt = OrphanedAt.AddDays(1) };

        // Act & Assert
        FeatureFlagDetail.IsOffered(Admin, orphaned, FeatureFlagAction.Delete).Should().BeTrue();
        FeatureFlagDetail.IsOffered(Admin, orphaned, FeatureFlagAction.Deactivate).Should().BeFalse();
        FeatureFlagDetail.IsOffered(Admin, orphaned, FeatureFlagAction.SetRolloutPercentage).Should().BeFalse();
        FeatureFlagDetail.IsOffered(Admin, deleted, FeatureFlagAction.Delete).Should().BeFalse();
        FeatureFlagDetail.IsOrphaned(orphaned).Should().BeTrue();
        FeatureFlagDetail.IsOrphaned(deleted).Should().BeFalse();
        FeatureFlagDetail.IsDeleted(deleted).Should().BeTrue();
    }

    [Fact]
    public void FromResult_WhenTheAccountApiStoredTheChange_ShouldBeSucceeded()
    {
        // Act
        var outcome = FeatureFlagDetail.FromResult(ApiCallResult.Success());

        // Assert
        outcome.Should().Be(FeatureFlagActionOutcome.Succeeded);
    }

    [Theory]
    [InlineData(ApiCallOutcome.ValidationFailure, 400, FeatureFlagActionOutcome.Invalid)]
    [InlineData(ApiCallOutcome.Failure, 400, FeatureFlagActionOutcome.Invalid)]
    [InlineData(ApiCallOutcome.Failure, 403, FeatureFlagActionOutcome.Refused)]
    [InlineData(ApiCallOutcome.Failure, 404, FeatureFlagActionOutcome.NotFound)]
    [InlineData(ApiCallOutcome.Failure, 500, FeatureFlagActionOutcome.Failed)]
    [InlineData(ApiCallOutcome.TransportFailure, null, FeatureFlagActionOutcome.Failed)]
    [InlineData(ApiCallOutcome.Unauthorized, 401, FeatureFlagActionOutcome.Leaving)]
    public void FromResult_WhenTheActionFailed_ShouldMapTheAnswerToItsOutcome(ApiCallOutcome outcome, int? statusCode, FeatureFlagActionOutcome expected)
    {
        // Arrange
        var result = ApiCallResult.Failed(outcome, new ApiCallProblem(statusCode, null, null, NoErrors, null));

        // Act
        var actionOutcome = FeatureFlagDetail.FromResult(result);

        // Assert
        actionOutcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(FeatureFlagAction.Activate)]
    [InlineData(FeatureFlagAction.Deactivate)]
    [InlineData(FeatureFlagAction.Delete)]
    public void GetConfirmation_ShouldNameTheFlagAndItsScope(FeatureFlagAction action)
    {
        // Arrange
        var flag = FeatureFlagsListSourceTests.CreateFlag("sso") with { RequiredPlan = "Premium" };

        // Act
        var confirmation = FeatureFlagDetail.GetConfirmation(action, flag);

        // Assert
        confirmation.Should().Contain("Single sign-on").And.Contain(BackOfficeStrings.PlanFlagScope);
        if (action == FeatureFlagAction.Delete) confirmation.Should().Contain("sso");
    }

    [Fact]
    public void GetSucceededMessage_ShouldNameTheDeletedFlagAndNoteThePropagationOfAChange()
    {
        // Act & Assert
        FeatureFlagDetail.GetSucceededMessage(FeatureFlagAction.Activate, "Beta").Should().Be(BackOfficeStrings.FeatureFlagActivated);
        FeatureFlagDetail.GetSucceededMessage(FeatureFlagAction.SetRolloutPercentage, "Beta").Should().Be(BackOfficeStrings.RolloutPercentageUpdated);
        FeatureFlagDetail.GetSucceededMessage(FeatureFlagAction.Delete, "Beta").Should().Contain("Beta");
        FeatureFlagDetail.GetSucceededDetail(FeatureFlagAction.Deactivate).Should().Be(BackOfficeStrings.ChangesReachUsersWithinFiveMinutes);
        FeatureFlagDetail.GetSucceededDetail(FeatureFlagAction.Delete).Should().BeNull();
    }

    [Fact]
    public void GetFacts_ShouldListTheKeyScopeRolloutAndBucketsOfAnABTest()
    {
        // Arrange
        var flag = AbTest with { RolloutBucketStart = 10, RolloutBucketEnd = 51, RolloutPercentage = 42, EnabledAt = OrphanedAt };

        // Act
        var facts = FeatureFlagDetail.GetFacts(flag);

        // Assert
        facts.Select(fact => fact.TestId).Should()
            .Equal("feature-flag-key", "feature-flag-scope", "feature-flag-enabled", "feature-flag-rollout", "feature-flag-rollout-buckets");
        facts[0].Value.Should().Be("experimental-ui");
        facts[1].Value.Should().Be(BackOfficeStrings.UserFlagScope);
        facts[2].Label.Should().Be(BackOfficeStrings.EnabledSince);
    }

    [Fact]
    public void GetFacts_ShouldListTheEnabledPeriodTheDeletedDateAndTheRequiredPlan()
    {
        // Arrange
        var flag = FeatureFlagsListSourceTests.CreateFlag("removed-from-code") with
        {
            RequiredPlan = "Premium", EnabledAt = OrphanedAt, DisabledAt = OrphanedAt.AddDays(2), OrphanedAt = OrphanedAt, DeletedAt = OrphanedAt.AddDays(3)
        };

        // Act
        var facts = FeatureFlagDetail.GetFacts(flag);

        // Assert
        facts.Select(fact => fact.TestId).Should().Equal("feature-flag-key", "feature-flag-scope", "feature-flag-required-plan", "feature-flag-enabled", "feature-flag-deleted");
        facts[3].Label.Should().Be(BackOfficeStrings.EnabledPeriod);
    }

    [Theory]
    [InlineData(10, 51, 42, "10-51 (42%)")]
    [InlineData(90, 9, 20, "90-99 and 0-9 (20%)")]
    public void GetRolloutBuckets_ShouldShowTheRangeAndWrapPastTheLastBucket(int start, int end, int percentage, string expected)
    {
        // Act
        var buckets = FeatureFlagDetail.GetRolloutBuckets(AbTest with { RolloutBucketStart = start, RolloutBucketEnd = end, RolloutPercentage = percentage });

        // Assert
        buckets.Should().Be(expected);
        FeatureFlagDetail.GetRolloutBuckets(AbTest).Should().BeNull();
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("42", true)]
    [InlineData("100", true)]
    [InlineData("101", false)]
    [InlineData("-1", false)]
    [InlineData("12.5", false)]
    [InlineData("1e2", false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    public void RolloutForm_ShouldAcceptOnlyAWholeNumberFrom0To100(string value, bool isValid)
    {
        // Arrange
        var form = new FeatureFlagRolloutForm { RolloutPercentage = value };
        var results = new List<ValidationResult>();

        // Act
        var valid = Validator.TryValidateObject(form, new ValidationContext(form), results, true);

        // Assert
        valid.Should().Be(isValid);
        if (isValid)
        {
            form.ToPercentage().Should().Be(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            results.Should().OnlyContain(result => result.ErrorMessage == BackOfficeStrings.RolloutPercentageInvalid);
        }
    }
}
