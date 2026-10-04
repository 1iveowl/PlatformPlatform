using Blazor.Client.Shell;
using Blazor.Tests.Client.Components;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazor.Tests.Client.Shell;

public sealed class ShellInteropTests
{
    private static readonly ShellBrowserState DefaultBrowserState = new(true, false, null);

    [Fact]
    public async Task AttachShellAsync_WhenTornDownDuringTheImport_ShouldReleaseTheModuleOnceAndAttachNothing()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var import = javaScript.Hold("import");
        var interop = new ShellInterop(javaScript.Runtime);
        using var dotNet = DotNetObjectReference.Create(new object());
        var attach = interop.AttachShellAsync(dotNet).AsTask();

        // Act
        var dispose = interop.DisposeAsync().AsTask();
        import.SetResult();
        var state = await attach;
        await dispose;

        // Assert
        state.Should().Be(DefaultBrowserState);
        javaScript.Calls.Should().Equal("import", "import.DisposeAsync");
    }

    [Fact]
    public async Task AttachShellAsync_WhenTornDownWhileTheAttachIsPending_ShouldReleaseTheLateHandleAndModuleOnce()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var pendingAttach = javaScript.Hold("attachShell");
        var interop = new ShellInterop(javaScript.Runtime);
        using var dotNet = DotNetObjectReference.Create(new object());
        var attach = interop.AttachShellAsync(dotNet).AsTask();

        // Act
        var dispose = interop.DisposeAsync().AsTask();
        var disposedBeforeAttach = dispose.IsCompleted;
        pendingAttach.SetResult();
        var state = await attach;
        await dispose;
        await interop.DisposeAsync();

        // Assert
        disposedBeforeAttach.Should().BeFalse();
        state.Should().Be(DefaultBrowserState);
        javaScript.Calls.Should().Equal("import", "import:attachShell", "import:attachShell:dispose", "import:attachShell.DisposeAsync", "import.DisposeAsync");
    }

    [Fact]
    public async Task ReadInstallPromptEnvironmentAsync_WhenAnAttachImportsAtTheSameTime_ShouldImportTheModuleOnce()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var import = javaScript.Hold("import");
        javaScript.Return("readState", new ShellBrowserState(false, true, "true"));
        javaScript.Return("readInstallPromptEnvironment", new InstallPromptEnvironment(null, 0, true, null, false));
        var interop = new ShellInterop(javaScript.Runtime);
        using var dotNet = DotNetObjectReference.Create(new object());

        // Act
        var environment = interop.ReadInstallPromptEnvironmentAsync().AsTask();
        var attach = interop.AttachShellAsync(dotNet).AsTask();
        import.SetResult();
        await Task.WhenAll(environment, attach);
        await interop.DisposeAsync();

        // Assert
        (await environment).IsStandalone.Should().BeTrue();
        (await attach).Should().Be(new ShellBrowserState(false, true, "true"));
        javaScript.Calls.Count(call => call == "import").Should().Be(1);
        javaScript.DisposeCount("import").Should().Be(1);
        javaScript.DisposeCalls("import:attachShell").Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenSeveralHandlesAreAttached_ShouldReleaseEachAndTheModuleOnce()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var interop = new ShellInterop(javaScript.Runtime);
        using var dotNet = DotNetObjectReference.Create(new object());
        await interop.AttachShellAsync(dotNet);
        await interop.AttachInstallPromptSwipeAsync(new ElementReference("install-prompt"), dotNet);

        // Act
        await interop.DisposeAsync();
        await interop.DisposeAsync();

        // Assert
        javaScript.DisposeCalls("import:attachShell").Should().Be(1);
        javaScript.DisposeCount("import:attachShell").Should().Be(1);
        javaScript.DisposeCalls("import:attachInstallPromptSwipe").Should().Be(1);
        javaScript.DisposeCount("import:attachInstallPromptSwipe").Should().Be(1);
        javaScript.DisposeCount("import").Should().Be(1);
    }

    [Fact]
    public async Task FocusAsync_WhenCalledAfterTeardown_ShouldCallNothing()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var interop = new ShellInterop(javaScript.Runtime);
        using var dotNet = DotNetObjectReference.Create(new object());
        await interop.DisposeAsync();

        // Act
        await interop.FocusAsync("user-menu");
        await interop.StoreInstallPromptDismissalAsync(null);
        await interop.StoreCollapsedAsync(true);
        await interop.AttachInstallPromptSwipeAsync(new ElementReference("install-prompt"), dotNet);
        var state = await interop.AttachShellAsync(dotNet);

        // Assert
        state.Should().Be(DefaultBrowserState);
        javaScript.Calls.Should().BeEmpty();
    }
}
