using Blazor.Client.Components.Images;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazor.Tests.Client.Components;

public sealed class FilePickerInteropTests
{
    [Fact]
    public async Task DisposeAsync_WhenTheAttachIsPending_ShouldReleaseTheLateHandleAndModuleOnce()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var pendingAttach = javaScript.Hold("attach");
        var interop = new FilePickerInterop(javaScript.Runtime);
        using var dotNet = DotNetObjectReference.Create(new object());
        var attach = interop.AttachAsync(new ElementReference("drop-zone"), new ElementReference("input"), dotNet).AsTask();

        // Act
        var dispose = interop.DisposeAsync().AsTask();
        var disposedBeforeAttach = dispose.IsCompleted;
        pendingAttach.SetResult();
        await attach;
        await dispose;
        await interop.DisposeAsync();
        await interop.OpenAsync();

        // Assert
        disposedBeforeAttach.Should().BeFalse();
        javaScript.Calls.Should().Equal("import", "import:attach", "import:attach:dispose", "import:attach.DisposeAsync", "import.DisposeAsync");
    }

    [Fact]
    public async Task AttachAsync_WhenTheImportCompletesAfterTeardown_ShouldReleaseTheModuleOnceAndAttachNothing()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var import = javaScript.Hold("import");
        var interop = new FilePickerInterop(javaScript.Runtime);
        using var dotNet = DotNetObjectReference.Create(new object());
        var attach = interop.AttachAsync(new ElementReference("drop-zone"), new ElementReference("input"), dotNet).AsTask();

        // Act
        var dispose = interop.DisposeAsync().AsTask();
        import.SetResult();
        await attach;
        await dispose;

        // Assert
        javaScript.Calls.Should().Equal("import", "import.DisposeAsync");
    }
}
