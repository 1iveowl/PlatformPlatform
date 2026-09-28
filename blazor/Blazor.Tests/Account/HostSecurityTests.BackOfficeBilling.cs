using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.Tests.Account;

// The back office's invoices and billing events lists on the back-office host. With BACK_OFFICE_SUBSCRIPTION_ENABLED "true",
// which the fixture sets, each page prerenders the shell and the list's frame with its item current in the Billing group;
// with it off each answers the back office's not-found page with status 404, as the React requireSubscriptionEnabled guard
// does, and a second host started with the setting off shows it. Neither page is served on another host.
public sealed partial class HostSecurityTests
{
    [Theory]
    [InlineData("invoices", "back-office-invoices", "invoices-grid")]
    [InlineData("billing-events", "back-office-billing-events", "billing-events-grid")]
    public async Task BackOfficeBillingList_WhenTheSubscriptionSettingIsOn_ShouldPrerenderTheShellAndTheListFrame(string page, string surfaceTestId, string gridTestId)
    {
        // Arrange
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/{page}?view=all", fixture.ProtectBackOfficeIdentity("User", false));

        // Act
        using var response = await fixture.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-shell\"").And.Contain($"data-testid=\"{surfaceTestId}\"").And.Contain($"data-testid=\"{gridTestId}\"");
        html.Should().Contain($"aria-current=\"page\" data-testid=\"sidebar-nav-{page}\"");
        html.Should().NotContain("data-testid=\"back-office-not-found\"");
    }

    [Theory]
    [InlineData("invoices")]
    [InlineData("billing-events")]
    public async Task BackOfficeBillingList_WhenTheSubscriptionSettingIsOff_ShouldAnswer404WithTheBackOfficeNotFoundPage(string page)
    {
        // Arrange
        await using var host = await fixture.StartAdditionalHostAsync("--BACK_OFFICE_SUBSCRIPTION_ENABLED", "false");
        var dataProtectionProvider = host.Host.Services.GetRequiredService<IDataProtectionProvider>();
        using var request = HostFixture.CreateBackOfficeRequest($"{BackOfficePage}/{page}", fixture.ProtectBackOfficeIdentity("Admin", true, dataProtectionProvider));

        // Act
        using var response = await host.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"back-office-not-found\"").And.Contain("data-testid=\"not-found-home\"");
        html.Should().NotContain("data-testid=\"invoices-grid\"").And.NotContain("data-testid=\"billing-events-grid\"").And.NotContain("sidebar-nav-invoices");
    }
}
