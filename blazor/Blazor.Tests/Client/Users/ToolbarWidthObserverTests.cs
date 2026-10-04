using Blazor.Client.Users;
using Blazor.Tests.Client.Components;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazor.Tests.Client.Users;

// The users toolbar's width observer owns the module, the handle and the .NET reference across the import and the attach, so a
// users page torn down at either await leaves no ResizeObserver connected and is never told a width afterwards
public sealed class ToolbarWidthObserverTests
{
    private const string Observe = "import:observeToolbarWidth";

    [Fact]
    public async Task ObserveAsync_WhenAttached_ShouldPassTheToolbarTheReferenceAndTheThreshold()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var observer = new ToolbarWidthObserver(javaScript.Runtime, _ => { });
        var toolbar = new ElementReference("toolbar");

        // Act
        await observer.ObserveAsync(toolbar, UsersFilterModel.InlineThresholdRem);

        // Assert
        javaScript.Calls.Should().Equal("import", Observe);
        var arguments = javaScript.LastArguments["observeToolbarWidth"]!;
        arguments[0].Should().Be(toolbar);
        arguments[1].Should().BeOfType<DotNetObjectReference<ToolbarWidthObserver>>().Which.Value.Should().BeSameAs(observer);
        arguments[2].Should().Be(UsersFilterModel.InlineThresholdRem);
    }

    [Fact]
    public async Task DisposeAsync_WhenObserving_ShouldDisconnectTheObserverAndReleaseEverythingOnceInOrder()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var observer = new ToolbarWidthObserver(javaScript.Runtime, _ => { });
        await observer.ObserveAsync(new ElementReference("toolbar"), UsersFilterModel.InlineThresholdRem);
        var reference = ObservedReference(javaScript);

        // Act
        await observer.DisposeAsync();
        await observer.DisposeAsync();

        // Assert
        javaScript.Calls.Should().Equal("import", Observe, $"{Observe}:dispose", $"{Observe}.DisposeAsync", "import.DisposeAsync");
        javaScript.DisposeCount(Observe).Should().Be(1);
        javaScript.DisposeCount("import").Should().Be(1);
        IsDisposed(reference).Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_WhenTheImportCompletesAfterTeardown_ShouldNeverObserveAndReleaseTheModuleOnce()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var import = javaScript.Hold("import");
        var observer = new ToolbarWidthObserver(javaScript.Runtime, _ => { });
        var observe = observer.ObserveAsync(new ElementReference("toolbar"), UsersFilterModel.InlineThresholdRem);

        // Act
        var dispose = observer.DisposeAsync().AsTask();
        import.SetResult();
        await observe;
        await dispose;

        // Assert
        javaScript.Calls.Should().Equal("import", "import.DisposeAsync");
        javaScript.LastArguments.Should().NotContainKey("observeToolbarWidth");
        javaScript.DisposeCount("import").Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenTheObserveIsPending_ShouldDisconnectTheLateObserverWhileTheReferenceIsAliveAndReleaseEverythingOnce()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var pendingObserve = javaScript.Hold("observeToolbarWidth");
        var observer = new ToolbarWidthObserver(javaScript.Runtime, _ => { });
        var observe = observer.ObserveAsync(new ElementReference("toolbar"), UsersFilterModel.InlineThresholdRem);
        var reference = ObservedReference(javaScript);
        var referenceAliveAtDisconnect = false;
        javaScript.OnCall = call =>
        {
            if (call == $"{Observe}:dispose") referenceAliveAtDisconnect = !IsDisposed(reference);
        };

        // Act
        var dispose = observer.DisposeAsync().AsTask();
        var referenceDisposedUnderTheObserve = IsDisposed(reference);
        pendingObserve.SetResult();
        await observe;
        await dispose;

        // Assert
        referenceDisposedUnderTheObserve.Should().BeFalse();
        javaScript.Calls.Should().Equal("import", Observe, $"{Observe}:dispose", $"{Observe}.DisposeAsync", "import.DisposeAsync");
        javaScript.DisposeCalls(Observe).Should().Be(1);
        javaScript.DisposeCount(Observe).Should().Be(1);
        javaScript.DisposeCount("import").Should().Be(1);
        referenceAliveAtDisconnect.Should().BeTrue();
        IsDisposed(reference).Should().BeTrue();
    }

    [Fact]
    public async Task OnToolbarWidthChanged_WhenObserving_ShouldReportTheWidth()
    {
        // Arrange
        var reported = new List<bool>();
        var observer = new ToolbarWidthObserver(new ControlledJavaScript().Runtime, reported.Add);

        // Act
        await observer.OnToolbarWidthChanged(true);
        await observer.OnToolbarWidthChanged(false);

        // Assert
        reported.Should().Equal(true, false);
    }

    [Fact]
    public async Task OnToolbarWidthChanged_WhenTheOwnerIsTornDown_ShouldReportNothing()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var pendingObserve = javaScript.Hold("observeToolbarWidth");
        var reported = new List<bool>();
        var observer = new ToolbarWidthObserver(javaScript.Runtime, reported.Add);
        var observe = observer.ObserveAsync(new ElementReference("toolbar"), UsersFilterModel.InlineThresholdRem);
        var dispose = observer.DisposeAsync().AsTask();

        // Act
        await observer.OnToolbarWidthChanged(true);
        pendingObserve.SetResult();
        await observe;
        await dispose;
        await observer.OnToolbarWidthChanged(false);

        // Assert
        reported.Should().BeEmpty();
    }

    [Fact]
    public async Task ObserveAsync_WhenTheObserveFails_ShouldReleaseTheModuleAndReferenceAndLetTheFailureThrough()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        javaScript.Fail("observeToolbarWidth", new JSException("The observe failed."));
        var observer = new ToolbarWidthObserver(javaScript.Runtime, _ => { });

        // Act
        var act = () => observer.ObserveAsync(new ElementReference("toolbar"), UsersFilterModel.InlineThresholdRem);

        // Assert
        await act.Should().ThrowAsync<JSException>();
        IsDisposed(ObservedReference(javaScript)).Should().BeTrue();
        javaScript.DisposeCount("import").Should().Be(1);
        await observer.DisposeAsync();
        javaScript.DisposeCount("import").Should().Be(1);
    }

    private static DotNetObjectReference<ToolbarWidthObserver> ObservedReference(ControlledJavaScript javaScript)
    {
        return (DotNetObjectReference<ToolbarWidthObserver>)javaScript.LastArguments["observeToolbarWidth"]![1]!;
    }

    private static bool IsDisposed(DotNetObjectReference<ToolbarWidthObserver> reference)
    {
        try
        {
            _ = reference.Value;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}
