using Account.Features.Authentication.Queries;
using Blazor.Client.Bootstrap;
using Blazor.Client.Components.Lists;
using Blazor.Client.Forms;
using Blazor.Client.Session;
using FluentAssertions;
using SharedKernel.Domain;

namespace Blazor.Tests.Client.Session;

public sealed class SessionStateTests
{
    [Fact]
    public async Task RefreshAsync_WhenAnOlderReadCompletesAfterANewerOne_ShouldKeepTheNewerBootstrap()
    {
        // Arrange
        var source = new ControlledBootstrapSource();
        using var session = CreateSession(source, out _);
        var staleRead = session.RefreshAsync();
        var freshRead = session.RefreshAsync();

        // Act
        source.Complete(1, CreateBootstrap("Ann"));
        await freshRead;
        source.Complete(0, CreateBootstrap("Old"));
        var staleResult = await staleRead;

        // Assert
        session.Current!.User!.FirstName.Should().Be("Ann");
        staleResult.User!.FirstName.Should().Be("Ann");
    }

    [Fact]
    public async Task RefreshAsync_WhenReadCompletes_ShouldRaiseChanged()
    {
        // Arrange
        var source = new ControlledBootstrapSource();
        using var session = CreateSession(source, out _);
        var changes = 0;
        session.Changed += () => changes++;
        var read = session.RefreshAsync();

        // Act
        source.Complete(0, CreateBootstrap("Ann"));
        await read;

        // Assert
        changes.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_WhenCalledTwiceBeforeTheReadCompletes_ShouldReadTheBootstrapOnce()
    {
        // Arrange
        var source = new ControlledBootstrapSource();
        using var session = CreateSession(source, out _);

        // Act
        var first = session.GetAsync();
        var second = session.GetAsync();
        source.Complete(0, CreateBootstrap("Ann"));

        // Assert
        (await first).Should().BeSameAs(await second);
        source.ReadCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_WhenTheSharedReadFails_ShouldFailEveryCallerAndReadAgainOnTheNextCall()
    {
        // Arrange
        var source = new ControlledBootstrapSource();
        using var session = CreateSession(source, out _);
        var first = session.GetAsync();
        var second = session.GetAsync();
        source.Fail(0, new InvalidOperationException("The bootstrap endpoint call ended with TransportFailure (status none)."));
        var firstAct = () => first;
        await firstAct.Should().ThrowAsync<InvalidOperationException>();
        var secondAct = () => second;
        await secondAct.Should().ThrowAsync<InvalidOperationException>();

        // Act
        var retry = session.GetAsync();
        source.Complete(1, CreateBootstrap("Ann"));

        // Assert
        (await retry).User!.FirstName.Should().Be("Ann");
        session.Current!.User!.FirstName.Should().Be("Ann");
        source.ReadCount.Should().Be(2);
    }

    [Fact]
    public async Task Leaving_WhenAReadCompletesAfterTheSurfaceIsLeft_ShouldCancelTheReadWithoutRestoringTheIdentity()
    {
        // Arrange
        var source = new ControlledBootstrapSource();
        using var session = CreateSession(source, out var navigator);
        var changes = 0;
        session.Changed += () => changes++;
        var read = session.RefreshAsync();
        navigator.LeaveForLoggedOut();

        // Act
        source.Complete(0, CreateBootstrap("Ann"));

        // Assert
        var readAct = () => read;
        await readAct.Should().ThrowAsync<OperationCanceledException>();
        session.Current.Should().BeNull();
        changes.Should().Be(0);
    }

    [Fact]
    public async Task Leaving_WhenTheSurfaceIsLeft_ShouldCancelRequestsAndClearTheIdentityState()
    {
        // Arrange
        var source = new ControlledBootstrapSource();
        using var session = CreateSession(source, out var navigator);
        var read = session.RefreshAsync();
        source.Complete(0, CreateBootstrap("Ann"));
        await read;

        // Act
        navigator.LeaveForLoggedOut();

        // Assert
        session.RequestsAborted.IsCancellationRequested.Should().BeTrue();
        session.Current.Should().BeNull();
    }

    private static SessionState CreateSession(IBootstrapSource source, out AuthenticationNavigator navigator)
    {
        navigator = new AuthenticationNavigator(new TestNavigationManager());
        return new SessionState(source, navigator, new DataListPageCache(), new ToastService());
    }

    private static BootstrapResponse CreateBootstrap(string firstName)
    {
        var user = new BootstrapUser(new UserId("usr_01KC0CURRENT0000000000000A"), new TenantId(1), "Owner", "ann@example.com", firstName, "Lee", null, null, "Acme", null, null, false, []);
        return new BootstrapResponse(true, user, "en-US", new Dictionary<string, string>(), new Dictionary<string, bool>(), "token");
    }

    private sealed class ControlledBootstrapSource : IBootstrapSource
    {
        private readonly List<TaskCompletionSource<BootstrapResponse>> _reads = [];

        public int ReadCount => _reads.Count;

        public Task<BootstrapResponse> GetAsync(CancellationToken cancellationToken = default)
        {
            var read = new TaskCompletionSource<BootstrapResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _reads.Add(read);
            return read.Task;
        }

        public Action Apply(BootstrapResponse? bootstrap)
        {
            return static () => { };
        }

        public void Complete(int index, BootstrapResponse bootstrap)
        {
            _reads[index].SetResult(bootstrap);
        }

        public void Fail(int index, Exception exception)
        {
            _reads[index].SetException(exception);
        }
    }
}
