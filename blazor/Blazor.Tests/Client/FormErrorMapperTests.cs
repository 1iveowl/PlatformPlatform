using System.ComponentModel.DataAnnotations;
using Account.Client;
using Blazor.Client.Forms;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Blazor.Tests.Client;

// The shared mapper on an EditContext with data annotations validation enabled, the way DataAnnotationsValidator enables it
public sealed class FormErrorMapperTests
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    [Fact]
    public void Apply_WhenFieldHasSeveralMessages_ShouldKeepEveryMessageOnTheField()
    {
        // Arrange
        var (editContext, formErrors) = CreateForm(new MapperForm { Name = "Ada", Email = "ada@example.com" });
        using var _ = formErrors;
        var problem = CreateValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is too short.", "Name is reserved."], ["email"] = ["Email is taken."] });

        // Act
        formErrors.Apply(problem);

        // Assert
        editContext.GetValidationMessages(editContext.Field(nameof(MapperForm.Name))).Should().Equal("Name is too short.", "Name is reserved.");
        editContext.GetValidationMessages(editContext.Field(nameof(MapperForm.Email))).Should().Equal("Email is taken.");
        formErrors.FormMessages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("email")]
    [InlineData("EMAIL")]
    [InlineData("eMaIl")]
    public void Apply_WhenKeyDiffersInCase_ShouldMatchTheProperty(string key)
    {
        // Arrange
        var (editContext, formErrors) = CreateForm(new MapperForm());
        using var _ = formErrors;

        // Act
        formErrors.Apply(CreateValidationProblem(new Dictionary<string, string[]> { [key] = ["Email is taken."] }));

        // Assert
        editContext.GetValidationMessages(editContext.Field(nameof(MapperForm.Email))).Should().Equal("Email is taken.");
        formErrors.FormMessages.Should().BeEmpty();
    }

    [Fact]
    public void Apply_WhenKeyMatchesNoProperty_ShouldAddFormLevelMessagesShownByValidationSummary()
    {
        // Arrange
        var (editContext, formErrors) = CreateForm(new MapperForm());
        using var _ = formErrors;

        // Act
        formErrors.Apply(CreateValidationProblem(new Dictionary<string, string[]> { ["tenantSlug"] = ["The tenant is locked.", "Contact the owner."] }));

        // Assert
        formErrors.FormMessages.Should().Equal("The tenant is locked.", "Contact the owner.");
        editContext.GetValidationMessages(new FieldIdentifier(editContext.Model, string.Empty)).Should().Equal("The tenant is locked.", "Contact the owner.");
        editContext.GetValidationMessages().Should().Equal("The tenant is locked.", "Contact the owner.");
    }

    [Fact]
    public void Apply_ShouldNotifyValidationStateChanged()
    {
        // Arrange
        var (editContext, formErrors) = CreateForm(new MapperForm());
        using var _ = formErrors;
        var notifications = 0;
        editContext.OnValidationStateChanged += (_, _) => notifications++;

        // Act
        formErrors.Apply(CreateValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Email is taken."] }));

        // Assert
        notifications.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Validate_WhenServerAndDataAnnotationsMessagesCoexist_ShouldClearOnlyServerMessages()
    {
        // Arrange
        var (editContext, formErrors) = CreateForm(new MapperForm { Name = "", Email = "ada@example.com" });
        using var _ = formErrors;
        await editContext.ValidateAsync(CancellationToken.None);
        formErrors.Apply(CreateValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Email is taken."], ["name"] = ["Name is reserved."], ["plan"] = ["Plan expired."] }));
        editContext.GetValidationMessages(editContext.Field(nameof(MapperForm.Name))).Should().HaveCount(2);

        // Act
        var isValid = await editContext.ValidateAsync(CancellationToken.None);

        // Assert
        isValid.Should().BeFalse();
        editContext.GetValidationMessages(editContext.Field(nameof(MapperForm.Name))).Should().Equal("The Name field is required.");
        editContext.GetValidationMessages(editContext.Field(nameof(MapperForm.Email))).Should().BeEmpty();
        formErrors.FormMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_WhenOnlyServerMessagesExist_ShouldClearThemOnTheNextSubmit()
    {
        // Arrange
        var (editContext, formErrors) = CreateForm(new MapperForm { Name = "Ada", Email = "ada@example.com" });
        using var _ = formErrors;
        formErrors.ApplyFailure(ApiCallOutcome.Failure, new ApiCallProblem(400, "Antiforgery token validation failed", null, NoErrors, null));
        formErrors.IsReloadRequired.Should().BeTrue();

        // Act
        var isValid = await editContext.ValidateAsync(CancellationToken.None);

        // Assert
        isValid.Should().BeTrue();
        editContext.GetValidationMessages().Should().BeEmpty();
        formErrors.FormMessages.Should().BeEmpty();
        formErrors.IsReloadRequired.Should().BeFalse();
    }

    [Fact]
    public void ApplyFailure_WhenFailureIsAMessage_ShouldAddDetailThenTitleAsFormMessage()
    {
        // Arrange
        var (_, formErrors) = CreateForm(new MapperForm());
        using var _ = formErrors;

        // Act
        formErrors.ApplyFailure(ApiCallOutcome.Failure, new ApiCallProblem(409, "Conflict", "The email is already in use.", NoErrors, null));
        formErrors.ApplyFailure(ApiCallOutcome.Failure, new ApiCallProblem(409, "Conflict", null, NoErrors, null));

        // Assert
        formErrors.FormMessages.Should().Equal("The email is already in use.", "Conflict");
    }

    [Fact]
    public void ApplyFailure_WhenUnauthorized_ShouldAddNoMessage()
    {
        // Arrange
        var (editContext, formErrors) = CreateForm(new MapperForm());
        using var _ = formErrors;

        // Act
        var failure = formErrors.ApplyFailure(ApiCallOutcome.Unauthorized, new ApiCallProblem(401, "Unauthorized", "Session expired.", NoErrors, null));

        // Assert
        failure.Kind.Should().Be(ApiFailureKind.Suppressed);
        formErrors.FormMessages.Should().BeEmpty();
        editContext.GetValidationMessages().Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_ShouldStopClearingOnValidation()
    {
        // Arrange
        var (editContext, formErrors) = CreateForm(new MapperForm { Name = "Ada", Email = "ada@example.com" });
        formErrors.Apply(CreateValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Email is taken."] }));

        // Act
        formErrors.Dispose();
        await editContext.ValidateAsync(CancellationToken.None);

        // Assert
        editContext.GetValidationMessages(editContext.Field(nameof(MapperForm.Email))).Should().Equal("Email is taken.");
    }

    private static (EditContext EditContext, FormErrorMapper FormErrors) CreateForm(MapperForm model)
    {
        var editContext = new EditContext(model);
        editContext.EnableDataAnnotationsValidation(new ServiceCollection().BuildServiceProvider());
        return (editContext, new FormErrorMapper(editContext));
    }

    private static ApiCallProblem CreateValidationProblem(Dictionary<string, string[]> errors)
    {
        return new ApiCallProblem(400, "One or more validation errors occurred.", null, errors, null);
    }

    private sealed class MapperForm
    {
        [Required]
        public string Name { get; set; } = "";

        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";
    }
}
