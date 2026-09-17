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

// Logout and tenant switch over the real WebAssembly registration, with a scripted network: every logout outcome, the
// reconciliation of a lost response without replaying the mutation, serialization of transitions, the refusal of competing
// writes, and the end of the previous identity after a switch whatever happens to the optional preference cookie.
public sealed class SessionTransitionTests
{
    private const string BootstrapPath = "/api/account/bootstrap";
    private const string LogoutPath = "/api/account/authentication/logout";
    private const string SwitchTenantPath = "/api/account/authentication/switch-tenant";
    private const string CurrentUserPath = "/api/account/users/me";
    private const string LoggedOutDestination = "/blazor/login";
    private const string AuthenticatedHomeDestination = "/blazor/app";

    private static readonly TenantId CurrentTenantId = new(1);
    private static readonly TenantId OtherTenantId = new(2);

    [Fact]
    public async Task LogoutAsync_WhenAccepted_ShouldLeaveForLoginOnce()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(LogoutPath, () => new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var outcome = await services.GetRequiredService<SessionTransition>().LogoutAsync();

        // Assert
        outcome.Should().Be(LogoutOutcome.LoggedOut);
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal(LoggedOutDestination);
        network.RequestsAfterSignIn.Should().Equal($"POST {LogoutPath}");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task LogoutAsync_WhenRejected_ShouldStaySignedInWithRetryStateAndNoReconciliationRead(HttpStatusCode statusCode)
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(LogoutPath, () => new HttpResponseMessage(statusCode));
        var transition = services.GetRequiredService<SessionTransition>();

        // Act
        var outcome = await transition.LogoutAsync();

        // Assert
        outcome.Should().Be(LogoutOutcome.Failed);
        transition.Status.Should().Be(SessionTransitionStatus.LogoutFailed);
        transition.IsBusy.Should().BeFalse();
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().BeEmpty();
        services.GetRequiredService<AuthenticationNavigator>().IsLeaving.Should().BeFalse();
        services.GetRequiredService<SessionState>().Current.Should().NotBeNull();
        network.RequestsAfterSignIn.Should().Equal($"POST {LogoutPath}");
    }

    [Fact]
    public async Task LogoutAsync_WhenRetriedAfterRejection_ShouldLeaveForLogin()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        var responses = new Queue<HttpStatusCode>([HttpStatusCode.InternalServerError, HttpStatusCode.OK]);
        network.Respond(LogoutPath, () => new HttpResponseMessage(responses.Dequeue()));
        var transition = services.GetRequiredService<SessionTransition>();
        await transition.LogoutAsync();

        // Act
        var outcome = await transition.LogoutAsync();

        // Assert
        outcome.Should().Be(LogoutOutcome.LoggedOut);
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal(LoggedOutDestination);
        network.RequestsAfterSignIn.Should().Equal($"POST {LogoutPath}", $"POST {LogoutPath}");
    }

    [Fact]
    public async Task LogoutAsync_WhenUnauthorized_ShouldLeaveThroughTheUnauthorizedHandlerOnly()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(LogoutPath, () => CreateUnauthorizedResponse("Revoked"));

        // Act
        var outcome = await services.GetRequiredService<SessionTransition>().LogoutAsync();

        // Assert
        outcome.Should().Be(LogoutOutcome.SessionEnded);
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal("/blazor/error?error=session_revoked");
        network.RequestsAfterSignIn.Should().Equal($"POST {LogoutPath}");
    }

    [Fact]
    public async Task LogoutAsync_WhenResponseIsLostAfterTheServerCommittedAndTheBrowserStillHoldsTheSession_ShouldShowUnconfirmedWithoutReplaying()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(LogoutPath, () => throw new HttpRequestException("The connection was closed after the request was sent."));
        network.Respond(BootstrapPath, () => CreateBootstrapResponse(CurrentTenantId));
        var transition = services.GetRequiredService<SessionTransition>();

        // Act
        var outcome = await transition.LogoutAsync();

        // Assert
        outcome.Should().Be(LogoutOutcome.Unconfirmed);
        transition.Status.Should().Be(SessionTransitionStatus.LogoutUnconfirmed);
        transition.IsBusy.Should().BeFalse();
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().BeEmpty();
        network.RequestsAfterSignIn.Should().Equal($"POST {LogoutPath}", $"GET {BootstrapPath}");
    }

    [Fact]
    public async Task LogoutAsync_WhenResponseIsLostAndTheReconciliationReadIsUnauthorized_ShouldLeaveThroughTheUnauthorizedHandler()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(LogoutPath, () => throw new HttpRequestException("The connection was closed after the request was sent."));
        network.Respond(BootstrapPath, () => CreateUnauthorizedResponse("Revoked"));

        // Act
        var outcome = await services.GetRequiredService<SessionTransition>().LogoutAsync();

        // Assert
        outcome.Should().Be(LogoutOutcome.SessionEnded);
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal("/blazor/error?error=session_revoked");
        network.RequestsAfterSignIn.Should().Equal($"POST {LogoutPath}", $"GET {BootstrapPath}");
    }

    [Fact]
    public async Task LogoutAsync_WhenTimedOutAndTheBrowserHoldsNoSession_ShouldLeaveForLogin()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(LogoutPath, () => throw new TaskCanceledException("The request timed out."));
        network.Respond(BootstrapPath, CreateAnonymousBootstrapResponse);

        // Act
        var outcome = await services.GetRequiredService<SessionTransition>().LogoutAsync();

        // Assert
        outcome.Should().Be(LogoutOutcome.LoggedOut);
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal(LoggedOutDestination);
        network.RequestsAfterSignIn.Should().Equal($"POST {LogoutPath}", $"GET {BootstrapPath}");
    }

    [Fact]
    public async Task LogoutAsync_WhenDisconnectedAndTheReconciliationReadFails_ShouldShowUnconfirmed()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(LogoutPath, () => throw new HttpRequestException("The network is unreachable."));
        network.Respond(BootstrapPath, () => throw new HttpRequestException("The network is unreachable."));
        var transition = services.GetRequiredService<SessionTransition>();

        // Act
        var outcome = await transition.LogoutAsync();

        // Assert
        outcome.Should().Be(LogoutOutcome.Unconfirmed);
        transition.Status.Should().Be(SessionTransitionStatus.LogoutUnconfirmed);
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().BeEmpty();
        network.RequestsAfterSignIn.Should().Equal($"POST {LogoutPath}", $"GET {BootstrapPath}");
    }

    [Fact]
    public async Task LogoutAsync_WhileATenantSwitchIsInFlight_ShouldBeIgnoredAndRefuseCompetingWrites()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        var switchResponse = network.Hold(SwitchTenantPath);
        var transition = services.GetRequiredService<SessionTransition>();
        var switching = transition.SwitchTenantAsync(OtherTenantId);

        // Act
        var logoutOutcome = await transition.LogoutAsync();
        var competingWrite = await services.GetRequiredService<UsersClient>().UpdateCurrentUserAsync(new UpdateCurrentUserCommand("Ann", "Lee", ""), CancellationToken.None);
        var busyWhileSwitching = transition.IsBusy;
        switchResponse.SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        var switchResult = await switching;

        // Assert
        logoutOutcome.Should().Be(LogoutOutcome.Ignored);
        competingWrite.Outcome.Should().Be(ApiCallOutcome.TransportFailure);
        busyWhileSwitching.Should().BeTrue();
        switchResult.Outcome.Should().Be(TenantSwitchOutcome.Switched);
        network.RequestsAfterSignIn.Should().Equal($"POST {SwitchTenantPath}");
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal(AuthenticatedHomeDestination);
    }

    [Fact]
    public async Task SwitchTenantAsync_WhenAccepted_ShouldEndThePreviousIdentityBeforeRememberingThePreferenceAndLeaveForHome()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(SwitchTenantPath, () => new HttpResponseMessage(HttpStatusCode.OK));
        var session = services.GetRequiredService<SessionState>();
        var navigator = services.GetRequiredService<AuthenticationNavigator>();
        var javaScript = services.GetRequiredService<ScriptedJavaScript>();
        var endedBeforePreference = false;
        javaScript.OnRemember = () => endedBeforePreference = navigator.IsLeaving && session.RequestsAborted.IsCancellationRequested && session.Current is null;

        // Act
        var result = await services.GetRequiredService<SessionTransition>().SwitchTenantAsync(OtherTenantId);

        // Assert
        result.Outcome.Should().Be(TenantSwitchOutcome.Switched);
        endedBeforePreference.Should().BeTrue();
        javaScript.RememberedTenants.Should().Equal("2");
        services.GetRequiredService<FeatureFlagState>().UserId.Should().BeNull();
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal(AuthenticatedHomeDestination);
    }

    [Fact]
    public async Task SwitchTenantAsync_WhenThePreferenceModuleFails_ShouldStillLeaveForHome()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(SwitchTenantPath, () => new HttpResponseMessage(HttpStatusCode.OK));
        services.GetRequiredService<ScriptedJavaScript>().ImportFailure = new JSException("Failed to fetch dynamically imported module.");

        // Act
        var result = await services.GetRequiredService<SessionTransition>().SwitchTenantAsync(OtherTenantId);

        // Assert
        result.Outcome.Should().Be(TenantSwitchOutcome.Switched);
        services.GetRequiredService<ScriptedJavaScript>().RememberedTenants.Should().BeEmpty();
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal(AuthenticatedHomeDestination);
    }

    [Fact]
    public async Task SwitchTenantAsync_WhenAnOldTenantResponseArrivesAfterTheSwitch_ShouldDiscardItAndKeepTheDestination()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        var delayedRead = network.Hold(CurrentUserPath);
        var oldTenantRead = services.GetRequiredService<UsersClient>().GetCurrentUserAsync(CancellationToken.None);
        network.Respond(SwitchTenantPath, () => new HttpResponseMessage(HttpStatusCode.OK));
        await services.GetRequiredService<SessionTransition>().SwitchTenantAsync(OtherTenantId);

        // Act
        var lateResponse = CreateUnauthorizedResponse("Revoked");
        lateResponse.Headers.Add(AccountApiHeaders.UserFeatureFlags, "beta-features");
        delayedRead.SetResult(lateResponse);
        var result = await oldTenantRead;
        var readAfterSwitch = await services.GetRequiredService<UsersClient>().GetCurrentUserAsync(CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ApiCallOutcome.TransportFailure);
        readAfterSwitch.Outcome.Should().Be(ApiCallOutcome.TransportFailure);
        services.GetRequiredService<FeatureFlagState>().UserId.Should().BeNull();
        services.GetRequiredService<ToastService>().Toasts.Should().BeEmpty();
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal(AuthenticatedHomeDestination);
        network.RequestsAfterSignIn.Should().Equal($"GET {CurrentUserPath}", $"POST {SwitchTenantPath}");
    }

    [Fact]
    public async Task SwitchTenantAsync_WhenRejected_ShouldReturnTheFailureAndKeepTheSurface()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(SwitchTenantPath, () => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var transition = services.GetRequiredService<SessionTransition>();

        // Act
        var result = await transition.SwitchTenantAsync(OtherTenantId);

        // Assert
        result.Outcome.Should().Be(TenantSwitchOutcome.Failed);
        result.Failure!.Problem!.StatusCode.Should().Be(403);
        transition.Status.Should().Be(SessionTransitionStatus.Idle);
        services.GetRequiredService<AuthenticationNavigator>().IsLeaving.Should().BeFalse();
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().BeEmpty();
        network.RequestsAfterSignIn.Should().Equal($"POST {SwitchTenantPath}");
    }

    [Fact]
    public async Task SwitchTenantAsync_WhenResponseIsLostAndTheBrowserIsOnAnotherTenant_ShouldLeaveForHome()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(SwitchTenantPath, () => throw new HttpRequestException("The connection was closed after the request was sent."));
        network.Respond(BootstrapPath, () => CreateBootstrapResponse(OtherTenantId));

        // Act
        var result = await services.GetRequiredService<SessionTransition>().SwitchTenantAsync(OtherTenantId);

        // Assert
        result.Outcome.Should().Be(TenantSwitchOutcome.Switched);
        services.GetRequiredService<ScriptedJavaScript>().RememberedTenants.Should().Equal("2");
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().Equal(AuthenticatedHomeDestination);
        network.RequestsAfterSignIn.Should().Equal($"POST {SwitchTenantPath}", $"GET {BootstrapPath}");
    }

    [Fact]
    public async Task SwitchTenantAsync_WhenResponseIsLostAndTheBrowserIsStillOnTheSameTenant_ShouldReturnTheFailure()
    {
        // Arrange
        var network = new ScriptedNetwork();
        await using var services = await CreateSignedInServicesAsync(network);
        network.Respond(SwitchTenantPath, () => throw new HttpRequestException("The connection was closed after the request was sent."));
        network.Respond(BootstrapPath, () => CreateBootstrapResponse(CurrentTenantId));

        // Act
        var result = await services.GetRequiredService<SessionTransition>().SwitchTenantAsync(OtherTenantId);

        // Assert
        result.Outcome.Should().Be(TenantSwitchOutcome.Failed);
        result.Failure!.Outcome.Should().Be(ApiCallOutcome.TransportFailure);
        services.GetRequiredService<AuthenticationNavigator>().IsLeaving.Should().BeFalse();
        services.GetRequiredService<RecordingNavigationManager>().Navigations.Should().BeEmpty();
        network.RequestsAfterSignIn.Should().Equal($"POST {SwitchTenantPath}", $"GET {BootstrapPath}");
    }

    private static async Task<ServiceProvider> CreateSignedInServicesAsync(ScriptedNetwork network)
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingNavigationManager>();
        services.AddSingleton<NavigationManager>(serviceProvider => serviceProvider.GetRequiredService<RecordingNavigationManager>());
        services.AddSingleton<ScriptedJavaScript>();
        services.AddSingleton<IJSRuntime>(serviceProvider => serviceProvider.GetRequiredService<ScriptedJavaScript>());
        services.AddAccountApiClients(new Uri(RecordingNavigationManager.BaseAddress), () => network);
        services.AddScoped<DataListPageCache>();
        services.AddScoped<ToastService>();
        services.AddScoped<SessionState>();
        services.AddScoped<AuthSyncCoordinator>();
        services.AddScoped<SessionTransition>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = false });

        network.Respond(BootstrapPath, () => CreateBootstrapResponse(CurrentTenantId));
        await provider.GetRequiredService<SessionState>().GetAsync();
        network.MarkSignedIn();
        return provider;
    }

    private static HttpResponseMessage CreateBootstrapResponse(TenantId tenantId)
    {
        var user = new BootstrapUser(new UserId("usr_01JZ8Q4N6V3K2M7P9R5T0W1XYZ"), tenantId, "Owner", "owner@example.com", "Ann", "Lee", null, null, "Acme", null, null, false, []);
        return CreateJsonResponse(new BootstrapResponse(true, user, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool>(), "antiforgery-token"));
    }

    private static HttpResponseMessage CreateAnonymousBootstrapResponse()
    {
        return CreateJsonResponse(new BootstrapResponse(false, null, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool>(), "antiforgery-token"));
    }

    private static HttpResponseMessage CreateJsonResponse(BootstrapResponse bootstrap)
    {
        var json = JsonSerializer.Serialize(bootstrap, ApiJsonSerializerOptions.Create());
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static HttpResponseMessage CreateUnauthorizedResponse(string unauthorizedReason)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        response.Headers.Add(AccountApiHeaders.UnauthorizedReason, unauthorizedReason);
        return response;
    }

    // Answers each path with its latest script, or holds a response until the test releases it
    private sealed class ScriptedNetwork : HttpMessageHandler
    {
        private readonly List<string> _requests = [];
        private readonly Dictionary<string, Func<Task<HttpResponseMessage>>> _responses = [];
        private int _signedInAt;

        public IReadOnlyList<string> RequestsAfterSignIn => _requests[_signedInAt..];

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

        public void MarkSignedIn()
        {
            _signedInAt = _requests.Count;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            _requests.Add($"{request.Method} {path}");
            return _responses[path]();
        }
    }

    // Stands in for the browser's module loader and preferred-tenant.js
    private sealed class ScriptedJavaScript : IJSRuntime, IJSObjectReference
    {
        public JSException? ImportFailure { get; set; }

        public Action? OnRemember { get; set; }

        public List<string> RememberedTenants { get; } = [];

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            try
            {
                return ValueTask.FromResult(Run<TValue>(identifier, args));
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
                return ValueTask.FromResult(Run<TValue>(identifier, args));
            }
            catch (JSException exception)
            {
                return ValueTask.FromException<TValue>(exception);
            }
        }

        public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, object?[]? args)
        {
            throw new NotSupportedException("No constructor is scripted.");
        }

        public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            throw new NotSupportedException("No constructor is scripted.");
        }

        public ValueTask<TValue> GetValueAsync<TValue>(string identifier)
        {
            throw new NotSupportedException("No value is scripted.");
        }

        public ValueTask<TValue> GetValueAsync<TValue>(string identifier, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("No value is scripted.");
        }

        public ValueTask SetValueAsync<TValue>(string identifier, TValue value)
        {
            throw new NotSupportedException("No value is scripted.");
        }

        public ValueTask SetValueAsync<TValue>(string identifier, TValue value, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("No value is scripted.");
        }

        // A module load that fails is reported as a faulted call, the way the browser rejects the import promise
        private TValue Run<TValue>(string identifier, object?[]? args)
        {
            switch (identifier)
            {
                case "import" when ImportFailure is not null:
                    throw ImportFailure;
                case "import":
                    return (TValue)(object)this;
                case "remember":
                    OnRemember?.Invoke();
                    RememberedTenants.Add((string)args![1]!);
                    return default!;
                default:
                    throw new NotSupportedException($"No script for '{identifier}'.");
            }
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
            Navigations.Add(options.ForceLoad ? uri : throw new InvalidOperationException("A session transition always loads a new document."));
        }
    }
}
