using Blazor.Client.Preferences;
using FluentAssertions;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.Preferences;

// What the notifications section shows for each combination the browser can report, and how one change at a time is kept.
// The section never decides from a click: every state below comes from what the browser and the account API reported.
public sealed class PushNotificationSectionTests
{
    [Fact]
    public void ReadDeviceOwner_WhenTheBrowserHoldsNoSubscription_ShouldReportNone()
    {
        // Act
        var owner = PushNotificationSection.ReadDeviceOwner(false, "psub_01KC0000000000000000000001", ["psub_01KC0000000000000000000001"]);

        // Assert
        owner.Should().Be(PushDeviceOwner.None);
    }

    [Fact]
    public void ReadDeviceOwner_WhenTheAccountHasTheRowThisDeviceStored_ShouldReportThisAccount()
    {
        // Act
        var owner = PushNotificationSection.ReadDeviceOwner(true, "psub_01KC0000000000000000000001", ["psub_01KC0000000000000000000002", "psub_01KC0000000000000000000001"]);

        // Assert
        owner.Should().Be(PushDeviceOwner.ThisAccount);
    }

    [Fact]
    public void ReadDeviceOwner_WhenTheAccountDoesNotHaveTheRowThisDeviceStored_ShouldReportAnotherAccount()
    {
        // Act: what an account that logged out of this browser without the browser being unsubscribed leaves behind
        var owner = PushNotificationSection.ReadDeviceOwner(true, "psub_01KC0000000000000000000001", ["psub_01KC0000000000000000000002"]);

        // Assert
        owner.Should().Be(PushDeviceOwner.AnotherAccount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ReadDeviceOwner_WhenThisDeviceStoredNoIdentifier_ShouldReportUnattributed(string? storedSubscriptionId)
    {
        // Act
        var owner = PushNotificationSection.ReadDeviceOwner(true, storedSubscriptionId, ["psub_01KC0000000000000000000002"]);

        // Assert
        owner.Should().Be(PushDeviceOwner.Unattributed);
    }

    [Fact]
    public void ReadDeviceOwner_WhenTheAccountHasNoSubscriptionsAtAll_ShouldReportAnotherAccount()
    {
        // Act
        var owner = PushNotificationSection.ReadDeviceOwner(true, "psub_01KC0000000000000000000001", []);

        // Assert
        owner.Should().Be(PushDeviceOwner.AnotherAccount);
    }

    [Fact]
    public void IsVisible_BeforeTheBrowserHasBeenRead_ShouldStayHidden()
    {
        // Arrange
        var section = new PushNotificationSection();

        // Assert
        section.IsVisible.Should().BeFalse();
        section.Notice.Should().BeNull();
        section.IsSwitchDisabled.Should().BeTrue();
        section.IsTestDisabled.Should().BeTrue();
    }

    [Fact]
    public void Load_WhenTheBrowserSupportsNotificationsAndIsSubscribed_ShouldEnableBothControls()
    {
        // Arrange
        var section = new PushNotificationSection();

        // Act
        section.Load(true, PushPermission.Granted, true);

        // Assert
        section.IsVisible.Should().BeTrue();
        section.IsSubscribed.Should().BeTrue();
        section.IsSwitchDisabled.Should().BeFalse();
        section.IsTestDisabled.Should().BeFalse();
        section.Notice.Should().BeNull();
    }

    [Fact]
    public void Load_WhenThePermissionHasNotBeenAnswered_ShouldAllowSubscribingAndRefuseTheTest()
    {
        // Arrange
        var section = new PushNotificationSection();

        // Act
        section.Load(true, PushPermission.Default, false);

        // Assert
        section.IsSwitchDisabled.Should().BeFalse();
        section.IsTestDisabled.Should().BeTrue();
        section.Notice.Should().BeNull();
    }

    [Fact]
    public void Load_WhenThePermissionIsDenied_ShouldRefuseTheSwitchAndSayWhereToChangeIt()
    {
        // Arrange
        var section = new PushNotificationSection();

        // Act
        section.Load(true, PushPermission.Denied, false);

        // Assert
        section.IsBlocked.Should().BeTrue();
        section.IsSwitchDisabled.Should().BeTrue();
        section.Notice.Should().Be(AccountStrings.NotificationsBlocked);
    }

    [Fact]
    public void Load_WhenThePermissionIsDeniedWhileTheBrowserStillHoldsTheSubscription_ShouldReadOffAndSayItIsBlocked()
    {
        // Arrange
        var section = new PushNotificationSection();

        // Act: what Safari reports after the user denies the permission in its settings for a device that was subscribed
        section.Load(true, PushPermission.Denied, true);

        // Assert
        section.IsSubscribed.Should().BeFalse();
        section.IsSwitchDisabled.Should().BeTrue();
        section.IsTestDisabled.Should().BeTrue();
        section.Notice.Should().Be(AccountStrings.NotificationsBlocked);
    }

    [Fact]
    public void Load_WhenTheBrowserHasNoSupport_ShouldRefuseEverythingAndSayWhy()
    {
        // Arrange
        var section = new PushNotificationSection();

        // Act
        section.Load(false, PushPermission.Default, true);

        // Assert
        section.IsSupported.Should().BeFalse();
        section.IsSubscribed.Should().BeFalse();
        section.IsSwitchDisabled.Should().BeTrue();
        section.IsTestDisabled.Should().BeTrue();
        section.Notice.Should().Be(AccountStrings.NotificationsNotSupported);
    }

    [Fact]
    public void TryBeginChange_WhenAChangeIsAlreadyRunning_ShouldStartNothingElse()
    {
        // Arrange
        var section = new PushNotificationSection();
        section.Load(true, PushPermission.Granted, true);

        // Act
        var first = section.TryBeginChange();
        var second = section.TryBeginChange();

        // Assert
        first.Should().BeTrue();
        second.Should().BeFalse();
        section.IsBusy.Should().BeTrue();
        section.IsSwitchDisabled.Should().BeTrue();
        section.IsTestDisabled.Should().BeTrue();

        section.EndChange();
        section.TryBeginChange().Should().BeTrue();
    }

    [Fact]
    public void TryBeginChange_BeforeTheBrowserHasBeenRead_ShouldStartNothing()
    {
        // Arrange
        var section = new PushNotificationSection();

        // Assert
        section.TryBeginChange().Should().BeFalse();
    }

    [Fact]
    public void Reset_WhenTheAccountIsLeft_ShouldHideTheSectionAndForgetTheState()
    {
        // Arrange
        var section = new PushNotificationSection();
        section.Load(true, PushPermission.Granted, true);
        section.TryBeginChange();

        // Act
        section.Reset();

        // Assert
        section.IsVisible.Should().BeFalse();
        section.IsSubscribed.Should().BeFalse();
        section.IsBusy.Should().BeFalse();
        section.Permission.Should().Be(PushPermission.Default);
    }

    [Theory]
    [InlineData("granted", PushPermission.Granted)]
    [InlineData("denied", PushPermission.Denied)]
    [InlineData("default", PushPermission.Default)]
    [InlineData("", PushPermission.Default)]
    [InlineData(null, PushPermission.Default)]
    public void ParsePermission_WhenTheBrowserReportsAValue_ShouldMapItOrFallBackToDefault(string? permission, PushPermission expected)
    {
        // Assert
        PushNotificationSection.ParsePermission(permission).Should().Be(expected);
    }

    [Theory]
    [InlineData("subscribed", PushSubscribeOutcome.Subscribed)]
    [InlineData("denied", PushSubscribeOutcome.Denied)]
    [InlineData("dismissed", PushSubscribeOutcome.Dismissed)]
    [InlineData("unsupported", PushSubscribeOutcome.Unsupported)]
    [InlineData("something else", PushSubscribeOutcome.Failed)]
    [InlineData(null, PushSubscribeOutcome.Failed)]
    public void ParseOutcome_WhenTheModuleReportsAnOutcome_ShouldMapItOrFallBackToFailed(string? outcome, PushSubscribeOutcome expected)
    {
        // Assert
        PushNotificationSection.ParseOutcome(outcome).Should().Be(expected);
    }
}
