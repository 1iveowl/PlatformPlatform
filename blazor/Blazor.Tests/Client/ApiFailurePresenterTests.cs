using System.ComponentModel.DataAnnotations;
using Account.Client;
using Blazor.Client.Bootstrap;
using Blazor.Client.Forms;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using SharedKernel.Localization;

namespace Blazor.Tests.Client;

// The interactive presentation of failed calls into the in-house toast queue; the region's markup and the reload action in
// a browser are covered by blazor/tests/form-errors.mjs
public sealed class ApiFailurePresenterTests
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    private readonly TestNavigationManager _navigation = new();
    private readonly ToastService _toasts = new();
    private AuthenticationNavigator? _authenticationNavigator;

    [Fact]
    public void Present_WhenFailureHasDetail_ShouldShowErrorToastWithDetail()
    {
        // Arrange
        var presenter = CreatePresenter();

        // Act
        var failure = presenter.Present(ApiCallOutcome.Failure, new ApiCallProblem(409, "Conflict", "The user was changed by someone else.", NoErrors, null));

        // Assert
        failure.Kind.Should().Be(ApiFailureKind.Message);
        var toast = _toasts.Toasts.Should().ContainSingle().Subject;
        toast.Kind.Should().Be(ToastKind.Error);
        toast.Title.Should().Be(CommonStrings.SomethingWentWrong);
        toast.Message.Should().Be("The user was changed by someone else.");
        toast.TestId.Should().Be(ApiFailurePresenter.ErrorToastTestId);
        toast.ActionLabel.Should().BeNull();
    }

    [Fact]
    public void Present_WhenFailureHasNoDetail_ShouldShowTitle()
    {
        // Arrange
        var presenter = CreatePresenter();

        // Act
        presenter.Present(ApiCallOutcome.Failure, new ApiCallProblem(409, "Conflict", null, NoErrors, null));

        // Assert
        _toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be("Conflict");
    }

    [Theory]
    [InlineData(ApiCallOutcome.Unauthorized, 401)]
    [InlineData(ApiCallOutcome.Failure, 401)]
    public void Present_WhenUnauthorized_ShouldShowNothingAndNotNavigate(ApiCallOutcome outcome, int statusCode)
    {
        // Arrange
        var presenter = CreatePresenter();

        // Act
        var failure = presenter.Present(outcome, new ApiCallProblem(statusCode, "Unauthorized", "The session has expired.", NoErrors, "Revoked"));

        // Assert
        failure.Kind.Should().Be(ApiFailureKind.Suppressed);
        _toasts.Toasts.Should().BeEmpty();
        _navigation.Navigations.Should().BeEmpty();
    }

    [Fact]
    public void Present_WhenAntiforgeryTokenIsRejected_ShouldShowRecoveryToastWhoseActionReloadsTheDocument()
    {
        // Arrange
        var presenter = CreatePresenter();

        // Act
        presenter.Present(ApiCallOutcome.Failure, new ApiCallProblem(400, "Antiforgery token validation failed", "The antiforgery token was not accepted.", NoErrors, null));

        // Assert
        var toast = _toasts.Toasts.Should().ContainSingle().Subject;
        toast.Kind.Should().Be(ToastKind.Warning);
        toast.Title.Should().Be(CommonStrings.AntiforgeryRecovery);
        toast.ActionLabel.Should().Be(CommonStrings.ReloadPage);
        toast.TestId.Should().Be(ApiFailurePresenter.AntiforgeryToastTestId);
        _navigation.Navigations.Should().BeEmpty();

        toast.OnAction!.Invoke();
        _navigation.Navigations.Should().Equal(new RecordedNavigation(TestNavigationManager.CurrentUri, true));
    }

    [Fact]
    public void Present_WhenFieldErrorsHaveAForm_ShouldPlaceThemOnTheFormAndShowNoToast()
    {
        // Arrange
        var presenter = CreatePresenter();
        var editContext = new EditContext(new PresenterForm());
        using var formErrors = new FormErrorMapper(editContext);
        var problem = new ApiCallProblem(400, "One or more validation errors occurred.", null, new Dictionary<string, string[]> { ["name"] = ["Name is reserved."] }, null);

        // Act
        var failure = presenter.Present(ApiCallOutcome.ValidationFailure, problem, formErrors);

        // Assert
        failure.Kind.Should().Be(ApiFailureKind.FieldValidation);
        editContext.GetValidationMessages(editContext.Field(nameof(PresenterForm.Name))).Should().Equal("Name is reserved.");
        _toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void Present_WhenFieldErrorsHaveNoForm_ShouldKeepEveryMessageInOneToast()
    {
        // Arrange
        var presenter = CreatePresenter();
        var errors = new Dictionary<string, string[]> { ["name"] = ["Name is too short.", "Name is reserved."], ["email"] = ["Email <b>is</b> already in use."] };

        // Act
        presenter.Present(ApiCallOutcome.ValidationFailure, new ApiCallProblem(400, "One or more validation errors occurred.", null, errors, null));

        // Assert
        _toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be("Name is too short. Name is reserved. Email <b>is</b> already in use.");
    }

    [Fact]
    public void Present_WhenTheSessionHasEnded_ShouldShowNothing()
    {
        // Arrange
        var presenter = CreatePresenter();
        _authenticationNavigator!.EndSession();

        // Act
        var failure = presenter.Present(ApiCallOutcome.TransportFailure, new ApiCallProblem(null, null, "The session of this browser runtime has ended.", new Dictionary<string, string[]>(), null));

        // Assert
        failure.Kind.Should().Be(ApiFailureKind.Message);
        _toasts.Toasts.Should().BeEmpty();
        _navigation.Navigations.Should().BeEmpty();
    }

    private ApiFailurePresenter CreatePresenter()
    {
        _authenticationNavigator = new AuthenticationNavigator(_navigation);
        return new ApiFailurePresenter(_toasts, _navigation, _authenticationNavigator);
    }

    private sealed class PresenterForm
    {
        [Required]
        public string Name { get; set; } = "Ada";
    }
}
