using Account.Client;
using Account.Features.Tenants.Domain;
using Account.Features.Tenants.Queries;
using Blazor.Client.Components.Images;
using Blazor.Client.Settings;
using FluentAssertions;
using SharedKernel.Domain;

namespace Blazor.Tests.Client.Settings;

public sealed class AccountSettingsSaveTests
{
    private const string SavedLogoUrl = "/logos/1/logo/SAVED.png";
    private const string NewLogoUrl = "/logos/1/logo/NEW.png";
    private const string SavedName = "Acme";
    private const string IntendedName = "Acme Corp";

    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();
    private static readonly AccountSettingsForm Intended = new() { Name = IntendedName };

    [Fact]
    public async Task RunAsync_WhenUploadAndNameSucceed_ShouldUploadBeforeThePutAndReadNothingBack()
    {
        // Arrange
        var server = new FakeServer();

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Upload, SavedLogoUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Saved);
        server.Steps.Should().Equal("upload", "put");
    }

    [Fact]
    public async Task RunAsync_WhenRemovingAndNameSucceed_ShouldRemoveBeforeThePut()
    {
        // Arrange
        var server = new FakeServer();

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Remove, SavedLogoUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Saved);
        server.Steps.Should().Equal("remove", "put");
    }

    [Fact]
    public async Task RunAsync_WhenTheLogoIsKept_ShouldOnlyPutTheName()
    {
        // Arrange
        var server = new FakeServer();

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Keep, SavedLogoUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Saved);
        server.Steps.Should().Equal("put");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    public async Task RunAsync_WhenUploadCommitsAndThePutFails_ShouldReportTheLogoSavedAndTheNameUnsavedWithoutUploadingAgain(int statusCode)
    {
        // Arrange
        var putFailure = Failure(statusCode);
        var server = new FakeServer { Put = putFailure, StoredTenant = Tenant(SavedName, NewLogoUrl) };

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Upload, SavedLogoUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Failed);
        outcome.Failure.Should().BeSameAs(putFailure);
        outcome.LogoSaved.Should().BeTrue();
        outcome.NameSaved.Should().BeFalse();
        outcome.ConfirmedTenant!.LogoUrl.Should().Be(NewLogoUrl);
        server.Steps.Should().Equal("upload", "put", "read");
    }

    [Fact]
    public async Task RunAsync_WhenTheUploadResponseIsLostAfterItCommitted_ShouldConfirmTheLogoFromTheServerAndNotPut()
    {
        // Arrange
        var lost = ApiCallResult.Failed(ApiCallOutcome.TransportFailure, new ApiCallProblem(null, null, "The connection was reset.", NoErrors, null));
        var server = new FakeServer { Upload = lost, StoredTenant = Tenant(SavedName, NewLogoUrl) };

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Upload, SavedLogoUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Failed);
        outcome.LogoSaved.Should().BeTrue();
        outcome.NameSaved.Should().BeFalse();
        server.Steps.Should().Equal("upload", "read");
    }

    [Fact]
    public async Task RunAsync_WhenTheUploadFailsWithoutCommitting_ShouldReportNothingSavedAndNotPut()
    {
        // Arrange
        var rejected = ApiCallResult.Failed(ApiCallOutcome.ValidationFailure, new ApiCallProblem(400, "Bad Request", null, new Dictionary<string, string[]> { ["fileStream"] = ["Image must be a valid JPEG, PNG, GIF, or WebP file."] }, null));
        var server = new FakeServer { Upload = rejected, StoredTenant = Tenant(SavedName, SavedLogoUrl) };

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Upload, SavedLogoUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Failed);
        outcome.Failure.Should().BeSameAs(rejected);
        outcome.LogoSaved.Should().BeFalse();
        outcome.NameSaved.Should().BeFalse();
        server.Steps.Should().Equal("upload", "read");
    }

    [Fact]
    public async Task RunAsync_WhenThePutResponseIsLostAfterItCommitted_ShouldReportBothConfirmedButNotSaved()
    {
        // Arrange
        var lost = ApiCallResult.Failed(ApiCallOutcome.TransportFailure, new ApiCallProblem(null, null, "The connection was reset.", NoErrors, null));
        var server = new FakeServer { Put = lost, StoredTenant = Tenant(IntendedName, null) };

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Remove, SavedLogoUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Failed);
        outcome.LogoSaved.Should().BeTrue();
        outcome.NameSaved.Should().BeTrue();
        server.Steps.Should().Equal("remove", "put", "read");
    }

    [Fact]
    public async Task RunAsync_WhenTheReadBackFailsToo_ShouldReportOnlyTheStepsThatSucceeded()
    {
        // Arrange
        var server = new FakeServer { Put = Failure(500), TenantResult = ApiCallResult<TenantResponse>.Failed(ApiCallOutcome.Failure, new ApiCallProblem(500, "Internal Server Error", null, NoErrors, null)) };

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Upload, SavedLogoUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Failed);
        outcome.LogoSaved.Should().BeTrue();
        outcome.NameSaved.Should().BeFalse();
        outcome.ConfirmedTenant.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WhenTheAccountIsLeftDuringTheUpload_ShouldAbandonWithoutFurtherCalls()
    {
        // Arrange
        var server = new FakeServer { LeaveOnUpload = true };

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Upload, SavedLogoUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Abandoned);
        outcome.LogoSaved.Should().BeFalse();
        outcome.NameSaved.Should().BeFalse();
        server.Steps.Should().Equal("upload");
    }

    [Fact]
    public async Task RunAsync_WhenTheFormIsLeftDuringTheUpload_ShouldCancelTheUploadAndAbandonWithoutFurtherCalls()
    {
        // Arrange
        using var abandoned = new CancellationTokenSource();
        var server = new FakeServer { OnUpload = abandoned.Cancel };

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Upload, SavedLogoUrl, Intended, server.Calls, abandoned.Token);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Abandoned);
        server.Steps.Should().Equal("upload");
        server.UploadWasCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenTheUploadIsUnauthorized_ShouldAbandonWithoutReadingBack()
    {
        // Arrange
        var server = new FakeServer { Upload = ApiCallResult.Failed(ApiCallOutcome.Unauthorized, new ApiCallProblem(401, "Unauthorized", null, NoErrors, null)) };

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Upload, SavedLogoUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Abandoned);
        server.Steps.Should().Equal("upload");
    }

    [Fact]
    public async Task RunAsync_WhenTheNameIsRefusedForANonOwner_ShouldReportNothingSaved()
    {
        // Arrange
        var forbidden = ApiCallResult.Failed(ApiCallOutcome.Failure, new ApiCallProblem(403, "Forbidden", "Only owners are allowed to update tenant information.", NoErrors, null));
        var server = new FakeServer { Put = forbidden, StoredTenant = Tenant(SavedName, SavedLogoUrl) };

        // Act
        var outcome = await AccountSettingsSave.RunAsync(ImageIntent.Keep, SavedLogoUrl, Intended, server.Calls, CancellationToken.None);

        // Assert
        outcome.Status.Should().Be(AccountSettingsSaveStatus.Failed);
        outcome.NameSaved.Should().BeFalse();
        outcome.Failure!.Problem!.Detail.Should().Be("Only owners are allowed to update tenant information.");
    }

    [Theory]
    [InlineData(ImageIntent.Upload, SavedLogoUrl, NewLogoUrl, true)]
    [InlineData(ImageIntent.Upload, SavedLogoUrl, SavedLogoUrl, false)]
    [InlineData(ImageIntent.Upload, null, null, false)]
    [InlineData(ImageIntent.Upload, null, NewLogoUrl, true)]
    [InlineData(ImageIntent.Remove, SavedLogoUrl, null, true)]
    [InlineData(ImageIntent.Remove, SavedLogoUrl, SavedLogoUrl, false)]
    [InlineData(ImageIntent.Keep, SavedLogoUrl, SavedLogoUrl, true)]
    public void IsLogoConfirmed_ShouldCompareTheServerLogoWithTheIntent(ImageIntent intent, string? savedLogoUrl, string? serverLogoUrl, bool expected)
    {
        // Act and Assert
        AccountSettingsSave.IsLogoConfirmed(intent, savedLogoUrl, serverLogoUrl).Should().Be(expected);
    }

    [Theory]
    [InlineData(IntendedName, true)]
    [InlineData(SavedName, false)]
    public void IsNameConfirmed_ShouldCompareTheServerNameWithTheIntendedName(string serverName, bool expected)
    {
        // Act and Assert
        AccountSettingsSave.IsNameConfirmed(Intended, Tenant(serverName, null)).Should().Be(expected);
    }

    private static ApiCallResult Failure(int statusCode)
    {
        return ApiCallResult.Failed(ApiCallOutcome.Failure, new ApiCallProblem(statusCode, "Failed", "The request failed.", NoErrors, null));
    }

    private static TenantResponse Tenant(string name, string? logoUrl)
    {
        return new TenantResponse(new TenantId(1), DateTimeOffset.UnixEpoch, null, name, TenantState.Active, null, logoUrl);
    }

    // Records the order of the calls and answers each as configured; a call that returns null stands for a departure from
    // the authenticated surface (SessionState.UnlessLeavingAsync)
    private sealed class FakeServer
    {
        public List<string> Steps { get; } = [];

        public ApiCallResult Upload { get; init; } = ApiCallResult.Success();

        public ApiCallResult Put { get; init; } = ApiCallResult.Success();

        public TenantResponse? StoredTenant { get; init; }

        public ApiCallResult<TenantResponse>? TenantResult { get; init; }

        public bool LeaveOnUpload { get; init; }

        public Action? OnUpload { get; init; }

        public bool UploadWasCancelled { get; private set; }

        public AccountSettingsSaveCalls Calls => new(UploadAsync, RemoveAsync, PutAsync, ReadAsync);

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

        private Task<ApiCallResult<TenantResponse>?> ReadAsync(CancellationToken cancellationToken)
        {
            Steps.Add("read");
            return Task.FromResult<ApiCallResult<TenantResponse>?>(TenantResult ?? ApiCallResult<TenantResponse>.Success(StoredTenant!));
        }
    }
}
