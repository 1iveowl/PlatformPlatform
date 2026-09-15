using System.ComponentModel.DataAnnotations;
using Blazor.Host.Components.Pages.App;
using Blazor.Host.Components.Pages.Public;
using FluentAssertions;

namespace Blazor.Tests.Public;

public sealed class PublicFormsTests
{
    [Theory]
    [InlineData("ann@example.com", true)]
    [InlineData("", false)]
    [InlineData("invalid-email", false)]
    public void EmailForm_WhenValidated_ShouldMirrorEmailRule(string email, bool isValid)
    {
        // Act & Assert
        Validate(new EmailForm { Email = email }).Should().Be(isValid);
    }

    [Fact]
    public void EmailForm_WhenLongerThanOneHundredCharacters_ShouldBeInvalid()
    {
        // Act & Assert
        Validate(new EmailForm { Email = $"{new string('a', 88)}@example.com" }).Should().BeTrue();
        Validate(new EmailForm { Email = $"{new string('a', 89)}@example.com" }).Should().BeFalse();
    }

    [Theory]
    [InlineData("ABCDEF", true)]
    [InlineData("abcdef", true)]
    [InlineData("", false)]
    [InlineData("ABCDE", false)]
    [InlineData("ABCDEFG", false)]
    [InlineData("WRONG1", false)]
    public void OneTimePasswordForm_WhenValidated_ShouldAcceptSixLettersInAnyCase(string code, bool isValid)
    {
        // Act & Assert
        Validate(new OneTimePasswordForm { OneTimePassword = code }).Should().Be(isValid);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(0, false)]
    [InlineData(31, false)]
    public void AccountSetupForm_WhenValidated_ShouldAllowOneToThirtyCharacters(int length, bool isValid)
    {
        // Act & Assert
        Validate(new AccountSetupForm { Name = new string('A', length) }).Should().Be(isValid);
    }

    [Theory]
    [InlineData(30, 30, 50, true)]
    [InlineData(1, 1, 0, true)]
    [InlineData(31, 5, 5, false)]
    [InlineData(5, 0, 5, false)]
    [InlineData(5, 5, 51, false)]
    public void ProfileSetupForm_WhenValidated_ShouldApplyNameAndTitleLimits(int firstName, int lastName, int title, bool isValid)
    {
        // Arrange
        var form = new ProfileSetupForm { FirstName = new string('A', firstName), LastName = new string('B', lastName), Title = new string('C', title) };

        // Act & Assert
        Validate(form).Should().Be(isValid);
    }

    private static bool Validate(object model)
    {
        return Validator.TryValidateObject(model, new ValidationContext(model), new List<ValidationResult>(), true);
    }
}
