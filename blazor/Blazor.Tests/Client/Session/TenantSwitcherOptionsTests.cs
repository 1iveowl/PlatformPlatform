using Account.Features.Tenants.Queries;
using Blazor.Client.Session;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.Session;

public sealed class TenantSwitcherOptionsTests
{
    private static readonly UserId UserId = new("usr_01KC0CURRENT0000000000000A");

    [Fact]
    public void IsVisible_WhenUserHasOneTenant_ShouldBeFalse()
    {
        // Arrange
        var options = TenantSwitcherOptions.Create([new TenantInfo(new TenantId(1), "Acme", UserId, null, false)], new TenantId(1));

        // Act
        var isVisible = TenantSwitcherOptions.IsVisible(options);

        // Assert
        isVisible.Should().BeFalse();
    }

    [Fact]
    public void IsVisible_WhenUserHasNoTenants_ShouldBeFalse()
    {
        // Act
        var isVisible = TenantSwitcherOptions.IsVisible(TenantSwitcherOptions.Create([], null));

        // Assert
        isVisible.Should().BeFalse();
    }

    [Fact]
    public void IsVisible_WhenUserHasTwoTenants_ShouldBeTrueWithTheCurrentAndPendingOnesMarked()
    {
        // Arrange
        TenantInfo[] tenants = [new(new TenantId(1), "Acme", UserId, null, false), new(new TenantId(2), "Globex", UserId, null, true)];

        // Act
        var options = TenantSwitcherOptions.Create(tenants, new TenantId(2));

        // Assert
        TenantSwitcherOptions.IsVisible(options).Should().BeTrue();
        options.Should().Equal(new TenantSwitcherOption(new TenantId(1), "Acme", false), new TenantSwitcherOption(new TenantId(2), "Globex", true, true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetDisplayName_WhenTenantHasNoName_ShouldUseTheLocalizedPlaceholder(string? tenantName)
    {
        // Act
        var displayName = TenantSwitcherOptions.GetDisplayName(tenantName);

        // Assert
        displayName.Should().Be(AccountStrings.UnnamedAccount);
    }

    [Fact]
    public void GetDisplayName_WhenTenantHasName_ShouldUseTheName()
    {
        // Act
        var displayName = TenantSwitcherOptions.GetDisplayName("Acme");

        // Assert
        displayName.Should().Be("Acme");
    }
}
