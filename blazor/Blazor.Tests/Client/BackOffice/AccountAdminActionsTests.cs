using System.Globalization;
using Account.Features.Tenants.BackOffice.Commands;
using Blazor.Client.BackOffice.Accounts;
using Blazor.Client.BackOffice.Shared;
using Blazor.Client.Forms;
using FluentAssertions;
using SharedKernel.FeatureFlags;

namespace Blazor.Tests.Client.BackOffice;

// What the account's admin actions report and send: the reconcile outcome by what the account API appended and whether drift
// remains, the disaster recovery confirmation with and without the archived events reconcile found, the replay outcome, and
// the feature flag rollouts choices with Default as no pin
public sealed class AccountAdminActionsTests
{
    private static readonly DateTimeOffset ReconciledAt = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FromReconcile_WhenNothingWasAppendedAndNoDriftRemains_ShouldReportThatTheAccountMatchesStripe()
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var outcome = AccountAdminActionOutcomes.FromReconcile(new ReconcileTenantWithStripeResponse(0, false, 0, ReconciledAt, null));

        // Assert
        outcome.Should().Be(new AccountAdminActionToast(ToastKind.Success, "Reconcile complete", "No new billing events were appended. The account matches Stripe.", false));
    }

    [Fact]
    public void FromReconcile_WhenEventsWereAppended_ShouldReportTheCount()
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var outcome = AccountAdminActionOutcomes.FromReconcile(new ReconcileTenantWithStripeResponse(3, false, 0, ReconciledAt, null));

        // Assert
        outcome.Kind.Should().Be(ToastKind.Success);
        outcome.Title.Should().Be("Reconcile complete");
        outcome.Message.Should().Be($"Appended 3 new billing events. Last reconciled {ReconciledAt.ToLocalTime():M/d/yyyy}.");
        outcome.OffersDisasterRecovery.Should().BeFalse();
    }

    [Fact]
    public void FromReconcile_WhenDriftRemainsWithoutNewEvents_ShouldWarnWithTheDiscrepancyCountAndOfferDisasterRecovery()
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var outcome = AccountAdminActionOutcomes.FromReconcile(new ReconcileTenantWithStripeResponse(0, true, 2, ReconciledAt, null));

        // Assert
        outcome.Kind.Should().Be(ToastKind.Warning);
        outcome.Title.Should().Be("Reconcile complete with drift detected");
        outcome.Message.Should().StartWith("The account has 2 drift discrepancies.");
        outcome.OffersDisasterRecovery.Should().BeTrue();
    }

    [Fact]
    public void FromReconcile_WhenDriftRemainsAfterNewEvents_ShouldReportTheCountUnderTheDriftTitle()
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var outcome = AccountAdminActionOutcomes.FromReconcile(new ReconcileTenantWithStripeResponse(1, true, 1, ReconciledAt, null));

        // Assert
        outcome.Title.Should().Be("Reconcile complete with drift detected");
        outcome.Message.Should().StartWith("Appended 1 new billing events.");
        outcome.OffersDisasterRecovery.Should().BeTrue();
    }

    [Fact]
    public void FromReconcile_InDanish_ShouldUseTheDanishText()
    {
        // Arrange
        using var culture = new CultureScope("da-DK");

        // Act
        var outcome = AccountAdminActionOutcomes.FromReconcile(new ReconcileTenantWithStripeResponse(0, false, 0, ReconciledAt, null));

        // Assert
        outcome.Title.Should().Be("Afstemningen er gennemført");
        outcome.Message.Should().Be("Der blev ikke tilføjet nye faktureringshændelser. Kontoen stemmer med Stripe.");
    }

    [Fact]
    public void GetReplayConfirmation_WhenReconcileFoundArchivedEvents_ShouldQuoteTheirCountAndRange()
    {
        // Arrange
        using var culture = new CultureScope("en-US");
        var oldest = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var newest = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

        // Act
        var text = AccountAdminActionOutcomes.GetReplayConfirmation(new ArchivedEventsAwaitingConfirmation(4, oldest, newest));

        // Assert
        text.Should().StartWith($"Reconcile found 4 archived events older than Stripe's 30-day window, from {oldest.ToLocalTime():M/d/yyyy} to {newest.ToLocalTime():M/d/yyyy}.");
    }

    [Fact]
    public void GetReplayConfirmation_WithoutArchivedEvents_ShouldNameTheCaveatOnly()
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var text = AccountAdminActionOutcomes.GetReplayConfirmation(null);

        // Assert
        text.Should().StartWith("This rebuilds the billing event ledger from this account's archived Stripe events.");
    }

    [Fact]
    public void FromReplay_ShouldReportTheReplayedCount()
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var outcome = AccountAdminActionOutcomes.FromReplay(new ReplayArchivedTenantStripeEventsResponse(5, ReconciledAt));

        // Assert
        outcome.Kind.Should().Be(ToastKind.Success);
        outcome.Title.Should().Be("Disaster recovery complete");
        outcome.Message.Should().Be($"Replayed 5 archived events into the billing event ledger {ReconciledAt.ToLocalTime():M/d/yyyy}.");
    }

    [Theory]
    [InlineData(null, AbInclusionPinChoice.Default)]
    [InlineData(AbInclusionPin.AlwaysOn, AbInclusionPinChoice.AlwaysOn)]
    [InlineData(AbInclusionPin.NeverOn, AbInclusionPinChoice.NeverOn)]
    public void AbInclusionPins_ShouldMapThePinToItsChoiceAndBack(AbInclusionPin? pin, AbInclusionPinChoice choice)
    {
        // Act
        var mappedChoice = AbInclusionPins.ToChoice(pin);
        var mappedPin = AbInclusionPins.ToPin(choice);

        // Assert
        mappedChoice.Should().Be(choice);
        mappedPin.Should().Be(pin);
    }

    [Theory]
    [InlineData(AbInclusionPin.AlwaysOn, "Acme is now first in feature flag rollouts")]
    [InlineData(AbInclusionPin.NeverOn, "Acme is now last in feature flag rollouts")]
    [InlineData(null, "Feature flag rollouts reset to default for Acme")]
    public void GetSavedMessage_ShouldNameTheEntityAndTheSavedChoice(AbInclusionPin? pin, string expected)
    {
        // Arrange
        using var culture = new CultureScope("en-US");

        // Act
        var message = AbInclusionPins.GetSavedMessage("Acme", pin);

        // Assert
        message.Should().Be(expected);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string locale)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
    }
}
