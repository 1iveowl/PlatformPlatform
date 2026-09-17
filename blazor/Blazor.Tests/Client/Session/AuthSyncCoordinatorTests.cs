using System.Net;
using System.Text;
using System.Text.Json;
using Account.Client;
using Account.Features.Authentication.Queries;
using Account.Features.Users.Requests;
using Blazor.Client.Bootstrap;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using Blazor.Client.Session;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using SharedKernel.ApiResults;
using SharedKernel.Domain;

namespace Blazor.Tests.Client.Session;

// The coordinator over the real WebAssembly registration: an identity ended by another tab stops writes, discards pending
// reads and clears the session before the dialog state is set; stale and duplicate messages change nothing; and the
// server reconciliation adopts nothing.
public sealed class AuthSyncCoordinatorTests
{
    private const string BootstrapPath = "/api/account/bootstrap";
    private const string CurrentUserPath = "/api/account/users/me";
    private const string UserId = "usr_01JZ8Q4N6V3K2M7P9R5T0W1XYZ";

    [Fact]
    public async Task AttachAsync_WhenAuthenticated_ShouldAnnounceTheIdentityOnce()
    {
        // Arrange
        var network = new Network();
        await using var services = await CreateSignedInServicesAsync(network);
        var coordinator = services.GetRequiredService<AuthSyncCoordinator>();
        var bootstrap = services.GetRequiredService<SessionState>().Current!;

        // Act
        await coordinator.AttachAsync(bootstrap);
        await coordinator.AttachAsync(bootstrap);

        // Assert
        var javaScript = services.GetRequiredService<RecordingJavaScript>();
        javaScript.Calls.Should().Equal("import ./js/auth-sync.js", "attach");
        javaScript.Announcements.Should().ContainSingle().Which.Should().Be(AuthSyncRules.LoggedIn(bootstrap.User!));
    }

    [Fact]
    public async Task OnMessage_WhenAnotherTabLoggedOut_ShouldEndTheSessionBeforeTheDialogAndRefuseWritesAndDiscardPendingReads()
    {
        // Arrange
        var network = new Network();
        await using var services = await CreateAttachedServicesAsync(network);
        var coordinator = services.GetRequiredService<AuthSyncCoordinator>();
        var session = services.GetRequiredService<SessionState>();
        var navigator = services.GetRequiredService<AuthenticationNavigator>();
        var pendingRead = network.Hold(CurrentUserPath);
        var read = session.UnlessLeavingAsync(services.GetRequiredService<UsersClient>().GetCurrentUserAsync);
        var sessionEndedBeforeDialog = false;
        coordinator.Changed += () => sessionEndedBeforeDialog = navigator.IsLeaving && session.Current is null;

        // Act
        await coordinator.OnMessage(new AuthSyncMessage(AuthSyncRules.UserLoggedOutType, UserId, null, null, null, null, null, 10));
        pendingRead.SetResult(CreateJsonResponse(new { }));
        var readResult = await read;
        network.RequestsAfterSignIn.Clear();
        var write = await services.GetRequiredService<UsersClient>().UpdateCurrentUserAsync(new UpdateCurrentUserCommand("Ann", "Lee", ""), CancellationToken.None);

        // Assert
        coordinator.Invalidation.Should().Be(new AuthSyncDecision(AuthSyncOutcome.LoggedOut));
        sessionEndedBeforeDialog.Should().BeTrue();
        readResult.Should().BeNull();
        write.Outcome.Should().Be(ApiCallOutcome.TransportFailure);
        network.RequestsAfterSignIn.Should().BeEmpty();
        services.GetRequiredService<TestNavigationManager>().Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task OnMessage_WhenMessagesAreDuplicatedOrOutOfOrder_ShouldProcessOnlyNewerOnes()
    {
        // Arrange
        var network = new Network();
        await using var services = await CreateAttachedServicesAsync(network);
        var coordinator = services.GetRequiredService<AuthSyncCoordinator>();
        var sameTenant = new AuthSyncMessage(AuthSyncRules.TenantSwitchedType, UserId, null, "1", "2", "Acme", null, 20);
        var olderSwitch = new AuthSyncMessage(AuthSyncRules.TenantSwitchedType, UserId, null, "2", "1", "Globex", null, 10);

        // Act
        await coordinator.OnMessage(sameTenant);
        await coordinator.OnMessage(sameTenant);
        await coordinator.OnMessage(olderSwitch);

        // Assert
        coordinator.Invalidation.Should().BeNull();
        services.GetRequiredService<AuthenticationNavigator>().IsLeaving.Should().BeFalse();
    }

    [Fact]
    public async Task Reconcile_WhenTheServerHoldsAnotherTenant_ShouldEndTheIdentityWithoutCommittingTheServerBootstrap()
    {
        // Arrange
        var network = new Network();
        await using var services = await CreateAttachedServicesAsync(network);
        var coordinator = services.GetRequiredService<AuthSyncCoordinator>();
        network.Respond(BootstrapPath, () => CreateBootstrapResponse(new TenantId(2)));

        // Act
        await coordinator.Reconcile();

        // Assert
        coordinator.Invalidation.Should().Be(new AuthSyncDecision(AuthSyncOutcome.TenantSwitched, "Tenant 2"));
        services.GetRequiredService<SessionState>().Current.Should().BeNull();
        network.RequestsAfterSignIn.Should().Equal($"GET {BootstrapPath}");
    }

    [Fact]
    public async Task Reconcile_WhenTheServerMatches_ShouldChangeNothing()
    {
        // Arrange
        var network = new Network();
        await using var services = await CreateAttachedServicesAsync(network);
        var coordinator = services.GetRequiredService<AuthSyncCoordinator>();

        // Act
        await coordinator.Reconcile();

        // Assert
        coordinator.Invalidation.Should().BeNull();
        services.GetRequiredService<SessionState>().Current.Should().NotBeNull();
    }

    [Fact]
    public async Task Reconcile_WhenTheSessionEndedOnTheServer_ShouldLeaveThroughTheUnauthorizedHandler()
    {
        // Arrange
        var network = new Network();
        await using var services = await CreateAttachedServicesAsync(network);
        var coordinator = services.GetRequiredService<AuthSyncCoordinator>();
        network.Respond(BootstrapPath, () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                response.Headers.Add(AccountApiHeaders.UnauthorizedReason, "Revoked");
                return response;
            }
        );

        // Act
        await coordinator.Reconcile();

        // Assert
        coordinator.Invalidation.Should().BeNull();
        services.GetRequiredService<TestNavigationManager>().Navigations.Should().Equal(new RecordedNavigation("/blazor/error?error=session_revoked", true));
    }

    [Fact]
    public async Task Reload_ShouldLoadTheCurrentPageAsANewDocument()
    {
        // Arrange
        var network = new Network();
        await using var services = await CreateAttachedServicesAsync(network);
        var coordinator = services.GetRequiredService<AuthSyncCoordinator>();
        await coordinator.OnMessage(new AuthSyncMessage(AuthSyncRules.UserLoggedOutType, UserId, null, null, null, null, null, 10));

        // Act
        coordinator.Reload();

        // Assert
        services.GetRequiredService<TestNavigationManager>().Navigations.Should().Equal(new RecordedNavigation("/blazor/development/form-errors/interactive?tab=forms", true));
    }

    private static async Task<ServiceProvider> CreateAttachedServicesAsync(Network network)
    {
        var services = await CreateSignedInServicesAsync(network);
        await services.GetRequiredService<AuthSyncCoordinator>().AttachAsync(services.GetRequiredService<SessionState>().Current!);
        return services;
    }

    private static async Task<ServiceProvider> CreateSignedInServicesAsync(Network network)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestNavigationManager>();
        services.AddSingleton<NavigationManager>(serviceProvider => serviceProvider.GetRequiredService<TestNavigationManager>());
        services.AddSingleton<RecordingJavaScript>();
        services.AddSingleton<IJSRuntime>(serviceProvider => serviceProvider.GetRequiredService<RecordingJavaScript>());
        services.AddAccountApiClients(new Uri(TestNavigationManager.BaseAddress), () => network);
        services.AddScoped<DataListPageCache>();
        services.AddScoped<ToastService>();
        services.AddScoped<SessionState>();
        services.AddScoped<AuthSyncCoordinator>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });

        network.Respond(BootstrapPath, () => CreateBootstrapResponse(new TenantId(1)));
        await provider.GetRequiredService<SessionState>().GetAsync();
        network.RequestsAfterSignIn.Clear();
        return provider;
    }

    private static HttpResponseMessage CreateBootstrapResponse(TenantId tenantId)
    {
        var user = new BootstrapUser(new UserId(UserId), tenantId, "Owner", "owner@example.com", "Ann", "Lee", null, null, $"Tenant {tenantId.Value}", null, null, false, []);
        return CreateJsonResponse(new BootstrapResponse(true, user, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool>(), "antiforgery-token"));
    }

    private static HttpResponseMessage CreateJsonResponse<T>(T body)
    {
        var json = JsonSerializer.Serialize(body, ApiJsonSerializerOptions.Create());
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class Network : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<Task<HttpResponseMessage>>> _responses = [];

        public List<string> RequestsAfterSignIn { get; } = [];

        public void Respond(string path, Func<HttpResponseMessage> respond)
        {
            _responses[path] = () => Task.FromResult(respond());
        }

        public TaskCompletionSource<HttpResponseMessage> Hold(string path)
        {
            var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _responses[path] = () => response.Task;
            return response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            RequestsAfterSignIn.Add($"{request.Method} {path}");
            return _responses[path]();
        }
    }

    // The module and the channel handle: records the import, the attach and every announcement
    private sealed class RecordingJavaScript : IJSRuntime, IJSObjectReference
    {
        public List<string> Calls { get; } = [];

        public List<AuthSyncMessage> Announcements { get; } = [];

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            try
            {
                return ValueTask.FromResult(Record<TValue>(identifier, args));
            }
            catch (JSException exception)
            {
                return ValueTask.FromException<TValue>(exception);
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            try
            {
                return ValueTask.FromResult(Record<TValue>(identifier, args));
            }
            catch (JSException exception)
            {
                return ValueTask.FromException<TValue>(exception);
            }
        }

        private TValue Record<TValue>(string identifier, object?[]? args)
        {
            switch (identifier)
            {
                case "import":
                    Calls.Add($"import {args![0]}");
                    break;
                case "attach":
                    Calls.Add(identifier);
                    Announcements.Add((AuthSyncMessage)args![1]!);
                    break;
                case "post":
                    Calls.Add(identifier);
                    Announcements.Add((AuthSyncMessage)args![0]!);
                    break;
                default:
                    Calls.Add(identifier);
                    break;
            }

            return this is TValue handle ? handle : default!;
        }
    }
}
