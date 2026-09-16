using System.Net;
using System.Text;
using System.Text.Json;
using Account.Client;
using Account.Features.Authentication.Queries;
using Blazor.Client.Bootstrap;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.ApiResults;
using SharedKernel.Domain;
using FeatureFlagRegistry = SharedKernel.FeatureFlags.FeatureFlags;

namespace Blazor.Tests.Client;

// The WebAssembly registration with a recording stand-in for the network: the bootstrap adapter, the antiforgery token
// source and the single authentication-loss navigation through UnauthorizedResponseHandler.
public sealed class WebAssemblyAccountApiTests
{
    private const string AntiforgeryToken = "bootstrap-antiforgery-token";

    private static readonly UserId UserId = new("usr_01JZ8Q4N6V3K2M7P9R5T0W1XYZ");

    [Fact]
    public async Task GetBootstrap_WhenAuthenticated_ShouldInitializeFeatureFlagsAndStoreAntiforgeryToken()
    {
        // Arrange
        var network = new RecordingNetwork(_ => CreateBootstrapResponse());
        await using var services = CreateServices(network);

        // Act
        var bootstrap = await services.GetRequiredService<IBootstrapSource>().GetAsync();

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
    public async Task GetBootstrap_WhenSessionIsRevoked_ShouldResetStateAndNavigateToErrorPageOnce()
    {
        // Arrange
        var responses = new Queue<HttpResponseMessage>([CreateBootstrapResponse(), CreateUnauthorizedResponse("Revoked"), CreateUnauthorizedResponse("Revoked")]);
        var network = new RecordingNetwork(_ => responses.Dequeue());
        await using var services = CreateServices(network);
        var bootstrapSource = services.GetRequiredService<IBootstrapSource>();
        await bootstrapSource.GetAsync();

        // Act
        var first = await bootstrapSource.GetAsync();
        var second = await bootstrapSource.GetAsync();

        // Assert
        first.IsAuthenticated.Should().BeFalse();
        second.IsAuthenticated.Should().BeFalse();
        var featureFlagState = services.GetRequiredService<FeatureFlagState>();
        featureFlagState.UserId.Should().BeNull();
        featureFlagState.IsEnabled(FeatureFlagRegistry.BetaFeatures).Should().BeFalse();
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

    private static ServiceProvider CreateServices(RecordingNetwork network)
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingNavigationManager>();
        services.AddSingleton<NavigationManager>(serviceProvider => serviceProvider.GetRequiredService<RecordingNavigationManager>());
        services.AddAccountApiClients(new Uri(RecordingNavigationManager.BaseAddress), () => network);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });
    }

    private static HttpResponseMessage CreateBootstrapResponse()
    {
        var user = new BootstrapUser(UserId, new TenantId(1), "Owner", "owner@example.com", null, null, null, null, null, null, null, false, [FeatureFlagRegistry.BetaFeatures.Key]);
        var bootstrap = new BootstrapResponse(true, user, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool> { [FeatureFlagRegistry.GoogleOauth.Key] = true }, AntiforgeryToken);
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
