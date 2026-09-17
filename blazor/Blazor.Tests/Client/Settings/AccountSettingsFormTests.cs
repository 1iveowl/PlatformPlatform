using System.ComponentModel.DataAnnotations;
using Account.Features.Tenants.Domain;
using Account.Features.Tenants.Queries;
using Blazor.Client.Settings;
using FluentAssertions;
using SharedKernel.Domain;
using SharedKernel.Localization;

namespace Blazor.Tests.Client.Settings;

public sealed class AccountSettingsFormTests
{
    [Fact]
    public void From_ShouldTakeTheNameTheServerHolds()
    {
        // Act
        var form = AccountSettingsForm.From(Tenant("Acme Corp"));

        // Assert
        form.Name.Should().Be("Acme Corp");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenTheNameIsMissing_ShouldReportTheAccountApiMessage(string name)
    {
        // Act
        var results = Validate(new AccountSettingsForm { Name = name });

        // Assert
        results.Should().ContainSingle().Which.ErrorMessage.Should().Be(AccountStrings.TenantNameLength);
    }

    [Fact]
    public void Validate_WhenTheNameIsLongerThanTheAccountApiAllows_ShouldReportTheAccountApiMessage()
    {
        // Act
        var results = Validate(new AccountSettingsForm { Name = new string('a', 31) });

        // Assert
        results.Should().ContainSingle().Which.ErrorMessage.Should().Be(AccountStrings.TenantNameLength);
    }

    [Fact]
    public void Validate_WhenTheNameIsAtTheLimit_ShouldAccept()
    {
        // Act and Assert
        Validate(new AccountSettingsForm { Name = new string('a', 30) }).Should().BeEmpty();
    }

    [Fact]
    public void ToCommand_ShouldSendTheNameAsTyped()
    {
        // Act and Assert
        new AccountSettingsForm { Name = " Acme " }.ToCommand().Name.Should().Be(" Acme ");
    }

    [Fact]
    public void HasChangesFrom_ShouldCompareTheName()
    {
        // Arrange
        var saved = AccountSettingsForm.From(Tenant("Acme"));

        // Act and Assert
        saved.Copy().HasChangesFrom(saved).Should().BeFalse();
        new AccountSettingsForm { Name = "Acme Corp" }.HasChangesFrom(saved).Should().BeTrue();
    }

    private static IReadOnlyList<ValidationResult> Validate(AccountSettingsForm form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, true);
        return results;
    }

    private static TenantResponse Tenant(string name)
    {
        return new TenantResponse(new TenantId(1), DateTimeOffset.UnixEpoch, null, name, TenantState.Active, null, null);
    }
}
