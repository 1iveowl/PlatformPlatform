using System.Security.Claims;
using Blazor.Client;
using Blazor.Host.Components.Pages.App;
using FluentAssertions;

namespace Blazor.Tests.Public;

public sealed class WelcomeGateTests
{
    [Theory]
    [InlineData("Owner", "", "Ann", WelcomeStep.Account)]
    [InlineData("Owner", "", "", WelcomeStep.Account)]
    [InlineData("Owner", "Acme", "", WelcomeStep.Profile)]
    [InlineData("Member", "", "", WelcomeStep.Profile)]
    [InlineData("Member", "", "Ann", WelcomeStep.Done)]
    [InlineData("Owner", "Acme", "Ann", WelcomeStep.Done)]
    public void GetStep_WhenClaims_ShouldRequireAccountForUnnamedOwnerThenProfile(string role, string tenantName, string firstName, WelcomeStep expected)
    {
        // Arrange
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (tenantName.Length > 0) claims.Add(new Claim("tenant_name", tenantName));
        if (firstName.Length > 0) claims.Add(new Claim(ClaimTypes.GivenName, firstName));

        // Act
        var step = WelcomeGate.GetStep(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));

        // Assert
        step.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "/blazor/app")]
    [InlineData("/blazor/app/details?tab=1", "/blazor/app/details?tab=1")]
    [InlineData("//evil.example.com", "/blazor/app")]
    [InlineData("/blazor/welcome", "/blazor/app")]
    [InlineData("/blazor/welcome?returnPath=%2Fblazor%2Fwelcome", "/blazor/app")]
    public void GetDestination_WhenReturnPath_ShouldSanitizeAndNeverReturnWelcome(string? returnPath, string expected)
    {
        // Act & Assert
        WelcomeGate.GetDestination(returnPath).Should().Be(expected);
    }

    [Fact]
    public void GetWelcomeUrl_WhenDestinationValid_ShouldCarryItEscaped()
    {
        // Act & Assert
        WelcomeGate.GetWelcomeUrl("/blazor/app/users/quick").Should().Be("/blazor/welcome?returnPath=%2Fblazor%2Fapp%2Fusers%2Fquick");
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("-1", null)]
    [InlineData("0", null)]
    [InlineData("42; admin", null)]
    public void PreferredTenant_WhenCookieValue_ShouldOnlyReadAPositiveTenantId(string? cookieValue, long? expected)
    {
        // Act
        var tenantId = PreferredTenant.Parse(cookieValue);

        // Assert
        tenantId?.Value.Should().Be(expected);
        (tenantId is null).Should().Be(expected is null);
    }
}
