using Blazor.Client.Settings;
using FluentAssertions;

namespace Blazor.Tests.Client.Settings;

// The letters shown in place of an account logo, matching the React edition's getTenantInitials
public sealed class TenantInitialsTests
{
    [Theory]
    [InlineData("Acme", "AC")]
    [InlineData("acme", "AC")]
    [InlineData("A", "A")]
    [InlineData("Acme Corporation", "AC")]
    [InlineData("Acme Holding Corporation", "AC")]
    [InlineData("  Acme   Corp  ", "AC")]
    [InlineData("ærø bådværft", "ÆB")]
    public void Of_WhenTheNameHasLetters_ShouldTakeTheFirstLetters(string tenantName, string expected)
    {
        // Act and Assert
        TenantInitials.Of(tenantName).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Of_WhenTheNameIsBlank_ShouldBeAQuestionMark(string? tenantName)
    {
        // Act and Assert
        TenantInitials.Of(tenantName).Should().Be("?");
    }
}
