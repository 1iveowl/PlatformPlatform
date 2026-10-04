using Account.Client;
using Blazor.Client.BackOffice.Shared;
using Blazor.Client.Bootstrap;
using Blazor.Client.Forms;
using FluentAssertions;

namespace Blazor.Tests.Client.BackOffice;

// The state every back-office card, tab and detail page renders from its read: loading until the answer, loaded, not found
// or failed with Try again; a refresh that keeps what is shown; and an older or disposed read that never replaces newer state
public sealed class BackOfficeReadTests
{
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    private readonly TestNavigationManager _navigation = new();
    private readonly ApiFailurePresenter _presenter;
    private readonly ToastService _toasts = new();

    public BackOfficeReadTests()
    {
        _presenter = new ApiFailurePresenter(_toasts, _navigation, new AuthenticationNavigator(_navigation));
    }

    [Fact]
    public async Task Load_WhenTheFirstReadFailsAndTheRetrySucceeds_ShouldLeaveTheFailedStateForTheValue()
    {
        // Arrange
        using var read = new BackOfficeRead<string>();

        // Act
        await read.LoadAsync(_ => Task.FromResult(Failure(500, "The dashboard is unavailable.")), value => value, _presenter);
        var failedStatus = read.Status;
        var failureMessage = read.FailureMessage;
        await read.LoadAsync(_ => Task.FromResult(ApiCallResult<string>.Success("rows")), value => value, _presenter);

        // Assert
        failedStatus.Should().Be(BackOfficeReadStatus.Failed);
        failureMessage.Should().Be("The dashboard is unavailable.");
        _toasts.Toasts.Should().ContainSingle().Which.TestId.Should().Be(ApiFailurePresenter.ErrorToastTestId);
        read.Status.Should().Be(BackOfficeReadStatus.Loaded);
        read.Value.Should().Be("rows");
        read.FailureMessage.Should().BeNull();
    }

    [Fact]
    public async Task Load_WhileTheAnswerIsPending_ShouldShowTheLoadingStateWithoutAValue()
    {
        // Arrange
        using var read = new BackOfficeRead<string>();
        await read.LoadAsync(_ => Task.FromResult(ApiCallResult<string>.Success("first")), value => value, _presenter);
        var pending = new TaskCompletionSource<ApiCallResult<string>>();

        // Act
        var loading = read.LoadAsync(_ => pending.Task, value => value, _presenter);

        // Assert
        read.Status.Should().Be(BackOfficeReadStatus.Loading);
        read.Value.Should().BeNull();
        pending.SetResult(ApiCallResult<string>.Success("second"));
        await loading;
        read.Value.Should().Be("second");
    }

    [Fact]
    public async Task Load_WhenAnOlderReadAnswersAfterANewerOne_ShouldKeepTheNewerStateAndPresentNothing()
    {
        // Arrange
        using var read = new BackOfficeRead<string>();
        var older = new TaskCompletionSource<ApiCallResult<string>>();
        var olderToken = CancellationToken.None;
        var olderLoad = read.LoadAsync(cancellationToken =>
            {
                olderToken = cancellationToken;
                return older.Task;
            }, value => value, _presenter
        );

        // Act
        var newerResult = await read.LoadAsync(_ => Task.FromResult(ApiCallResult<string>.Success("30 days")), value => value, _presenter);
        older.SetResult(Failure(500, "The 7 day read failed."));
        var olderResult = await olderLoad;

        // Assert
        olderToken.IsCancellationRequested.Should().BeTrue();
        newerResult.Should().NotBeNull();
        olderResult.Should().BeNull();
        read.Status.Should().Be(BackOfficeReadStatus.Loaded);
        read.Value.Should().Be("30 days");
        _toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WhenTheOlderRequestHonoursItsCancellation_ShouldReturnNothingAndKeepTheNewerState()
    {
        // Arrange
        using var read = new BackOfficeRead<string>();
        var olderLoad = read.LoadAsync(async cancellationToken =>
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return ApiCallResult<string>.Success("never");
            }, value => value, _presenter
        );

        // Act
        await read.LoadAsync(_ => Task.FromResult(ApiCallResult<string>.Success("newer")), value => value, _presenter);
        var olderResult = await olderLoad;

        // Assert
        olderResult.Should().BeNull();
        read.Value.Should().Be("newer");
    }

    [Fact]
    public async Task Dispose_WhileAReadIsPending_ShouldCancelItAndDropItsAnswer()
    {
        // Arrange
        var read = new BackOfficeRead<string>();
        var pending = new TaskCompletionSource<ApiCallResult<string>>();
        var token = CancellationToken.None;
        var load = read.LoadAsync(cancellationToken =>
            {
                token = cancellationToken;
                return pending.Task;
            }, value => value, _presenter
        );

        // Act
        read.Dispose();
        pending.SetResult(Failure(500, "Too late."));
        var result = await load;

        // Assert
        token.IsCancellationRequested.Should().BeTrue();
        result.Should().BeNull();
        read.Status.Should().Be(BackOfficeReadStatus.Loading);
        _toasts.Toasts.Should().BeEmpty();
        (await read.LoadAsync(_ => Task.FromResult(ApiCallResult<string>.Success("after")), value => value, _presenter)).Should().BeNull();
    }

    [Theory]
    [InlineData(ApiCallOutcome.Unauthorized)]
    [InlineData(ApiCallOutcome.Failure)]
    public async Task Load_WhenTheAnswerIsAnAuthenticationLoss_ShouldStayLoadingAndPresentNothing(ApiCallOutcome outcome)
    {
        // Arrange
        using var read = new BackOfficeRead<string>();
        var unauthorized = ApiCallResult<string>.Failed(outcome, new ApiCallProblem(401, "Unauthorized", "The session has expired.", NoErrors, "Revoked"));

        // Act
        var result = await read.LoadAsync(_ => Task.FromResult(unauthorized), value => value, _presenter);

        // Assert
        result.Should().BeSameAs(unauthorized);
        read.Status.Should().Be(BackOfficeReadStatus.Loading);
        _toasts.Toasts.Should().BeEmpty();
        _navigation.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WhenTheSelectionFindsNothing_ShouldBeNotFound()
    {
        // Arrange
        using var read = new BackOfficeRead<string>();

        // Act
        await read.LoadAsync(_ => Task.FromResult(ApiCallResult<string[]>.Success(["beta-features"])), flags => flags.FirstOrDefault(flag => flag == "no-such-flag"), _presenter);

        // Assert
        read.Status.Should().Be(BackOfficeReadStatus.NotFound);
        _toasts.Toasts.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_When404WithoutTheNotFoundOption_ShouldBeFailedAndPresented()
    {
        // Arrange
        using var read = new BackOfficeRead<string>();

        // Act
        await read.LoadAsync(_ => Task.FromResult(Failure(404, "Not found.")), value => value, _presenter);

        // Assert
        read.Status.Should().Be(BackOfficeReadStatus.Failed);
        _toasts.Toasts.Should().ContainSingle();
    }

    [Fact]
    public async Task Load_WhenATransportFailureLeavesNoProblemDetail_ShouldBeFailedWithTheTransportMessage()
    {
        // Arrange
        using var read = new BackOfficeRead<string>();
        var transportFailure = ApiCallResult<string>.Failed(ApiCallOutcome.TransportFailure, new ApiCallProblem(null, null, null, NoErrors, null));

        // Act
        await read.LoadAsync(_ => Task.FromResult(transportFailure), value => value, _presenter);

        // Assert
        read.Status.Should().Be(BackOfficeReadStatus.Failed);
        read.FailureMessage.Should().Be(SharedKernel.Localization.CommonStrings.TransportFailure);
    }

    [Fact]
    public async Task Refresh_WhenItFails_ShouldKeepTheShownValueAndPresentTheFailure()
    {
        // Arrange
        using var read = new BackOfficeRead<string>(true);
        await read.LoadAsync(_ => Task.FromResult(ApiCallResult<string>.Success("Acme")), value => value, _presenter);

        // Act
        var result = await read.RefreshAsync(_ => Task.FromResult(Failure(404, "Tenant not found.")), value => value, _presenter);

        // Assert
        result.Should().NotBeNull();
        read.Status.Should().Be(BackOfficeReadStatus.Loaded);
        read.Value.Should().Be("Acme");
        _toasts.Toasts.Should().ContainSingle().Which.Message.Should().Be("Tenant not found.");
    }

    [Fact]
    public async Task Refresh_WhenItSucceeds_ShouldShowTheNewValueWithoutPassingThroughLoading()
    {
        // Arrange
        using var read = new BackOfficeRead<string>();
        await read.LoadAsync(_ => Task.FromResult(ApiCallResult<string>.Success("Active")), value => value, _presenter);
        var pending = new TaskCompletionSource<ApiCallResult<string>>();

        // Act
        var refresh = read.RefreshAsync(_ => pending.Task, value => value, _presenter);
        var statusWhilePending = read.Status;
        pending.SetResult(ApiCallResult<string>.Success("Inactive"));
        await refresh;

        // Assert
        statusWhilePending.Should().Be(BackOfficeReadStatus.Loaded);
        read.Value.Should().Be("Inactive");
    }

    private static ApiCallResult<string> Failure(int statusCode, string detail)
    {
        return ApiCallResult<string>.Failed(ApiCallOutcome.Failure, new ApiCallProblem(statusCode, "Failed", detail, NoErrors, null));
    }
}
