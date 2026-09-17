using Blazor.Client.FeatureFlags;
using FluentAssertions;

namespace Blazor.Tests.Client.FeatureFlags;

public sealed class FeatureFlagSectionTests
{
    [Fact]
    public void IsVisible_BeforeTheListIsRead_ShouldBeFalse()
    {
        // Arrange
        var section = new FeatureFlagSection(FeatureFlagSectionScope.Tenant);

        // Assert
        section.IsVisible.Should().BeFalse();
        section.Rows.Should().BeEmpty();
    }

    [Fact]
    public void IsVisible_WhenTheListIsEmpty_ShouldStayFalse()
    {
        // Arrange
        var section = new FeatureFlagSection(FeatureFlagSectionScope.Tenant);

        // Act
        section.Load([]);

        // Assert
        section.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Load_WhenTheListHasFlags_ShouldKeepTheOrderAndTheReportedState()
    {
        // Arrange
        var section = new FeatureFlagSection(FeatureFlagSectionScope.User);

        // Act
        section.Load([("compact-view", true), ("account-overview", false)]);

        // Assert
        section.IsVisible.Should().BeTrue();
        section.Rows.Select(row => row.Key).Should().Equal("compact-view", "account-overview");
        section.Rows.Select(row => row.Enabled).Should().Equal(true, false);
        section.Rows[0].Name.Should().Be("Compact view");
        section.Rows[0].Description.Should().Be("Reduce spacing between UI elements for a denser layout");
    }

    [Fact]
    public void Load_WhenAKeyIsNotInTheRegistry_ShouldDropItRatherThanShowTheKey()
    {
        // Arrange
        var section = new FeatureFlagSection(FeatureFlagSectionScope.Tenant);

        // Act
        section.Load([("account-overview", true), ("retired-flag", true)]);

        // Assert
        section.Rows.Select(row => row.Key).Should().Equal("account-overview");
    }

    [Fact]
    public void Load_WhenReadAgainAfterAChange_ShouldReplaceTheState()
    {
        // Arrange
        var section = new FeatureFlagSection(FeatureFlagSectionScope.Tenant);
        section.Load([("account-overview", false)]);

        // Act
        section.Load([("account-overview", true)]);

        // Assert
        section.Rows.Should().ContainSingle().Which.Enabled.Should().BeTrue();
    }

    [Fact]
    public void TryBeginToggle_WhenAToggleIsAlreadyRunning_ShouldRefuseTheSecond()
    {
        // Arrange
        var section = new FeatureFlagSection(FeatureFlagSectionScope.User);
        section.Load([("compact-view", false)]);

        // Act
        var first = section.TryBeginToggle("compact-view");
        var second = section.TryBeginToggle("compact-view");

        // Assert
        first.Should().BeTrue();
        second.Should().BeFalse();
        section.IsBusy.Should().BeTrue();
        section.PendingKey.Should().Be("compact-view");
    }

    [Fact]
    public void TryBeginToggle_WhenTheKeyIsNotShown_ShouldRefuse()
    {
        // Arrange
        var section = new FeatureFlagSection(FeatureFlagSectionScope.User);
        section.Load([("compact-view", false)]);

        // Act
        var started = section.TryBeginToggle("account-overview");

        // Assert
        started.Should().BeFalse();
        section.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void EndToggle_WhenTheChangeFinished_ShouldAllowTheNextOne()
    {
        // Arrange
        var section = new FeatureFlagSection(FeatureFlagSectionScope.User);
        section.Load([("compact-view", false)]);
        section.TryBeginToggle("compact-view");

        // Act
        section.EndToggle();

        // Assert
        section.IsBusy.Should().BeFalse();
        section.TryBeginToggle("compact-view").Should().BeTrue();
    }

    [Fact]
    public void Reset_WhenTheAccountIsLeft_ShouldHideTheSectionAndDropTheRows()
    {
        // Arrange
        var section = new FeatureFlagSection(FeatureFlagSectionScope.Tenant);
        section.Load([("account-overview", true)]);
        section.TryBeginToggle("account-overview");

        // Act
        section.Reset();

        // Assert
        section.IsVisible.Should().BeFalse();
        section.Rows.Should().BeEmpty();
        section.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void Texts_WhenTheScopeIsTheTenant_ShouldBeTheFeaturesSectionTexts()
    {
        // Arrange
        var section = new FeatureFlagSection(FeatureFlagSectionScope.Tenant);

        // Assert
        section.Heading.Should().Be("Features");
        section.Description.Should().Be("Toggle features available to your account.");
        section.ToastTitle.Should().Be("Feature updated successfully");
        section.ToastTestId.Should().Be(FeatureFlagSection.TenantToastTestId);
        section.SectionTestId.Should().Be(FeatureFlagSection.TenantSectionTestId);
        section.HeadingId.Should().Be($"{FeatureFlagSection.TenantSectionTestId}-heading");
    }

    [Fact]
    public void Texts_WhenTheScopeIsTheUser_ShouldBeTheFeaturePreferencesTexts()
    {
        // Arrange
        var section = new FeatureFlagSection(FeatureFlagSectionScope.User);

        // Assert
        section.Heading.Should().Be("Feature preferences");
        section.Description.Should().Be("Customize which optional features are enabled for your account.");
        section.ToastTitle.Should().Be("Preference updated successfully");
        section.ToastTestId.Should().Be(FeatureFlagSection.UserToastTestId);
        section.SectionTestId.Should().Be(FeatureFlagSection.UserSectionTestId);
    }

    [Fact]
    public void UpdatedDetail_WhenAFlagChanged_ShouldNameTheFlagAndTheFiveMinuteDelay()
    {
        // Act
        var detail = FeatureFlagSection.UpdatedDetail("Compact view");

        // Assert
        detail.Should().Be("Compact view. It takes up to 5 minutes for changes to reach all users.");
    }
}
