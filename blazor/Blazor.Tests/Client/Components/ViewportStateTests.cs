using Blazor.Client.Components;
using FluentAssertions;
using Microsoft.JSInterop;

namespace Blazor.Tests.Client.Components;

public sealed class ViewportStateTests
{
    [Fact]
    public void Matches_WhenNothingHasBeenReported_ShouldBeTheWidestViewport()
    {
        // Arrange
        var state = new ViewportState(new RecordingJavaScript());

        // Assert
        state.Matches.Should().Be(ViewportMatches.Widest);
        foreach (var breakpoint in Breakpoints.All)
        {
            state.Reaches(breakpoint).Should().BeTrue();
        }
    }

    [Fact]
    public void Apply_WhenTheWidthChanges_ShouldRaiseChangedOnceAndNotAgainForTheSameWidth()
    {
        // Arrange
        var state = new ViewportState(new RecordingJavaScript());
        var changes = 0;
        state.Changed += () => changes++;
        var phone = new ViewportMatches(false, false, false, false, false);

        // Act
        var first = state.Apply(phone);
        var second = state.Apply(phone);

        // Assert
        first.Should().BeTrue();
        second.Should().BeFalse();
        changes.Should().Be(1);
        state.Reaches(Breakpoint.Small).Should().BeFalse();
    }

    [Theory]
    [InlineData(Breakpoint.Small, true)]
    [InlineData(Breakpoint.Medium, true)]
    [InlineData(Breakpoint.Large, false)]
    [InlineData(Breakpoint.ExtraLarge, false)]
    [InlineData(Breakpoint.ExtraExtraLarge, false)]
    public void Reaches_WhenATabletWidthIsReported_ShouldAnswerPerBreakpoint(Breakpoint breakpoint, bool expected)
    {
        // Arrange
        var state = new ViewportState(new RecordingJavaScript());

        // Act
        state.Apply(new ViewportMatches(true, true, false, false, false));

        // Assert
        state.Reaches(breakpoint).Should().Be(expected);
    }

    [Fact]
    public async Task OnViewportChanged_WhenTheModuleReportsAWidth_ShouldApplyItAndNotify()
    {
        // Arrange
        var state = new ViewportState(new RecordingJavaScript());
        var changes = 0;
        state.Changed += () => changes++;

        // Act
        await state.OnViewportChanged(new ViewportMatches(true, false, false, false, false));

        // Assert
        changes.Should().Be(1);
        state.Matches.Should().Be(new ViewportMatches(true, false, false, false, false));
    }

    [Fact]
    public async Task AttachAsync_WhenSeveralComponentsAskForTheWidth_ShouldAttachTheModuleOnlyOnce()
    {
        // Arrange
        var javaScript = new RecordingJavaScript();
        var state = new ViewportState(javaScript);

        // Act
        await state.AttachAsync();
        await state.AttachAsync();
        await state.AttachAsync();

        // Assert
        javaScript.Calls.Should().Equal("import", "attachViewport", "read");
        state.Matches.Should().Be(new ViewportMatches(true, true, true, false, false));
    }

    [Fact]
    public async Task DisposeAsync_WhenTheContainerIsDisposed_ShouldDisposeTheModulesListeners()
    {
        // Arrange
        var javaScript = new RecordingJavaScript();
        var state = new ViewportState(javaScript);
        await state.AttachAsync();

        // Act
        await state.DisposeAsync();

        // Assert
        javaScript.Calls.Should().Equal("import", "attachViewport", "read", "dispose");
    }

    [Fact]
    public async Task DisposeAsync_WhenTheAttachIsPending_ShouldReleaseTheLateHandleOnceAndNeverReadTheWidth()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var pendingAttach = javaScript.Hold("attachViewport");
        var state = new ViewportState(javaScript.Runtime);
        var changes = 0;
        state.Changed += () => changes++;
        var attach = state.AttachAsync();

        // Act
        var dispose = state.DisposeAsync().AsTask();
        pendingAttach.SetResult();
        await attach;
        await dispose;

        // Assert
        javaScript.Calls.Should().Equal(
            "import", "import:attachViewport", "import:attachViewport:dispose", "import:attachViewport.DisposeAsync", "import.DisposeAsync"
        );
        changes.Should().Be(0);
        state.Matches.Should().Be(ViewportMatches.Widest);
    }

    [Fact]
    public async Task OnViewportChanged_WhenTheContainerIsDisposed_ShouldNotNotify()
    {
        // Arrange
        var state = new ViewportState(new ControlledJavaScript().Runtime);
        var changes = 0;
        state.Changed += () => changes++;
        await state.DisposeAsync();

        // Act
        await state.OnViewportChanged(new ViewportMatches(true, false, false, false, false));

        // Assert
        changes.Should().Be(0);
        state.Matches.Should().Be(ViewportMatches.Widest);
    }

    private sealed class RecordingJavaScript : IJSRuntime, IJSObjectReference
    {
        public List<string> Calls { get; } = [];

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            try
            {
                return ValueTask.FromResult(Record<TValue>(identifier));
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
                return ValueTask.FromResult(Record<TValue>(identifier));
            }
            catch (JSException exception)
            {
                return ValueTask.FromException<TValue>(exception);
            }
        }

        private TValue Record<TValue>(string identifier)
        {
            Calls.Add(identifier);
            if (identifier == "read") return (TValue)(object)new ViewportMatches(true, true, true, false, false);

            return this is TValue handle ? handle : default!;
        }
    }
}
