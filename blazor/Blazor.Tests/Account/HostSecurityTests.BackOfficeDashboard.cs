using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.Tests.Account;

// The back office's dashboard, access-denied and not-found pages on the back-office host. The dashboard prerenders the shell
// and the dashboard's frame for the forwarded identity, with the billing parts only when BACK_OFFICE_SUBSCRIPTION_ENABLED is
// "true"; the fixture sets it, and one test starts a second host with it off.
public sealed partial class HostSecurityTests
{
    private static readonly string[] BillingMarkers =
    [
        "data-testid=\"sidebar-nav-invoices\"", "data-testid=\"sidebar-nav-billing-events\"", "data-testid=\"kpi-blended-mrr\"",
        "data-testid=\"kpi-total-revenue\"", "data-testid=\"recent-payments\"", "data-testid=\"recent-stripe-events\""
    ];

    [Fact]
    public async Task BackOfficeDashboard_WhenTheSubscriptionSettingIsOn_ShouldPrerenderTheShellTheTilesAndEveryCard()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest(BackOfficePage, fixture.ProtectBackOfficeIdentity("Admin", true));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-shell\"").And.Contain("data-testid=\"back-office-dashboard\"");
        html.Should().Contain("aria-current=\"page\" data-testid=\"sidebar-nav-dashboard\"").And.Contain("data-testid=\"sidebar-nav-feature-flags\"");
        html.Should().Contain("data-testid=\"recent-signups\"").And.Contain("data-testid=\"recent-logins\"");
        html.Should().Contain("href=\"/blazor/back-office/accounts?orderBy=CreatedAt\"");
        foreach (var marker in BillingMarkers)
        {
            html.Should().Contain(marker);
        }

        html.Should().NotContain("sidebar-nav-components");
    }

    [Fact]
    public async Task BackOfficeDashboard_WhenTheSubscriptionSettingIsOff_ShouldLeaveOutTheBillingGroupTilesAndCards()
    {
        // Arrange
        await using var host = await fixture.StartAdditionalHostAsync("--BACK_OFFICE_SUBSCRIPTION_ENABLED", "false");
        var dataProtectionProvider = host.Host.Services.GetRequiredService<IDataProtectionProvider>();
        using var request = HostFixture.CreateBackOfficeRequest(BackOfficePage, fixture.ProtectBackOfficeIdentity("Admin", true, dataProtectionProvider));

        // Act
        using var response = await host.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-dashboard\"").And.Contain("data-testid=\"recent-signups\"").And.Contain("data-testid=\"kpi-total-accounts\"");
        foreach (var marker in BillingMarkers)
        {
            html.Should().NotContain(marker);
        }
    }

    [Fact]
    public async Task BackOfficeNotFound_WhenAnUnknownBackOfficePathIsRequested_ShouldAnswer404InsideTheBackOffice()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/unknown-page", fixture.ProtectBackOfficeIdentity("Admin", true));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-not-found\"").And.Contain("data-testid=\"not-found-home\"");
        html.Should().Contain("href=\"/blazor/back-office\"");
        html.Should().Contain("data-back-office=\"\"");
    }

    [Fact]
    public async Task BackOfficeNotFound_WhenNoIdentityIsForwarded_ShouldRedirectToThePlatformLogin()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/invoices", null);

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/.auth/login/aad?post_login_redirect_uri=%2Fblazor%2Fback-office%2Finvoices");
    }

    [Fact]
    public async Task BackOfficeAccessDenied_WhenRendered_ShouldOfferHomeAndThePlatformLogout()
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/access-denied", fixture.ProtectBackOfficeIdentity("User", false));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-access-denied\"");
        html.Should().Contain("href=\"/.auth/logout\"").And.Contain("href=\"/blazor/back-office\"");
    }

    [Theory]
    [InlineData("blazor/back-office/access-denied")]
    [InlineData("blazor/back-office/does-not-exist")]
    public async Task BackOfficeStatusPage_WhenRequestedOnTheAppHost_ShouldNotBeFound(string path)
    {
        // Arrange: the app host, with a back-office identity that only the back-office host would accept
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Back-Office-Identity", fixture.ProtectBackOfficeIdentity("Admin", true));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        (await response.Content.ReadAsStringAsync()).Should().NotContain("back-office-not-found").And.NotContain("back-office-access-denied");
    }
}
