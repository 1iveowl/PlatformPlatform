using Account.Client;
using Blazor.Client.Components.Images;
using FluentAssertions;

namespace Blazor.Tests.Client.Components;

public sealed class ImageThenDetailsSaveTests
{
    private const string SavedImageUrl = "/images/1/saved.png";
    private const string NewImageUrl = "/images/1/new.png";
    private const string SavedDetails = "saved";
    private const string IntendedDetails = "intended";

    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();
    private static readonly ApiCallResult Lost = ApiCallResult.Failed(ApiCallOutcome.TransportFailure, new ApiCallProblem(null, null, "The connection was reset.", NoErrors, null));
    private static readonly ApiCallResult Unauthorized = ApiCallResult.Failed(ApiCallOutcome.Unauthorized, new ApiCallProblem(401, "Unauthorized", null, NoErrors, null));
    private static readonly ImageThenDetailsSaveOutcome<StoredState> AbandonedOutcome = new(ImageThenDetailsSaveStatus.Abandoned, null, null, false, false);

    [Theory]
    [InlineData(ImageIntent.Upload, "upload")]
    [InlineData(ImageIntent.Remove, "remove")]
    public async Task RunAsync_WhenTheImageChangeAndTheDetailsSucceed_ShouldChangeTheImageBeforeTheDetailsAndReadNothingBack(ImageIntent intent, string imageStep)
    {
        // Arrange
        using var server = new FakeServer();

        // Act
        var outcome = await RunAsync(intent, server);

        // Assert
        outcome.Should().Be(new ImageThenDetailsSaveOutcome<StoredState>(ImageThenDetailsSaveStatus.Saved, null, null, true, true));
        server.Steps.Should().Equal(imageStep, "details");
    }

    [Fact]
    public async Task RunAsync_WhenTheImageIsKept_ShouldOnlySaveTheDetails()
    {
        // Arrange
        using var server = new FakeServer();

        // Act
        var outcome = await RunAsync(ImageIntent.Keep, server);

        // Assert
        outcome.Status.Should().Be(ImageThenDetailsSaveStatus.Saved);
        server.Steps.Should().Equal("details");
    }

    [Fact]
    public async Task RunAsync_WhenTheFirstStepFailsWithoutCommitting_ShouldReportNothingSavedAndNeitherRetryNorSaveTheDetails()
    {
        // Arrange
        var rejected = ApiCallResult.Failed(ApiCallOutcome.ValidationFailure, new ApiCallProblem(400, "Bad Request", null, new Dictionary<string, string[]> { ["fileSteam"] = ["Invalid image."] }, null));
        using var server = new FakeServer { Upload = rejected };

        // Act
        var outcome = await RunAsync(ImageIntent.Upload, server);

        // Assert
        outcome.Status.Should().Be(ImageThenDetailsSaveStatus.Failed);
        outcome.Failure.Should().BeSameAs(rejected);
        outcome.Confirmed.Should().Be(new StoredState(SavedImageUrl, SavedDetails));
        outcome.ImageSaved.Should().BeFalse();
        outcome.DetailsSaved.Should().BeFalse();
        server.Steps.Should().Equal("upload", "read");
    }

    [Theory]
    [InlineData(ImageIntent.Upload, "upload", NewImageUrl)]
    [InlineData(ImageIntent.Remove, "remove", null)]
    public async Task RunAsync_WhenTheImageResponseIsLostAfterTheServerCommitted_ShouldConfirmTheImageFromTheReadBackWithoutReplayingIt(ImageIntent intent, string imageStep, string? storedImageUrl)
    {
        // Arrange
        using var server = new FakeServer { Upload = Lost, Remove = Lost, Stored = new StoredState(storedImageUrl, SavedDetails) };

        // Act
        var outcome = await RunAsync(intent, server);

        // Assert
        outcome.Status.Should().Be(ImageThenDetailsSaveStatus.Failed);
        outcome.Failure.Should().BeSameAs(Lost);
        outcome.ImageSaved.Should().BeTrue();
        outcome.DetailsSaved.Should().BeFalse();
        server.Steps.Should().Equal(imageStep, "read");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    public async Task RunAsync_WhenTheImageCommitsAndTheDetailsFail_ShouldReportTheImageSavedAndTheDetailsUnsavedWithoutReplayingTheImage(int statusCode)
    {
        // Arrange
        var failure = Failure(statusCode);
        using var server = new FakeServer { Details = failure, Stored = new StoredState(NewImageUrl, SavedDetails) };

        // Act
        var outcome = await RunAsync(ImageIntent.Upload, server);

        // Assert
        outcome.Status.Should().Be(ImageThenDetailsSaveStatus.Failed);
        outcome.Failure.Should().BeSameAs(failure);
        outcome.Confirmed.Should().Be(new StoredState(NewImageUrl, SavedDetails));
        outcome.ImageSaved.Should().BeTrue();
        outcome.DetailsSaved.Should().BeFalse();
        server.Steps.Should().Equal("upload", "details", "read");
    }

    [Fact]
    public async Task RunAsync_WhenTheDetailsResponseIsLostAfterTheServerCommitted_ShouldReportBothConfirmedButNotSaved()
    {
        // Arrange
        using var server = new FakeServer { Details = Lost, Stored = new StoredState(null, IntendedDetails) };

        // Act
        var outcome = await RunAsync(ImageIntent.Remove, server);

        // Assert
        outcome.Status.Should().Be(ImageThenDetailsSaveStatus.Failed);
        outcome.Failure.Should().BeSameAs(Lost);
        outcome.ImageSaved.Should().BeTrue();
        outcome.DetailsSaved.Should().BeTrue();
        server.Steps.Should().Equal("remove", "details", "read");
    }

    [Fact]
    public async Task RunAsync_WhenTheDetailsFailWithTheImageKept_ShouldReportTheKeptImageSaved()
    {
        // Arrange
        var failure = Failure(403);
        using var server = new FakeServer { Details = failure };

        // Act
        var outcome = await RunAsync(ImageIntent.Keep, server);

        // Assert
        outcome.Status.Should().Be(ImageThenDetailsSaveStatus.Failed);
        outcome.Failure.Should().BeSameAs(failure);
        outcome.ImageSaved.Should().BeTrue();
        outcome.DetailsSaved.Should().BeFalse();
        server.Steps.Should().Equal("details", "read");
    }

    [Theory]
    [InlineData(true, "upload", "details", "read")]
    [InlineData(false, "upload", "read")]
    public async Task RunAsync_WhenTheReadBackFailsToo_ShouldKeepTheOriginalFailureAndReportOnlyTheStepsThatSucceeded(bool imageStepSucceeds, params string[] expectedSteps)
    {
        // Arrange
        var failure = Failure(500);
        using var server = new FakeServer
        {
            Upload = imageStepSucceeds ? ApiCallResult.Success() : failure,
            Details = failure,
            Read = ApiCallResult<StoredState>.Failed(ApiCallOutcome.Failure, new ApiCallProblem(503, "Service Unavailable", null, NoErrors, null))
        };

        // Act
        var outcome = await RunAsync(ImageIntent.Upload, server);

        // Assert
        outcome.Status.Should().Be(ImageThenDetailsSaveStatus.Failed);
        outcome.Failure.Should().BeSameAs(failure);
        outcome.Confirmed.Should().BeNull();
        outcome.ImageSaved.Should().Be(imageStepSucceeds);
        outcome.DetailsSaved.Should().BeFalse();
        server.Steps.Should().Equal(expectedSteps);
    }

    [Theory]
    [InlineData(true, "upload", "details")]
    [InlineData(false, "upload")]
    public async Task RunAsync_WhenAStepIsUnauthorized_ShouldAbandonWithoutReadingBack(bool imageStepSucceeds, params string[] expectedSteps)
    {
        // Arrange
        using var server = new FakeServer { Upload = imageStepSucceeds ? ApiCallResult.Success() : Unauthorized, Details = Unauthorized };

        // Act
        var outcome = await RunAsync(ImageIntent.Upload, server);

        // Assert
        outcome.Should().Be(AbandonedOutcome);
        server.Steps.Should().Equal(expectedSteps);
    }

    [Theory]
    [InlineData(ImageIntent.Upload, "upload", "upload")]
    [InlineData(ImageIntent.Remove, "remove", "remove")]
    [InlineData(ImageIntent.Upload, "details", "upload", "details")]
    [InlineData(ImageIntent.Upload, "read", "upload", "details", "read")]
    public async Task RunAsync_WhenTheSessionIsLeftDuringAStep_ShouldAbandonWithoutFurtherCalls(ImageIntent intent, string leavingStep, params string[] expectedSteps)
    {
        // Arrange
        using var server = new FakeServer { Details = Failure(500), LeaveOn = leavingStep };

        // Act
        var outcome = await RunAsync(intent, server);

        // Assert
        outcome.Should().Be(AbandonedOutcome);
        server.Steps.Should().Equal(expectedSteps);
    }

    [Theory]
    [InlineData(ImageIntent.Upload, "upload", "upload")]
    [InlineData(ImageIntent.Remove, "remove", "remove")]
    [InlineData(ImageIntent.Upload, "details", "upload", "details")]
    [InlineData(ImageIntent.Upload, "read", "upload", "details", "read")]
    public async Task RunAsync_WhenTheFormIsAbandonedDuringAStep_ShouldCancelThatStepAndAbandonWithoutFurtherCalls(ImageIntent intent, string cancelledStep, params string[] expectedSteps)
    {
        // Arrange
        using var server = new FakeServer { Details = Failure(500), CancelDuring = cancelledStep };

        // Act
        var outcome = await RunAsync(intent, server);

        // Assert
        outcome.Should().Be(AbandonedOutcome);
        server.Steps.Should().Equal(expectedSteps);
        server.CancelledSteps.Should().Equal(cancelledStep);
    }

    [Theory]
    [InlineData(ImageIntent.Upload, "upload", "upload")]
    [InlineData(ImageIntent.Remove, "remove", "remove")]
    [InlineData(ImageIntent.Upload, "details", "upload", "details")]
    [InlineData(ImageIntent.Upload, "read", "upload", "details", "read")]
    public async Task RunAsync_WhenTheFormIsAbandonedAsAStepCompletes_ShouldDiscardItsResultAndAbandonWithoutFurtherCalls(ImageIntent intent, string completingStep, params string[] expectedSteps)
    {
        // Arrange
        using var server = new FakeServer { Details = Failure(500), CancelAfter = completingStep };

        // Act
        var outcome = await RunAsync(intent, server);

        // Assert
        outcome.Should().Be(AbandonedOutcome);
        server.Steps.Should().Equal(expectedSteps);
    }

    [Fact]
    public async Task RunAsync_WhenAStepIsCancelledWithoutTheFormBeingAbandoned_ShouldLetTheCancellationThrough()
    {
        // Arrange
        using var server = new FakeServer { ForeignCancellationOn = "details" };

        // Act
        var running = RunAsync(ImageIntent.Keep, server);

        // Assert
        await FluentActions.Awaiting(() => running).Should().ThrowAsync<OperationCanceledException>();
        server.Steps.Should().Equal("details");
    }

    [Theory]
    [InlineData(ImageIntent.Upload, SavedImageUrl, NewImageUrl, true)]
    [InlineData(ImageIntent.Upload, SavedImageUrl, SavedImageUrl, false)]
    [InlineData(ImageIntent.Upload, null, null, false)]
    [InlineData(ImageIntent.Upload, null, NewImageUrl, true)]
    [InlineData(ImageIntent.Remove, SavedImageUrl, null, true)]
    [InlineData(ImageIntent.Remove, SavedImageUrl, SavedImageUrl, false)]
    [InlineData(ImageIntent.Keep, SavedImageUrl, SavedImageUrl, true)]
    public void IsImageConfirmed_ShouldCompareTheStoredImageWithTheIntent(ImageIntent intent, string? savedImageUrl, string? storedImageUrl, bool expected)
    {
        // Act and Assert
        ImageThenDetailsSave.IsImageConfirmed(intent, savedImageUrl, storedImageUrl).Should().Be(expected);
    }

    private static Task<ImageThenDetailsSaveOutcome<StoredState>> RunAsync(ImageIntent intent, FakeServer server)
    {
        return ImageThenDetailsSave.RunAsync(intent, SavedImageUrl, server.Calls, stored => stored.ImageUrl, stored => stored.Details == IntendedDetails, server.Abandoned.Token);
    }

    private static ApiCallResult Failure(int statusCode)
    {
        return ApiCallResult.Failed(ApiCallOutcome.Failure, new ApiCallProblem(statusCode, "Failed", "The request failed.", NoErrors, null));
    }

    // The stored state the read-back returns: the image URL and the details the form edits
    private sealed record StoredState(string? ImageUrl, string Details);

    // Records the order of the calls and answers each as configured. LeaveOn returns null for that step, a departure from
    // the authenticated surface (SessionState.UnlessLeavingAsync); CancelDuring abandons the form while the step is in
    // flight, so the step observes its token; CancelAfter abandons the form just before the step's result arrives;
    // ForeignCancellationOn throws a cancellation that did not come from abandoning the form.
    private sealed class FakeServer : IDisposable
    {
        public CancellationTokenSource Abandoned { get; } = new();

        public List<string> Steps { get; } = [];

        public List<string> CancelledSteps { get; } = [];

        public ApiCallResult Upload { get; init; } = ApiCallResult.Success();

        public ApiCallResult Remove { get; init; } = ApiCallResult.Success();

        public ApiCallResult Details { get; init; } = ApiCallResult.Success();

        public StoredState Stored { get; init; } = new(SavedImageUrl, SavedDetails);

        public ApiCallResult<StoredState>? Read { get; init; }

        public string? LeaveOn { get; init; }

        public string? CancelDuring { get; init; }

        public string? CancelAfter { get; init; }

        public string? ForeignCancellationOn { get; init; }

        public ImageThenDetailsSaveCalls<StoredState> Calls => new(
            cancellationToken => AnswerAsync("upload", Upload, cancellationToken),
            cancellationToken => AnswerAsync("remove", Remove, cancellationToken),
            cancellationToken => AnswerAsync("details", Details, cancellationToken),
            cancellationToken => AnswerAsync("read", Read ?? ApiCallResult<StoredState>.Success(Stored), cancellationToken)
        );

        public void Dispose()
        {
            Abandoned.Dispose();
        }

        private async Task<TResult?> AnswerAsync<TResult>(string step, TResult result, CancellationToken cancellationToken) where TResult : class
        {
            Steps.Add(step);
            if (step == LeaveOn) return null;
            if (step == ForeignCancellationOn) throw new OperationCanceledException(new CancellationToken(true));

            if (step == CancelAfter)
            {
                await Abandoned.CancelAsync();
                return result;
            }

            if (step != CancelDuring) return result;

            await Abandoned.CancelAsync();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancelledSteps.Add(step);
                throw;
            }

            return result;
        }
    }
}
