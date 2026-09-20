using System.Net;
using System.Text;
using System.Text.Json;
using Account.Client;
using Account.Features.Authentication.Queries;
using Account.Features.Authentication.Requests;
using Blazor.Client.Bootstrap;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using Blazor.Client.Session;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.ApiResults;
using SharedKernel.Domain;
using FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags;

namespace Blazor.Tests.Client.Session;

// The session state over the real WebAssembly registration: HttpBootstrapSource, the typed clients' handler chain, the
// antiforgery token source and the feature flag state, with a network whose responses the test releases in any order.
// Every case asserts that the identity, the token a state-changing call sends, the flags and the notifications agree
// with the one bootstrap the state accepted.
//
// The write gate re-reads the server's version before it forwards a mutation, so every state-changing call here makes a
// bootstrap read of its own first. That read belongs to no session read: it is answered separately, it carries no version
// in these responses, and it commits nothing to the session, the token source or the flags.
public sealed class SessionStateConsistencyTests
{
    private static readonly UserId UserId = new("usr_01JZ8Q4N6V3K2M7P9R5T0W1XYZ");

    private static readonly UserId OtherUserId = new("usr_01JZ8Q4N6V3K2M7P9R5T0W1XYA");

    [Fact]
    public async Task RefreshAsync_WhenAnOlderReadCompletesAfterANewerOne_ShouldKeepIdentityTokenAndFlagsOfTheNewerRead()
    {
        // Arrange
        var network = new ControlledNetwork();
        await using var services = CreateServices(network);
        var session = services.GetRequiredService<SessionState>();
        var notifications = RecordNotifications(services);
        var olderRead = session.RefreshAsync();
        var newerRead = session.RefreshAsync();
        await network.WaitForRequestsAsync(2);

        // Act
        network.Respond(1, CreateBootstrapResponse(UserId, 1, "Ann", "newer-token", FeatureFlagRegistry.BetaFeatures.Key));
        await newerRead;
        network.Respond(0, CreateBootstrapResponse(UserId, 1, "Old", "older-token", FeatureFlagRegistry.CompactView.Key));
        var olderResult = await olderRead;

        // Assert
        olderResult.User!.FirstName.Should().Be("Ann");
        session.Current!.User!.FirstName.Should().Be("Ann");
        var featureFlagState = services.GetRequiredService<FeatureFlagState>();
        featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeTrue();
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeFalse();
        notifications.Should().Equal("flags:Ann", "session:Ann:beta");
        (await SendStateChangingCallAsync(services, network)).Should().Be("newer-token");
    }

    [Fact]
    public async Task RefreshAsync_WhenAReadCompletesAfterLogout_ShouldCancelItAndKeepEveryStoreCleared()
    {
        // Arrange
        var network = new ControlledNetwork();
        await using var services = CreateServices(network);
        var session = services.GetRequiredService<SessionState>();
        var initialRead = session.GetAsync();
        await network.WaitForRequestsAsync(1);
        network.Respond(0, CreateBootstrapResponse(UserId, 1, "Ann", "first-token", FeatureFlagRegistry.BetaFeatures.Key));
        await initialRead;
        var notifications = RecordNotifications(services);
        var delayedRead = session.RefreshAsync();
        await network.WaitForRequestsAsync(2);
        services.GetRequiredService<AuthenticationNavigator>().LeaveForLoggedOut();

        // Act
        network.Respond(1, CreateBootstrapResponse(UserId, 1, "Ann", "delayed-token", FeatureFlagRegistry.BetaFeatures.Key));

        // Assert
        var delayedReadAct = () => delayedRead;
        await delayedReadAct.Should().ThrowAsync<OperationCanceledException>();
        session.Current.Should().BeNull();
        var featureFlagState = services.GetRequiredService<FeatureFlagState>();
        featureFlagState.UserId.Should().BeNull();
        featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeFalse();
        featureFlagState.IsEnabled(FeatureFlagRegistry.GoogleOauth).Should().BeFalse();
        notifications.Should().Equal("flags:");
        var tokenRead = services.GetRequiredService<BootstrapAntiforgeryTokenSource>().GetTokenAsync(CancellationToken.None).AsTask();
        var tokenReadAct = () => tokenRead;
        await tokenReadAct.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FeatureFlagsHeader_WhenAResponseSentBeforeATenantSwitchArrivesAfterIt_ShouldKeepTheNewTenantsFlags()
    {
        // Arrange
        var network = new ControlledNetwork();
        await using var services = CreateServices(network);
        var session = services.GetRequiredService<SessionState>();
        var initialRead = session.GetAsync();
        await network.WaitForRequestsAsync(1);
        network.Respond(0, CreateBootstrapResponse(UserId, 1, "Ann", "tenant-one-token", FeatureFlagRegistry.CompactView.Key));
        await initialRead;
        var delayedCall = services.GetRequiredService<UsersClient>().GetUserAsync(OtherUserId, CancellationToken.None);
        await network.WaitForRequestsAsync(2);
        var switchRead = session.RefreshAsync();
        await network.WaitForRequestsAsync(3);
        network.Respond(2, CreateBootstrapResponse(UserId, 2, "Ann", "tenant-two-token", FeatureFlagRegistry.BetaFeatures.Key));
        await switchRead;
        var notifications = RecordNotifications(services);

        // Act
        var delayedResponse = new HttpResponseMessage(HttpStatusCode.NotFound);
        delayedResponse.Headers.Add(AccountApiHeaders.UserFeatureFlags, FeatureFlagRegistry.CompactView.Key);
        network.Respond(1, delayedResponse);
        await delayedCall;

        // Assert
        var featureFlagState = services.GetRequiredService<FeatureFlagState>();
        featureFlagState.TenantId.Should().Be(new TenantId(2));
        featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeTrue();
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeFalse();
        session.Current!.User!.TenantId.Should().Be(new TenantId(2));
        notifications.Should().BeEmpty();
        (await SendStateChangingCallAsync(services, network)).Should().Be("tenant-two-token");
    }

    [Fact]
    public async Task FeatureFlagsHeader_WhenTheSwitchTenantResponseCarriesTheNewTenantsFlags_ShouldNotReportThemBesideTheOldTenant()
    {
        // Arrange
        var network = new ControlledNetwork();
        await using var services = CreateServices(network);
        var session = services.GetRequiredService<SessionState>();
        var featureFlagState = services.GetRequiredService<FeatureFlagState>();
        var initialRead = session.GetAsync();
        await network.WaitForRequestsAsync(1);
        network.Respond(0, CreateBootstrapResponse(UserId, 1, "Ann", "tenant-one-token", FeatureFlagRegistry.CompactView.Key));
        await initialRead;
        var observations = new List<string>();
        featureFlagState.Changed += () => observations.Add(
            $"tenant:{featureFlagState.TenantId?.Value}:{(featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures) ? "beta" : "")}:{(featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView) ? "compact" : "")}"
        );

        // Act
        // A tenant switch is a mutation, so the gate re-reads the version before it is forwarded
        var switchCall = services.GetRequiredService<AuthenticationClient>().SwitchTenantAsync(new SwitchTenantCommand(new TenantId(2)), CancellationToken.None);
        await network.WaitForRequestsAsync(2);
        network.Respond(1, CreateBootstrapResponse(UserId, 1, "Ann", "tenant-one-token", FeatureFlagRegistry.CompactView.Key));
        await network.WaitForRequestsAsync(3);
        var switchResponse = new HttpResponseMessage(HttpStatusCode.OK);
        switchResponse.Headers.Add(AccountApiHeaders.UserFeatureFlags, FeatureFlagRegistry.BetaFeatures.Key);
        network.Respond(2, switchResponse);
        (await switchCall).IsSuccess.Should().BeTrue();
        var flagsAfterSwitchResponse = (featureFlagState.TenantId, featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures));
        var refresh = session.RefreshAsync();
        await network.WaitForRequestsAsync(4);
        network.Respond(3, CreateBootstrapResponse(UserId, 2, "Ann", "tenant-two-token", FeatureFlagRegistry.BetaFeatures.Key));
        await refresh;

        // Assert
        network.Requests[1].Path.Should().Be(AccountApiRoutes.Bootstrap);
        network.Requests[2].Path.Should().Be(AccountApiRoutes.SwitchTenant);
        flagsAfterSwitchResponse.Should().Be((new TenantId(1), false));
        observations.Should().Equal("tenant:2:beta:");
        featureFlagState.TenantId.Should().Be(new TenantId(2));
        session.Current!.User!.TenantId.Should().Be(new TenantId(2));
        (await SendStateChangingCallAsync(services, network)).Should().Be("tenant-two-token");
    }

    [Fact]
    public async Task StateChangingCall_WhenItsLazyTokenReadRacesASessionRead_ShouldShareOneBootstrapRead()
    {
        // Arrange
        var network = new ControlledNetwork();
        await using var services = CreateServices(network);
        var session = services.GetRequiredService<SessionState>();
        // The gate's own re-check is the first request; the session read and the call's lazy token read follow it
        var deleteCall = services.GetRequiredService<UsersClient>().DeleteUserAsync(OtherUserId, CancellationToken.None);
        await network.WaitForRequestsAsync(1);
        network.Respond(0, CreateBootstrapResponse(UserId, 1, "Recheck", "recheck-token", FeatureFlagRegistry.BetaFeatures.Key));
        var sessionRead = session.GetAsync();
        await network.WaitForRequestsAsync(2);

        // Act
        network.Respond(1, CreateBootstrapResponse(UserId, 1, "Ann", "shared-token", FeatureFlagRegistry.BetaFeatures.Key));
        await network.WaitForRequestsAsync(3);
        network.Respond(2, new HttpResponseMessage(HttpStatusCode.NoContent));
        await deleteCall;

        // Assert
        (await sessionRead).AntiforgeryToken.Should().Be("shared-token");
        network.Requests.Select(request => request.Path).Should().Equal(AccountApiRoutes.Bootstrap, AccountApiRoutes.Bootstrap, AccountApiRoutes.User(OtherUserId));
        network.Requests[2].SentAntiforgeryToken.Should().Be("shared-token");
        session.Current!.User!.FirstName.Should().Be("Ann");
        session.Current.AntiforgeryToken.Should().Be("shared-token");
    }

    [Fact]
    public async Task StateChangingCall_WhenARefreshStartsDuringItsLazyTokenReadAndCompletesFirst_ShouldSendTheAcceptedToken()
    {
        // Arrange
        var network = new ControlledNetwork();
        await using var services = CreateServices(network);
        var session = services.GetRequiredService<SessionState>();
        // The first request is the gate's re-check, the second the call's lazy token read
        var deleteCall = services.GetRequiredService<UsersClient>().DeleteUserAsync(OtherUserId, CancellationToken.None);
        await network.WaitForRequestsAsync(1);
        network.Respond(0, CreateBootstrapResponse(UserId, 1, "Recheck", "recheck-token", FeatureFlagRegistry.BetaFeatures.Key));
        await network.WaitForRequestsAsync(2);
        var refresh = session.RefreshAsync();
        await network.WaitForRequestsAsync(3);

        // Act
        network.Respond(2, CreateBootstrapResponse(OtherUserId, 1, "Bob", "accepted-token", FeatureFlagRegistry.BetaFeatures.Key));
        await refresh;
        network.Respond(1, CreateBootstrapResponse(UserId, 1, "Ann", "discarded-token", FeatureFlagRegistry.CompactView.Key));
        await network.WaitForRequestsAsync(4);
        network.Respond(3, new HttpResponseMessage(HttpStatusCode.NoContent));
        await deleteCall;

        // Assert
        network.Requests[3].SentAntiforgeryToken.Should().Be("accepted-token");
        session.Current!.User!.FirstName.Should().Be("Bob");
        var featureFlagState = services.GetRequiredService<FeatureFlagState>();
        featureFlagState.UserId.Should().Be(OtherUserId);
        featureFlagState.IsEnabled(FeatureFlagRegistry.CompactView).Should().BeFalse();
        network.Requests.Count(request => request.Path == AccountApiRoutes.Bootstrap).Should().Be(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(null)]
    public async Task GetAsync_WhenTheFirstReadFails_ShouldLeaveStoresEmptyAndRecoverOnRetry(HttpStatusCode? failureStatusCode)
    {
        // Arrange
        var network = new ControlledNetwork();
        await using var services = CreateServices(network);
        var session = services.GetRequiredService<SessionState>();
        var failedRead = session.GetAsync();
        await network.WaitForRequestsAsync(1);
        if (failureStatusCode is { } statusCode)
        {
            network.Respond(0, new HttpResponseMessage(statusCode));
        }
        else
        {
            network.Fail(0, new HttpRequestException("The network is unreachable."));
        }

        var failedReadAct = () => failedRead;
        await failedReadAct.Should().ThrowAsync<InvalidOperationException>();
        services.GetRequiredService<FeatureFlagState>().UserId.Should().BeNull();
        session.Current.Should().BeNull();

        // Act
        var retry = session.GetAsync();
        await network.WaitForRequestsAsync(2);
        network.Respond(1, CreateBootstrapResponse(UserId, 1, "Ann", "retry-token", FeatureFlagRegistry.BetaFeatures.Key));
        var bootstrap = await retry;

        // Assert
        bootstrap.User!.FirstName.Should().Be("Ann");
        services.GetRequiredService<FeatureFlagState>().IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeTrue();
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().BeEmpty();
        (await SendStateChangingCallAsync(services, network)).Should().Be("retry-token");
    }

    [Fact]
    public async Task GetUnlessLeavingAsync_WhenTheBootstrapAnswersRevoked_ShouldReturnNullWithoutAnIdentityAndLeaveOnce()
    {
        // Arrange
        var network = new ControlledNetwork();
        await using var services = CreateServices(network);
        var session = services.GetRequiredService<SessionState>();
        var read = session.GetUnlessLeavingAsync();
        await network.WaitForRequestsAsync(1);

        // Act
        network.Respond(0, CreateUnauthorizedResponse("Revoked"));
        var bootstrap = await read;

        // Assert
        bootstrap.Should().BeNull();
        session.Current.Should().BeNull();
        services.GetRequiredService<FeatureFlagState>().UserId.Should().BeNull();
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().ContainSingle();
    }

    [Fact]
    public async Task RefreshUnlessLeavingAsync_WhenTheBootstrapAnswersRevokedAfterAnAcceptedRead_ShouldReturnNullAndClearTheIdentity()
    {
        // Arrange
        var network = new ControlledNetwork();
        await using var services = CreateServices(network);
        var session = services.GetRequiredService<SessionState>();
        var initialRead = session.GetUnlessLeavingAsync();
        await network.WaitForRequestsAsync(1);
        network.Respond(0, CreateBootstrapResponse(UserId, 1, "Ann", "first-token", FeatureFlagRegistry.BetaFeatures.Key));
        (await initialRead)!.User!.FirstName.Should().Be("Ann");
        var refresh = session.RefreshUnlessLeavingAsync();
        await network.WaitForRequestsAsync(2);

        // Act
        network.Respond(1, CreateUnauthorizedResponse("Revoked"));
        var bootstrap = await refresh;

        // Assert
        bootstrap.Should().BeNull();
        session.Current.Should().BeNull();
        (await session.GetUnlessLeavingAsync()).Should().BeNull();
        network.Requests.Should().HaveCount(2);
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().ContainSingle();
    }

    [Fact]
    public async Task UnlessLeavingAsync_WhenTheSurfaceIsLeftWhileATypedClientCallIsInFlight_ShouldReturnNullForTheCallAndLeaveOnce()
    {
        // Arrange
        var network = new ControlledNetwork();
        await using var services = CreateServices(network);
        var session = services.GetRequiredService<SessionState>();
        var initialRead = session.GetUnlessLeavingAsync();
        await network.WaitForRequestsAsync(1);
        network.Respond(0, CreateBootstrapResponse(UserId, 1, "Ann", "first-token", FeatureFlagRegistry.BetaFeatures.Key));
        await initialRead;
        var usersClient = services.GetRequiredService<UsersClient>();
        var call = session.UnlessLeavingAsync(usersClient.GetCurrentUserAsync);
        var refresh = session.RefreshUnlessLeavingAsync();
        await network.WaitForRequestsAsync(3);

        // Act
        network.Respond(2, CreateUnauthorizedResponse("Revoked"));
        var bootstrap = await refresh;
        network.Respond(1, new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var result = await call;
        var callAfterLeaving = await session.UnlessLeavingAsync(usersClient.GetCurrentUserAsync);

        // Assert
        bootstrap.Should().BeNull();
        result.Should().BeNull();
        callAfterLeaving.Should().BeNull();
        session.Current.Should().BeNull();
        network.Requests.Should().HaveCount(3);
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().ContainSingle();
    }

    private static ServiceProvider CreateServices(ControlledNetwork network)
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingNavigationManager>();
        services.AddSingleton<NavigationManager>(serviceProvider => serviceProvider.GetRequiredService<RecordingNavigationManager>());
        services.AddAccountApiClients(new Uri(RecordingNavigationManager.BaseAddress), () => network);
        services.AddScoped<DataListPageCache>();
        services.AddScoped<ToastService>();
        services.AddScoped<SessionState>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });
    }

    // Each notification records what the other store held when it was raised, so a notification raised between two parts
    // of a commit shows up as a mismatch
    private static List<string> RecordNotifications(ServiceProvider services)
    {
        var session = services.GetRequiredService<SessionState>();
        var featureFlagState = services.GetRequiredService<FeatureFlagState>();
        var notifications = new List<string>();
        featureFlagState.Changed += () => notifications.Add($"flags:{session.Current?.User?.FirstName}");
        session.Changed += () => notifications.Add($"session:{session.Current?.User?.FirstName}:{(featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures) ? "beta" : "")}");
        return notifications;
    }

    // The gate's re-check reads the bootstrap before the call is forwarded, and that read is answered on its own; the
    // returned token is the one the forwarded call carried
    private static async Task<string?> SendStateChangingCallAsync(ServiceProvider services, ControlledNetwork network)
    {
        var recheckIndex = network.Requests.Count;
        var call = services.GetRequiredService<UsersClient>().DeleteUserAsync(OtherUserId, CancellationToken.None);
        await network.WaitForRequestsAsync(recheckIndex + 1);
        network.Respond(recheckIndex, CreateBootstrapResponse(UserId, 1, "Ann", "recheck-token", FeatureFlagRegistry.BetaFeatures.Key));
        await network.WaitForRequestsAsync(recheckIndex + 2);
        network.Respond(recheckIndex + 1, new HttpResponseMessage(HttpStatusCode.NoContent));
        await call;
        network.Requests[recheckIndex].Path.Should().Be(AccountApiRoutes.Bootstrap);
        return network.Requests[recheckIndex + 1].SentAntiforgeryToken;
    }

    private static HttpResponseMessage CreateBootstrapResponse(UserId userId, long tenantId, string firstName, string antiforgeryToken, string featureFlag)
    {
        var user = new BootstrapUser(userId, new TenantId(tenantId), "Owner", "owner@example.com", firstName, null, null, null, null, null, null, false, [featureFlag]);
        var bootstrap = new BootstrapResponse(true, user, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool> { [FeatureFlagRegistry.GoogleOauth.Key] = true }, antiforgeryToken);
        var json = JsonSerializer.Serialize(bootstrap, ApiJsonSerializerOptions.Create());
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        response.Headers.Add(AccountApiHeaders.UserFeatureFlags, featureFlag);
        return response;
    }

    private static HttpResponseMessage CreateUnauthorizedResponse(string unauthorizedReason)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.Add(AuthenticationNavigator.UnauthorizedReasonHeaderName, unauthorizedReason);
        return response;
    }

    private sealed record RecordedRequest(string Path, string? SentAntiforgeryToken, TaskCompletionSource<HttpResponseMessage> Response);

    // Holds every response until the test releases it, and ignores cancellation the way a response already on the wire does
    private sealed class ControlledNetwork : HttpMessageHandler
    {
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

        private readonly Lock _lock = new();
        private readonly List<RecordedRequest> _requests = [];

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_lock)
                {
                    return _requests.ToArray();
                }
            }
        }

        public void Respond(int index, HttpResponseMessage response)
        {
            Requests[index].Response.SetResult(response);
        }

        public void Fail(int index, Exception exception)
        {
            Requests[index].Response.SetException(exception);
        }

        public async Task WaitForRequestsAsync(int count)
        {
            var deadline = DateTime.UtcNow + WaitTimeout;
            while (Requests.Count < count)
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException($"Expected {count} requests but {Requests.Count} were sent.");
                await Task.Delay(TimeSpan.FromMilliseconds(5));
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sentAntiforgeryToken = request.Headers.TryGetValues(AccountApiHeaders.AntiforgeryToken, out var values) ? values.Single() : null;
            var recordedRequest = new RecordedRequest(request.RequestUri!.AbsolutePath, sentAntiforgeryToken, new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously));
            lock (_lock)
            {
                _requests.Add(recordedRequest);
            }

            return recordedRequest.Response.Task;
        }
    }

    private sealed class RecordingNavigationManager : NavigationManager
    {
        public const string BaseAddress = "https://app.dev.localhost:9000/";

        public RecordingNavigationManager()
        {
            Initialize(BaseAddress, $"{BaseAddress}blazor/app/users");
        }

        public List<string> Navigations { get; } = [];

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Navigations.Add(uri);
        }
    }
}
