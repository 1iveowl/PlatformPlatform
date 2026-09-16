using Account.Client;
using Account.Features.Authentication.Queries;
using FluentAssertions;
using SharedKernel.Domain;
using Xunit;
using FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags;

namespace Account.Tests.Client;

public sealed class FeatureFlagStateTests
{
    [Fact]
    public void Initialize_WhenAuthenticated_ShouldExposeSystemAndEvaluatedFlagsAndIdentity()
    {
        // Arrange
        var featureFlagState = new FeatureFlagState();
        var bootstrap = CreateAuthenticatedBootstrap(UserId.NewId(), new TenantId(1), [" compact-view", "beta-features"], true);

        // Act
        featureFlagState.Initialize(bootstrap);

        // Assert
        featureFlagState.UserId.Should().Be(bootstrap.User!.Id);
        featureFlagState.TenantId.Should().Be(new TenantId(1));
        featureFlagState.IsEnabled(FeatureFlagRegistry.GoogleOauth).Should().BeTrue();
        featureFlagState.IsEnabled(FeatureFlagRegistry.Subscriptions).Should().BeFalse();
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeTrue();
        featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeTrue();
        featureFlagState.IsEnabled(FeatureFlagRegistry.ExperimentalUi).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_WhenSystemFlagKeyIsAlsoInEvaluatedFlags_ShouldReadSystemFlagsOnly()
    {
        // Arrange
        var featureFlagState = new FeatureFlagState();
        featureFlagState.Initialize(CreateAuthenticatedBootstrap(UserId.NewId(), new TenantId(1), [FeatureFlagRegistry.GoogleOauth.Key], false));

        // Act
        var isEnabled = featureFlagState.IsEnabled(FeatureFlagRegistry.GoogleOauth);

        // Assert
        isEnabled.Should().BeFalse();
    }

    [Fact]
    public void Initialize_WhenAnonymous_ShouldKeepSystemFlagsAndClearEvaluatedFlagsAndIdentity()
    {
        // Arrange
        var featureFlagState = new FeatureFlagState();
        featureFlagState.Initialize(CreateAuthenticatedBootstrap(UserId.NewId(), new TenantId(1), ["compact-view"], false));

        // Act
        featureFlagState.Initialize(CreateAnonymousBootstrap(true));

        // Assert
        featureFlagState.UserId.Should().BeNull();
        featureFlagState.TenantId.Should().BeNull();
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeFalse();
        featureFlagState.IsEnabled(FeatureFlagRegistry.GoogleOauth).Should().BeTrue();
    }

    [Fact]
    public void ApplyHeader_WhenHeaderIsMissing_ShouldLeaveStateUnchanged()
    {
        // Arrange
        var featureFlagState = CreateAuthenticatedState(["compact-view"]);
        var changedCount = 0;
        featureFlagState.Changed += () => changedCount++;

        // Act
        featureFlagState.ApplyHeader(null);

        // Assert
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeTrue();
        changedCount.Should().Be(0);
    }

    [Fact]
    public void ApplyHeader_WhenHeaderIsEmpty_ShouldClearEvaluatedFlagsOnly()
    {
        // Arrange
        var featureFlagState = CreateAuthenticatedState(["compact-view"]);
        var changedCount = 0;
        featureFlagState.Changed += () => changedCount++;

        // Act
        featureFlagState.ApplyHeader("");

        // Assert
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeFalse();
        featureFlagState.IsEnabled(FeatureFlagRegistry.GoogleOauth).Should().BeTrue();
        featureFlagState.UserId.Should().NotBeNull();
        changedCount.Should().Be(1);
    }

    [Fact]
    public void ApplyHeader_WhenHeaderHasWhitespaceDuplicatesAndEmptyEntries_ShouldNormalizeAndReplaceEvaluatedFlags()
    {
        // Arrange
        var featureFlagState = CreateAuthenticatedState(["beta-features"]);
        var changedCount = 0;
        featureFlagState.Changed += () => changedCount++;

        // Act
        featureFlagState.ApplyHeader(" experimental-ui ,compact-view,, compact-view ,");

        // Assert
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeTrue();
        featureFlagState.IsEnabled(FeatureFlagRegistry.ExperimentalUi).Should().BeTrue();
        featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeFalse();
        changedCount.Should().Be(1);
    }

    [Fact]
    public void ApplyHeader_WhenNormalizedFlagsAreUnchanged_ShouldNotRaiseChanged()
    {
        // Arrange
        var featureFlagState = CreateAuthenticatedState(["compact-view", "experimental-ui"]);
        var changedCount = 0;
        featureFlagState.Changed += () => changedCount++;

        // Act
        featureFlagState.ApplyHeader("experimental-ui, compact-view, experimental-ui");

        // Assert
        changedCount.Should().Be(0);
    }

    [Fact]
    public void ApplyHeader_WhenNotInitialized_ShouldIgnoreHeader()
    {
        // Arrange
        var featureFlagState = new FeatureFlagState();
        var changedCount = 0;
        featureFlagState.Changed += () => changedCount++;

        // Act
        featureFlagState.ApplyHeader("compact-view");

        // Assert
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeFalse();
        changedCount.Should().Be(0);
    }

    [Fact]
    public void ApplyHeader_WhenAnonymous_ShouldIgnoreHeader()
    {
        // Arrange
        var featureFlagState = new FeatureFlagState();
        featureFlagState.Initialize(CreateAnonymousBootstrap(false));

        // Act
        featureFlagState.ApplyHeader("compact-view");

        // Assert
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeFalse();
    }

    [Fact]
    public void Reset_WhenInitialized_ShouldClearAllFlagsAndIdentityAndIgnoreLaterHeaders()
    {
        // Arrange
        var featureFlagState = CreateAuthenticatedState(["compact-view"]);
        var changedCount = 0;
        featureFlagState.Changed += () => changedCount++;

        // Act
        featureFlagState.Reset();
        featureFlagState.ApplyHeader("compact-view");

        // Assert
        featureFlagState.UserId.Should().BeNull();
        featureFlagState.TenantId.Should().BeNull();
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeFalse();
        featureFlagState.IsEnabled(FeatureFlagRegistry.GoogleOauth).Should().BeFalse();
        changedCount.Should().Be(1);
    }

    [Fact]
    public void Initialize_WhenAnotherUserSignsIn_ShouldNotKeepPreviousUserFlags()
    {
        // Arrange
        var featureFlagState = CreateAuthenticatedState(["compact-view"]);
        featureFlagState.ApplyHeader("compact-view,experimental-ui");
        var otherUserId = UserId.NewId();

        // Act
        featureFlagState.Initialize(CreateAuthenticatedBootstrap(otherUserId, new TenantId(1), ["beta-features"], true));

        // Assert
        featureFlagState.UserId.Should().Be(otherUserId);
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeFalse();
        featureFlagState.IsEnabled(FeatureFlagRegistry.ExperimentalUi).Should().BeFalse();
        featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeTrue();
    }

    [Fact]
    public void Initialize_WhenSameUserSwitchesTenant_ShouldReplaceTenantAndEvaluatedFlagsAndRaiseChanged()
    {
        // Arrange
        var userId = UserId.NewId();
        var featureFlagState = new FeatureFlagState();
        featureFlagState.Initialize(CreateAuthenticatedBootstrap(userId, new TenantId(1), ["account-overview", "compact-view"], true));
        var changedCount = 0;
        featureFlagState.Changed += () => changedCount++;

        // Act
        featureFlagState.Initialize(CreateAuthenticatedBootstrap(userId, new TenantId(2), ["compact-view"], true));

        // Assert
        featureFlagState.TenantId.Should().Be(new TenantId(2));
        featureFlagState.IsEnabled(FeatureFlagRegistry.AccountOverview).Should().BeFalse();
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeTrue();
        changedCount.Should().Be(1);
    }

    [Fact]
    public void Initialize_WhenBootstrapIsUnchanged_ShouldNotRaiseChanged()
    {
        // Arrange
        var userId = UserId.NewId();
        var featureFlagState = new FeatureFlagState();
        featureFlagState.Initialize(CreateAuthenticatedBootstrap(userId, new TenantId(1), ["compact-view"], true));
        var changedCount = 0;
        featureFlagState.Changed += () => changedCount++;

        // Act
        featureFlagState.Initialize(CreateAuthenticatedBootstrap(userId, new TenantId(1), ["compact-view"], true));

        // Assert
        changedCount.Should().Be(0);
    }

    private static FeatureFlagState CreateAuthenticatedState(string[] featureFlags)
    {
        var featureFlagState = new FeatureFlagState();
        featureFlagState.Initialize(CreateAuthenticatedBootstrap(UserId.NewId(), new TenantId(1), featureFlags, true));
        return featureFlagState;
    }

    private static BootstrapResponse CreateAuthenticatedBootstrap(UserId userId, TenantId tenantId, string[] featureFlags, bool googleOauthEnabled)
    {
        var user = new BootstrapUser(userId, tenantId, "Owner", "owner@example.com", "Ada", "Lovelace", null, null, "Acme", null, "Free", false, featureFlags);
        return new BootstrapResponse(true, user, "en-US", new Dictionary<string, string>(), CreateSystemFeatureFlags(googleOauthEnabled), "antiforgery-token");
    }

    private static BootstrapResponse CreateAnonymousBootstrap(bool googleOauthEnabled)
    {
        return new BootstrapResponse(false, null, "en-US", new Dictionary<string, string>(), CreateSystemFeatureFlags(googleOauthEnabled), "antiforgery-token");
    }

    private static Dictionary<string, bool> CreateSystemFeatureFlags(bool googleOauthEnabled)
    {
        return new Dictionary<string, bool>
        {
            [FeatureFlagRegistry.GoogleOauth.Key] = googleOauthEnabled,
            [FeatureFlagRegistry.Subscriptions.Key] = false
        };
    }
}
