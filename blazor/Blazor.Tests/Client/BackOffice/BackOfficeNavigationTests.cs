using Blazor.Client.BackOffice.Shell;
using FluentAssertions;

namespace Blazor.Tests.Client.BackOffice;

public sealed class BackOfficeNavigationTests
{
    private const string Origin = "https://back-office.dev.localhost:9001";

    [Fact]
    public void Create_WhenSubscriptionIsEnabled_ShouldIncludeTheBillingGroupBetweenNavigationAndPlatform()
    {
        // Act
        var items = BackOfficeNavigation.Create(true, $"{Origin}/blazor/back-office");

        // Assert
        items.Select(item => (item.Group, item.Target)).Should().Equal(
            (BackOfficeNavigationGroup.Navigation, BackOfficeNavigationTarget.Dashboard),
            (BackOfficeNavigationGroup.Navigation, BackOfficeNavigationTarget.Accounts),
            (BackOfficeNavigationGroup.Navigation, BackOfficeNavigationTarget.Users),
            (BackOfficeNavigationGroup.Billing, BackOfficeNavigationTarget.Invoices),
            (BackOfficeNavigationGroup.Billing, BackOfficeNavigationTarget.BillingEvents),
            (BackOfficeNavigationGroup.Platform, BackOfficeNavigationTarget.FeatureFlags)
        );
    }

    [Fact]
    public void Create_WhenSubscriptionIsDisabled_ShouldOmitTheBillingGroup()
    {
        // Act
        var items = BackOfficeNavigation.Create(false, $"{Origin}/blazor/back-office");

        // Assert
        items.Select(item => item.Target).Should().Equal(
            BackOfficeNavigationTarget.Dashboard, BackOfficeNavigationTarget.Accounts, BackOfficeNavigationTarget.Users, BackOfficeNavigationTarget.FeatureFlags
        );
    }

    [Fact]
    public void Create_ShouldBuildRootAbsoluteHrefsBelowTheBackOfficeHome()
    {
        // Act
        var items = BackOfficeNavigation.Create(true, $"{Origin}/blazor/back-office");

        // Assert
        items.Select(item => item.Href).Should().Equal(
            "/blazor/back-office", "/blazor/back-office/accounts", "/blazor/back-office/users", "/blazor/back-office/invoices",
            "/blazor/back-office/billing-events", "/blazor/back-office/feature-flags"
        );
    }

    [Theory]
    [InlineData("/blazor/back-office", BackOfficeNavigationTarget.Dashboard)]
    [InlineData("/blazor/back-office/?x=1", BackOfficeNavigationTarget.Dashboard)]
    [InlineData("/blazor/back-office/accounts?driftDetected=true", BackOfficeNavigationTarget.Accounts)]
    [InlineData("/blazor/back-office/accounts/4711", BackOfficeNavigationTarget.Accounts)]
    [InlineData("/blazor/back-office/billing-events", BackOfficeNavigationTarget.BillingEvents)]
    public void Create_ShouldMarkTheItemOfTheCurrentPageAndItsSubpagesOnly(string path, BackOfficeNavigationTarget expected)
    {
        // Act
        var items = BackOfficeNavigation.Create(true, $"{Origin}{path}");

        // Assert
        items.Where(item => item.IsCurrent).Select(item => item.Target).Should().Equal(expected);
    }

    [Theory]
    [InlineData("/blazor/back-office/accountsx")]
    [InlineData("/blazor/back-office/identity")]
    public void Create_WhenThePageHasNoMenuEntry_ShouldMarkNothing(string path)
    {
        // Act
        var items = BackOfficeNavigation.Create(true, $"{Origin}{path}");

        // Assert
        items.Should().NotContain(item => item.IsCurrent);
    }
}
