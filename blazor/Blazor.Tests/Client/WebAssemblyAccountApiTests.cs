using System.Net;
using System.Text;
using System.Text.Json;
using Account.Client;
using Account.Features.Authentication.Queries;
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

namespace Blazor.Tests.Client;

// The WebAssembly registration with a recording stand-in for the network: the bootstrap adapter, the session state that
// applies an accepted bootstrap, the antiforgery token source and the single authentication-loss navigation through
// UnauthorizedResponseHandler.
public sealed class WebAssemblyAccountApiTests
{
    private const string AntiforgeryToken = "bootstrap-antiforgery-token";

    // A major version this client can never be built at, so the window is unsupported whatever version the test host reports
    private const string ServerVersionOutsideTheWindow = "9999.0.0";

    private static readonly UserId UserId = new("usr_01JZ8Q4N6V3K2M7P9R5T0W1XYZ");

    [Fact]
    public async Task GetSession_WhenAuthenticated_ShouldInitializeFeatureFlagsAndStoreAntiforgeryToken()
    {
        // Arrange
        var network = new RecordingNetwork(_ => CreateBootstrapResponse());
        await using var services = CreateServices(network);

        // Act
        var bootstrap = await services.GetRequiredService<SessionState>().GetAsync();

        // Assert
        bootstrap.IsAuthenticated.Should().BeTrue();
        var featureFlagState = services.GetRequiredService<FeatureFlagState>();
        featureFlagState.UserId.Should().Be(UserId);
        featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeTrue();
        featureFlagState.IsEnabled(FeatureFlagRegistry.GoogleOauth).Should().BeTrue();
        (await services.GetRequiredService<BootstrapAntiforgeryTokenSource>().GetTokenAsync(CancellationToken.None)).Should().Be(AntiforgeryToken);
        network.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task GetBootstrap_WhenReadDirectly_ShouldChangeNoClientState()
    {
        // Arrange
        var network = new RecordingNetwork(_ => CreateBootstrapResponse());
        await using var services = CreateServices(network);

        // Act
        var bootstrap = await services.GetRequiredService<IBootstrapSource>().GetAsync();

        // Assert
        bootstrap.IsAuthenticated.Should().BeTrue();
        services.GetRequiredService<FeatureFlagState>().UserId.Should().BeNull();
        services.GetRequiredService<FeatureFlagState>().IsEnabled(FeatureFlagRegistry.GoogleOauth).Should().BeFalse();
        services.GetRequiredService<SessionState>().Current.Should().BeNull();
    }

    [Fact]
    public async Task GetBootstrap_WhenSessionIsRevoked_ShouldResetStateNavigateToErrorPageOnceAndRefuseLaterReads()
    {
        // Arrange
        var responses = new Queue<HttpResponseMessage>([CreateBootstrapResponse(), CreateUnauthorizedResponse("Revoked"), CreateUnauthorizedResponse("Revoked")]);
        var network = new RecordingNetwork(_ => responses.Dequeue());
        await using var services = CreateServices(network);
        var session = services.GetRequiredService<SessionState>();
        await session.GetAsync();
        var bootstrapSource = services.GetRequiredService<IBootstrapSource>();

        // Act
        var first = await bootstrapSource.GetAsync();
        var second = () => bootstrapSource.GetAsync();

        // Assert
        first.IsAuthenticated.Should().BeFalse();
        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*TransportFailure*");
        network.Requests.Should().HaveCount(2);
        var featureFlagState = services.GetRequiredService<FeatureFlagState>();
        featureFlagState.UserId.Should().BeNull();
        featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeFalse();
        session.Current.Should().BeNull();
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal("/blazor/error?error=session_revoked");
    }

    [Fact]
    public async Task GetBootstrap_WhenUnauthorizedWithoutReason_ShouldNavigateToLoginWithReturnPathOnce()
    {
        // Arrange
        var network = new RecordingNetwork(_ => CreateUnauthorizedResponse(null));
        await using var services = CreateServices(network);

        // Act
        var bootstrap = await services.GetRequiredService<IBootstrapSource>().GetAsync();

        // Assert
        bootstrap.IsAuthenticated.Should().BeFalse();
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal("/blazor/login?returnPath=%2Fblazor%2Fapp%2Fusers%3Fmode%3Dpaged");
    }

    [Fact]
    public async Task GetBootstrap_WhenEndpointFails_ShouldThrowWithoutNavigating()
    {
        // Arrange
        var network = new RecordingNetwork(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await using var services = CreateServices(network);

        // Act
        var bootstrapSource = services.GetRequiredService<IBootstrapSource>();
        var act = () => bootstrapSource.GetAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Failure*500*");
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task StateChangingCalls_BeforeAnyBootstrapRead_ShouldReadBootstrapOnceAndSendItsToken()
    {
        // Arrange
        var network = new RecordingNetwork(request => request.RequestUri!.AbsolutePath == AccountApiRoutes.Bootstrap ? CreateBootstrapResponse() : new HttpResponseMessage(HttpStatusCode.NoContent));
        await using var services = CreateServices(network);
        var usersClient = services.GetRequiredService<UsersClient>();

        // Act
        await usersClient.DeleteUserAsync(UserId, CancellationToken.None);
        await usersClient.DeleteUserAsync(UserId, CancellationToken.None);

        // Assert
        network.Requests.Select(request => request.Path).Should().Equal(AccountApiRoutes.Bootstrap, AccountApiRoutes.User(UserId), AccountApiRoutes.User(UserId));
        network.Requests.Where(request => request.Method == HttpMethod.Delete).Should().OnlyContain(request => request.SentAntiforgeryToken == AntiforgeryToken);
    }

    [Fact]
    public async Task ReadCall_ShouldNeitherReadBootstrapNorSendAntiforgeryToken()
    {
        // Arrange
        var network = new RecordingNetwork(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await using var services = CreateServices(network);

        // Act
        await services.GetRequiredService<UsersClient>().GetUserAsync(UserId, CancellationToken.None);

        // Assert
        network.Requests.Should().ContainSingle().Which.Should().Be(new RecordedRequest(HttpMethod.Get, AccountApiRoutes.User(UserId), null));
    }

    [Fact]
    public async Task StateChangingCall_WhenTheClientIsOutsideTheVersionWindow_ShouldNotBeSentAndShouldAskForAReload()
    {
        // Arrange
        var network = new RecordingNetwork(_ => CreateBootstrapResponse(ServerVersionOutsideTheWindow));
        await using var services = CreateServices(network);
        await services.GetRequiredService<SessionState>().GetAsync();

        // Act
        var result = await services.GetRequiredService<UsersClient>().DeleteUserAsync(UserId, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Problem!.StatusCode.Should().Be((int)StaleClientRequestHandler.RefusalStatusCode);
        ApiFailureClassifier.Classify(result).Kind.Should().Be(ApiFailureKind.Version);
        network.Requests.Select(request => request.Path).Should().Equal(AccountApiRoutes.Bootstrap);
        services.GetRequiredService<ClientVersionState>().Support.Should().Be(ClientVersionSupport.Unsupported);
    }

    [Fact]
    public async Task ReadCallAndLogout_WhenTheClientIsOutsideTheVersionWindow_ShouldStillBeSent()
    {
        // Arrange
        var network = new RecordingNetwork(request =>
            request.RequestUri!.AbsolutePath == AccountApiRoutes.Bootstrap
                ? CreateBootstrapResponse(ServerVersionOutsideTheWindow)
                : new HttpResponseMessage(HttpStatusCode.NoContent)
        );
        await using var services = CreateServices(network);
        await services.GetRequiredService<SessionState>().GetAsync();

        // Act
        await services.GetRequiredService<UsersClient>().GetUserAsync(UserId, CancellationToken.None);
        await services.GetRequiredService<AuthenticationClient>().LogoutAsync(CancellationToken.None);

        // Assert
        network.Requests.Select(request => request.Path).Should().Equal(AccountApiRoutes.Bootstrap, AccountApiRoutes.User(UserId), AccountApiRoutes.Logout);
    }

    [Fact]
    public async Task StateChangingCall_WhenAFingerprintedAssetIsNoLongerServed_ShouldNotBeSent()
    {
        // Arrange
        var network = new RecordingNetwork(_ => CreateBootstrapResponse());
        await using var services = CreateServices(network);
        await services.GetRequiredService<SessionState>().GetAsync();
        services.GetRequiredService<ClientVersionState>().ReportMissingAsset("/blazor/_framework/Blazor.Client.6kbltrhlw8.wasm");

        // Act
        var result = await services.GetRequiredService<UsersClient>().DeleteUserAsync(UserId, CancellationToken.None);

        // Assert
        ApiFailureClassifier.Classify(result).Kind.Should().Be(ApiFailureKind.Version);
        network.Requests.Select(request => request.Path).Should().Equal(AccountApiRoutes.Bootstrap);
    }

    private static ServiceProvider CreateServices(RecordingNetwork network)
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

    private static HttpResponseMessage CreateBootstrapResponse(string? applicationVersion = null)
    {
        var runtimeConfiguration = new Dictionary<string, string>();
        if (applicationVersion is not null) runtimeConfiguration[BootstrapConfiguration.ApplicationVersionKey] = applicationVersion;

        var user = new BootstrapUser(UserId, new TenantId(1), "Owner", "owner@example.com", null, null, null, null, null, null, null, false, [FeatureFlagRegistry.BetaFeatures.Key]);
        var bootstrap = new BootstrapResponse(true, user, "en-US", runtimeConfiguration, new Dictionary<string, bool> { [FeatureFlagRegistry.GoogleOauth.Key] = true }, AntiforgeryToken);
        var json = JsonSerializer.Serialize(bootstrap, ApiJsonSerializerOptions.Create());
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static HttpResponseMessage CreateUnauthorizedResponse(string? unauthorizedReason)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        if (unauthorizedReason is not null) response.Headers.Add(AccountApiHeaders.UnauthorizedReason, unauthorizedReason);
        return response;
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? SentAntiforgeryToken);

    private sealed class RecordingNetwork(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sentAntiforgeryToken = request.Headers.TryGetValues(AccountApiHeaders.AntiforgeryToken, out var values) ? values.Single() : null;
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, sentAntiforgeryToken));
            return Task.FromResult(respond(request));
        }
    }

    private sealed class RecordingNavigationManager : NavigationManager
    {
        public const string BaseAddress = "https://app.dev.localhost:9000/";

        public RecordingNavigationManager()
        {
            Initialize(BaseAddress, $"{BaseAddress}blazor/app/users?mode=paged");
        }

        public List<string> Navigations { get; } = [];

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Navigations.Add(uri);
        }
    }
}
