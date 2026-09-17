using Account.Client;
using Account.Features.Users.Domain;
using Account.Features.Users.Queries;
using Blazor.Client.Profile;
using FluentAssertions;
using SharedKernel.Domain;

namespace Blazor.Tests.Client.Profile;

public sealed class ProfileSaveTests
{
    private const string SavedAvatarUrl = "/avatars/1/usr/saved.png";
    private const string NewAvatarUrl = "/avatars/1/usr/new.png";

    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();
    private static readonly ProfileForm Intended = new() { FirstName = "Ada", LastName = "Lovelace", Title = "Engineer" };

    [Fact]
    public async Task RunAsync_WhenUploadAndProfileSucceed_ShouldUploadBeforeThePutAndReadNothingBack()
    {
        // Arrange
        var server = new FakeServer();

        // Act
        var outcome = await ProfileSave.RunAsync(AvatarIntent.Upload, SavedAvatarUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(ProfileSaveStatus.Saved);
        server.Steps.Should().Equal("upload", "put");
    }

    [Fact]
    public async Task RunAsync_WhenRemovingAndProfileSucceed_ShouldRemoveBeforeThePut()
    {
        // Arrange
        var server = new FakeServer();

        // Act
        var outcome = await ProfileSave.RunAsync(AvatarIntent.Remove, SavedAvatarUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(ProfileSaveStatus.Saved);
        server.Steps.Should().Equal("remove", "put");
    }

    [Fact]
    public async Task RunAsync_WhenTheAvatarIsKept_ShouldOnlyPutTheProfile()
    {
        // Arrange
        var server = new FakeServer();

        // Act
        var outcome = await ProfileSave.RunAsync(AvatarIntent.Keep, SavedAvatarUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(ProfileSaveStatus.Saved);
        server.Steps.Should().Equal("put");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    public async Task RunAsync_WhenUploadCommitsAndThePutFails_ShouldReportTheAvatarSavedAndTheProfileUnsavedWithoutUploadingAgain(int statusCode)
    {
        // Arrange
        var putFailure = Failure(statusCode);
        var server = new FakeServer { Put = putFailure, CurrentUser = User("Grace", "Hopper", "", NewAvatarUrl) };

        // Act
        var outcome = await ProfileSave.RunAsync(AvatarIntent.Upload, SavedAvatarUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(ProfileSaveStatus.Failed);
        outcome.Failure.Should().BeSameAs(putFailure);
        outcome.AvatarSaved.Should().BeTrue();
        outcome.ProfileSaved.Should().BeFalse();
        outcome.ConfirmedUser!.AvatarUrl.Should().Be(NewAvatarUrl);
        server.Steps.Should().Equal("upload", "put", "read");
    }

    [Fact]
    public async Task RunAsync_WhenTheUploadResponseIsLostAfterItCommitted_ShouldConfirmTheAvatarFromTheServerAndNotPut()
    {
        // Arrange
        var lost = ApiCallResult.Failed(ApiCallOutcome.TransportFailure, new ApiCallProblem(null, null, "The connection was reset.", NoErrors, null));
        var server = new FakeServer { Upload = lost, CurrentUser = User("Grace", "Hopper", "", NewAvatarUrl) };

        // Act
        var outcome = await ProfileSave.RunAsync(AvatarIntent.Upload, SavedAvatarUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(ProfileSaveStatus.Failed);
        outcome.AvatarSaved.Should().BeTrue();
        outcome.ProfileSaved.Should().BeFalse();
        server.Steps.Should().Equal("upload", "read");
    }

    [Fact]
    public async Task RunAsync_WhenTheUploadFailsWithoutCommitting_ShouldReportNothingSavedAndNotPut()
    {
        // Arrange
        var rejected = ApiCallResult.Failed(ApiCallOutcome.ValidationFailure, new ApiCallProblem(400, "Bad Request", null, new Dictionary<string, string[]> { ["fileSteam"] = ["Image must be a valid JPEG, PNG, GIF, or WebP file."] }, null));
        var server = new FakeServer { Upload = rejected, CurrentUser = User("Grace", "Hopper", "", SavedAvatarUrl) };

        // Act
        var outcome = await ProfileSave.RunAsync(AvatarIntent.Upload, SavedAvatarUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(ProfileSaveStatus.Failed);
        outcome.Failure.Should().BeSameAs(rejected);
        outcome.AvatarSaved.Should().BeFalse();
        outcome.ProfileSaved.Should().BeFalse();
        server.Steps.Should().Equal("upload", "read");
    }

    [Fact]
    public async Task RunAsync_WhenThePutResponseIsLostAfterItCommitted_ShouldReportBothConfirmedButNotSaved()
    {
        // Arrange
        var lost = ApiCallResult.Failed(ApiCallOutcome.TransportFailure, new ApiCallProblem(null, null, "The connection was reset.", NoErrors, null));
        var server = new FakeServer { Put = lost, CurrentUser = User("Ada", "Lovelace", "Engineer", null) };

        // Act
        var outcome = await ProfileSave.RunAsync(AvatarIntent.Remove, SavedAvatarUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(ProfileSaveStatus.Failed);
        outcome.AvatarSaved.Should().BeTrue();
        outcome.ProfileSaved.Should().BeTrue();
        server.Steps.Should().Equal("remove", "put", "read");
    }

    [Fact]
    public async Task RunAsync_WhenTheReadBackFailsToo_ShouldReportOnlyTheStepsThatSucceeded()
    {
        // Arrange
        var server = new FakeServer { Put = Failure(500), CurrentUserResult = ApiCallResult<CurrentUserResponse>.Failed(ApiCallOutcome.Failure, new ApiCallProblem(500, "Internal Server Error", null, NoErrors, null)) };

        // Act
        var outcome = await ProfileSave.RunAsync(AvatarIntent.Upload, SavedAvatarUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(ProfileSaveStatus.Failed);
        outcome.AvatarSaved.Should().BeTrue();
        outcome.ProfileSaved.Should().BeFalse();
        outcome.ConfirmedUser.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WhenTheSessionIsLeftDuringTheUpload_ShouldAbandonWithoutFurtherCalls()
    {
        // Arrange
        var server = new FakeServer { LeaveOnUpload = true };

        // Act
        var outcome = await ProfileSave.RunAsync(AvatarIntent.Upload, SavedAvatarUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(ProfileSaveStatus.Abandoned);
        server.Steps.Should().Equal("upload");
    }

    [Fact]
    public async Task RunAsync_WhenTheFormIsLeftDuringTheUpload_ShouldCancelTheUploadAndAbandonWithoutFurtherCalls()
    {
        // Arrange
        using var abandoned = new CancellationTokenSource();
        var server = new FakeServer { OnUpload = abandoned.Cancel };

        // Act
        var outcome = await ProfileSave.RunAsync(AvatarIntent.Upload, SavedAvatarUrl, Intended, server.Calls, abandoned.Token);

        // Assert
        outcome.Status.Should().Be(ProfileSaveStatus.Abandoned);
        server.Steps.Should().Equal("upload");
        server.UploadWasCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenTheUploadIsUnauthorized_ShouldAbandonWithoutReadingBack()
    {
        // Arrange
        var server = new FakeServer { Upload = ApiCallResult.Failed(ApiCallOutcome.Unauthorized, new ApiCallProblem(401, "Unauthorized", null, NoErrors, null)) };

        // Act
        var outcome = await ProfileSave.RunAsync(AvatarIntent.Upload, SavedAvatarUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(ProfileSaveStatus.Abandoned);
        server.Steps.Should().Equal("upload");
    }

    [Theory]
    [InlineData(AvatarIntent.Upload, SavedAvatarUrl, NewAvatarUrl, true)]
    [InlineData(AvatarIntent.Upload, SavedAvatarUrl, SavedAvatarUrl, false)]
    [InlineData(AvatarIntent.Upload, null, null, false)]
    [InlineData(AvatarIntent.Upload, null, NewAvatarUrl, true)]
    [InlineData(AvatarIntent.Remove, SavedAvatarUrl, null, true)]
    [InlineData(AvatarIntent.Remove, SavedAvatarUrl, SavedAvatarUrl, false)]
    public void IsAvatarConfirmed_ShouldCompareTheServerAvatarWithTheIntent(AvatarIntent intent, string? savedAvatarUrl, string? serverAvatarUrl, bool expected)
    {
        // Act and Assert
        ProfileSave.IsAvatarConfirmed(intent, savedAvatarUrl, serverAvatarUrl).Should().Be(expected);
    }

    private static ApiCallResult Failure(int statusCode)
    {
        return ApiCallResult.Failed(ApiCallOutcome.Failure, new ApiCallProblem(statusCode, "Failed", "The request failed.", NoErrors, null));
    }

    private static CurrentUserResponse User(string firstName, string lastName, string title, string? avatarUrl)
    {
        return new CurrentUserResponse(new UserId("usr_01JZ8Q4N6V3K2M7P9R5T0W1XYZ"), DateTimeOffset.UnixEpoch, null, "ada@example.com", UserRole.Owner, firstName, lastName, title, avatarUrl);
    }

    // Records the order of the calls and answers each as configured; a call that returns null stands for a departure from
    // the authenticated surface (SessionState.UnlessLeavingAsync)
    private sealed class FakeServer
    {
        public List<string> Steps { get; } = [];

        public ApiCallResult Upload { get; init; } = ApiCallResult.Success();

        public ApiCallResult Put { get; init; } = ApiCallResult.Success();

        public CurrentUserResponse? CurrentUser { get; init; }

        public ApiCallResult<CurrentUserResponse>? CurrentUserResult { get; init; }

        public bool LeaveOnUpload { get; init; }

        public Action? OnUpload { get; init; }

        public bool UploadWasCancelled { get; private set; }

        public ProfileSaveCalls Calls => new(UploadAsync, RemoveAsync, PutAsync, ReadAsync);

        private async Task<ApiCallResult?> UploadAsync(CancellationToken cancellationToken)
        {
            Steps.Add("upload");
            if (LeaveOnUpload) return null;
            if (OnUpload is null) return Upload;

            OnUpload();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                UploadWasCancelled = true;
                throw;
            }

            return Upload;
        }

        private Task<ApiCallResult?> RemoveAsync(CancellationToken cancellationToken)
        {
            Steps.Add("remove");
            return Task.FromResult<ApiCallResult?>(ApiCallResult.Success());
        }

        private Task<ApiCallResult?> PutAsync(CancellationToken cancellationToken)
        {
            Steps.Add("put");
            return Task.FromResult<ApiCallResult?>(Put);
        }

        private Task<ApiCallResult<CurrentUserResponse>?> ReadAsync(CancellationToken cancellationToken)
        {
            Steps.Add("read");
            return Task.FromResult<ApiCallResult<CurrentUserResponse>?>(CurrentUserResult ?? ApiCallResult<CurrentUserResponse>.Success(CurrentUser!));
        }
    }
}
