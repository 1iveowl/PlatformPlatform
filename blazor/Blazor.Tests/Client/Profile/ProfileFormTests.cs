using System.ComponentModel.DataAnnotations;
using Blazor.Client.Profile;
using FluentAssertions;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.Profile;

public sealed class ProfileFormTests
{
    [Fact]
    public void Validate_WhenValuesAreAtTheAccountApiLimits_ShouldBeValid()
    {
        // Arrange
        var form = new ProfileForm { FirstName = new string('a', 30), LastName = "b", Title = new string('t', 50) };

        // Act
        var messages = Validate(form);

        // Assert
        messages.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WhenValuesExceedTheAccountApiLimits_ShouldReturnTheResourceMessages()
    {
        // Arrange
        var form = new ProfileForm { FirstName = new string('a', 31), LastName = new string('b', 31), Title = new string('t', 51) };

        // Act
        var messages = Validate(form);

        // Assert
        messages.Should().BeEquivalentTo(AccountStrings.FirstNameLength, AccountStrings.LastNameLength, AccountStrings.TitleTooLong);
    }

    [Fact]
    public void Validate_WhenNamesAreEmptyOrWhitespace_ShouldReturnTheLengthMessages()
    {
        // Arrange
        var form = new ProfileForm { FirstName = "   ", LastName = "", Title = null };

        // Act
        var messages = Validate(form);

        // Assert
        messages.Should().BeEquivalentTo(AccountStrings.FirstNameLength, AccountStrings.LastNameLength);
    }

    [Fact]
    public void Trimmed_WhenValuesHaveSurroundingWhitespace_ShouldTrimEachFieldAndNotChangeTheForm()
    {
        // Arrange
        var form = new ProfileForm { FirstName = "  Ada ", LastName = "Lovelace ", Title = " Engineer" };

        // Act
        var trimmed = form.Trimmed();

        // Assert
        trimmed.FirstName.Should().Be("Ada");
        trimmed.LastName.Should().Be("Lovelace");
        trimmed.Title.Should().Be("Engineer");
        form.FirstName.Should().Be("  Ada ");
    }

    [Fact]
    public void HasChangesFrom_WhenOnlyAnEmptyTitleDiffersFromNull_ShouldBeFalse()
    {
        // Arrange
        var saved = new ProfileForm { FirstName = "Ada", LastName = "Lovelace", Title = null };
        var form = new ProfileForm { FirstName = "Ada", LastName = "Lovelace", Title = "" };

        // Act
        var hasChanges = form.HasChangesFrom(saved);

        // Assert
        hasChanges.Should().BeFalse();
    }

    [Fact]
    public void HasChangesFrom_WhenANameDiffers_ShouldBeTrue()
    {
        // Arrange
        var saved = new ProfileForm { FirstName = "Ada", LastName = "Lovelace", Title = "Engineer" };
        var form = new ProfileForm { FirstName = "Grace", LastName = "Lovelace", Title = "Engineer" };

        // Act
        var hasChanges = form.HasChangesFrom(saved);

        // Assert
        hasChanges.Should().BeTrue();
    }

    private static string?[] Validate(ProfileForm form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, true);
        return results.Select(result => result.ErrorMessage).ToArray();
    }
}
