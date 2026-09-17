using Account.Client;
using Blazor.Client.Forms;
using Blazor.Host.Components.Pages.Public;
using FluentAssertions;

namespace Blazor.Tests.Public;

public sealed class VerificationOutcomeTests
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    [Theory]
    [InlineData(400, "The code is wrong or no longer valid.", false, VerificationOutcomeKind.WrongCode)]
    [InlineData(400, "The code is no longer valid, please request a new code.", true, VerificationOutcomeKind.Expired)]
    [InlineData(403, "Too many attempts, please request a new code.", false, VerificationOutcomeKind.Locked)]
    [InlineData(403, "Too many attempts, please request a new code.", true, VerificationOutcomeKind.Locked)]
    [InlineData(404, "Email login with id 'x' not found.", false, VerificationOutcomeKind.None)]
    [InlineData(429, "Too many attempts to confirm this email address. Please try again later.", false, VerificationOutcomeKind.None)]
    public void FromFailure_WhenTheApiRefuses_ShouldPickTheStateFromTheStatusAndTheShownExpiry(int statusCode, string detail, bool isShownAsExpired, VerificationOutcomeKind expected)
    {
        // Arrange
        var problem = new ApiCallProblem(statusCode, null, detail, NoErrors, null);
        var failure = ApiFailureClassifier.Classify(ApiCallOutcome.Failure, problem);

        // Act
        var outcome = VerificationOutcome.FromFailure(failure, problem, isShownAsExpired);

        // Assert
        outcome.Kind.Should().Be(expected);
        outcome.IsLocked.Should().Be(expected == VerificationOutcomeKind.Locked);
        failure.Message.Should().Be(detail);
    }

    [Fact]
    public void FromFailure_WhenFieldErrorsOrAntiforgeryOrTransport_ShouldHaveNoState()
    {
        // Arrange
        var fieldProblem = new ApiCallProblem(400, null, null, new Dictionary<string, string[]> { ["oneTimePassword"] = ["Invalid."] }, null);
        var antiforgeryProblem = new ApiCallProblem(400, "The antiforgery token was rejected", null, NoErrors, null);
        var transportProblem = new ApiCallProblem(null, null, null, NoErrors, null);

        // Act
        var outcomes = new[]
        {
            VerificationOutcome.FromFailure(ApiFailureClassifier.Classify(ApiCallOutcome.ValidationFailure, fieldProblem), fieldProblem, false),
            VerificationOutcome.FromFailure(ApiFailureClassifier.Classify(ApiCallOutcome.Failure, antiforgeryProblem), antiforgeryProblem, false),
            VerificationOutcome.FromFailure(ApiFailureClassifier.Classify(ApiCallOutcome.TransportFailure, transportProblem), transportProblem, false)
        };

        // Assert
        outcomes.Should().AllSatisfy(outcome => outcome.Kind.Should().Be(VerificationOutcomeKind.None));
    }

    [Fact]
    public void Label_WhenEachState_ShouldBeLocalizedAndNamed()
    {
        // Act & Assert
        VerificationOutcome.None.Label.Should().BeNull();
        VerificationOutcome.None.StateName.Should().BeNull();
        new VerificationOutcome(VerificationOutcomeKind.WrongCode).StateName.Should().Be("wrong-code");
        new VerificationOutcome(VerificationOutcomeKind.Expired).StateName.Should().Be("expired");
        new VerificationOutcome(VerificationOutcomeKind.Locked).StateName.Should().Be("locked");
        new VerificationOutcome(VerificationOutcomeKind.Locked).Label.Should().NotBeNullOrWhiteSpace();
    }
}
