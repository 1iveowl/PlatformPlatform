using Account.Client;
using Blazor.Client.Forms;
using FluentAssertions;
using SharedKernel.Localization;

namespace Blazor.Tests.Client;

public sealed class ApiFailureClassifierTests
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    [Fact]
    public void Classify_WhenDetailIsPresent_ShouldUseDetail()
    {
        // Arrange
        var problem = new ApiCallProblem(409, "Conflict", "The email is already in use.", NoErrors, null);

        // Act
        var failure = ApiFailureClassifier.Classify(ApiCallOutcome.Failure, problem);

        // Assert
        failure.Should().Be(new ApiFailure(ApiFailureKind.Message, "The email is already in use."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_WhenDetailIsMissing_ShouldFallBackToTitle(string? detail)
    {
        // Arrange
        var problem = new ApiCallProblem(409, "Conflict", detail, NoErrors, null);

        // Act
        var failure = ApiFailureClassifier.Classify(ApiCallOutcome.Failure, problem);

        // Assert
        failure.Should().Be(new ApiFailure(ApiFailureKind.Message, "Conflict"));
    }

    [Fact]
    public void Classify_WhenProblemHasFieldErrors_ShouldBeFieldValidation()
    {
        // Arrange
        var problem = new ApiCallProblem(400, "One or more validation errors occurred.", null, new Dictionary<string, string[]> { ["email"] = ["Email is taken."] }, null);

        // Act
        var failure = ApiFailureClassifier.Classify(ApiCallOutcome.ValidationFailure, problem);

        // Assert
        failure.Should().Be(new ApiFailure(ApiFailureKind.FieldValidation, null));
    }

    [Theory]
    [InlineData(ApiCallOutcome.Unauthorized, 401)]
    [InlineData(ApiCallOutcome.Failure, 401)]
    public void Classify_WhenUnauthorized_ShouldBeSuppressed(ApiCallOutcome outcome, int statusCode)
    {
        // Arrange
        var problem = new ApiCallProblem(statusCode, "Unauthorized", "Session expired.", NoErrors, "Revoked");

        // Act
        var failure = ApiFailureClassifier.Classify(outcome, problem);

        // Assert
        failure.Should().Be(new ApiFailure(ApiFailureKind.Suppressed, null));
    }

    [Fact]
    public void Cancellation_ShouldBeSuppressed()
    {
        // Act
        var failure = ApiFailureClassifier.Cancellation;

        // Assert
        failure.Should().Be(new ApiFailure(ApiFailureKind.Suppressed, null));
    }

    [Theory]
    [InlineData("Antiforgery token validation failed")]
    [InlineData("The ANTIFORGERY token is invalid")]
    public void Classify_WhenBadRequestTitleNamesAntiforgery_ShouldRequireRecovery(string title)
    {
        // Arrange
        var problem = new ApiCallProblem(400, title, "Detail that must not be shown.", NoErrors, null);

        // Act
        var failure = ApiFailureClassifier.Classify(ApiCallOutcome.Failure, problem);

        // Assert
        failure.Should().Be(new ApiFailure(ApiFailureKind.AntiforgeryRecovery, CommonStrings.AntiforgeryRecovery));
    }

    [Fact]
    public void Classify_WhenTitleNamesAntiforgeryButStatusIsNotBadRequest_ShouldBeMessage()
    {
        // Arrange
        var problem = new ApiCallProblem(403, "Antiforgery", null, NoErrors, null);

        // Act
        var failure = ApiFailureClassifier.Classify(ApiCallOutcome.Failure, problem);

        // Assert
        failure.Should().Be(new ApiFailure(ApiFailureKind.Message, "Antiforgery"));
    }

    [Theory]
    [InlineData(ApiCallOutcome.TransportFailure, nameof(CommonStrings.TransportFailure))]
    [InlineData(ApiCallOutcome.InvalidResponse, nameof(CommonStrings.InvalidResponse))]
    public void Classify_WhenNoProblemDetailsWereRead_ShouldUseDefinedMessage(ApiCallOutcome outcome, string expectedMessageKey)
    {
        // Arrange
        var problem = new ApiCallProblem(outcome == ApiCallOutcome.InvalidResponse ? 200 : null, "OK", "Connection refused (127.0.0.1:5000)", NoErrors, null);

        // Act
        var failure = ApiFailureClassifier.Classify(outcome, problem);

        // Assert
        failure.Should().Be(new ApiFailure(ApiFailureKind.Message, CommonStrings.ResourceManager.GetString(expectedMessageKey)));
    }

    [Fact]
    public void Classify_WhenCallSucceeded_ShouldThrow()
    {
        // Act
        var classify = () => ApiFailureClassifier.Classify(ApiCallResult.Success());

        // Assert
        classify.Should().Throw<ArgumentException>();
    }
}
