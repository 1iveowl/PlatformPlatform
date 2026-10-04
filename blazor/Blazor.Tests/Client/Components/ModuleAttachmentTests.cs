using Blazor.Client.Components;
using FluentAssertions;
using Microsoft.JSInterop;

namespace Blazor.Tests.Client.Components;

public sealed class ModuleAttachmentTests
{
    private const string ModulePath = "./js/test.js";

    [Fact]
    public async Task DisposeAsync_WhenAttached_ShouldReleaseTheHandleModuleAndReferenceOnceInThatOrder()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var attachment = new ModuleAttachment<Owner>(javaScript.Runtime, ModulePath);
        await attachment.AttachAsync(new Owner(), "attach", reference => [reference]);
        await attachment.AttachAsync(new Owner(), "attach", reference => [reference]);
        var reference = AttachedReference(javaScript);
        var referenceAliveAtHandleDispose = false;
        javaScript.OnCall = call =>
        {
            if (call == "import:attach:dispose") referenceAliveAtHandleDispose = !IsDisposed(reference);
        };

        // Act
        await attachment.DisposeAsync();
        await attachment.DisposeAsync();

        // Assert
        javaScript.Calls.Should().Equal("import", "import:attach", "import:attach:dispose", "import:attach.DisposeAsync", "import.DisposeAsync");
        javaScript.DisposeCount("import").Should().Be(1);
        javaScript.DisposeCount("import:attach").Should().Be(1);
        referenceAliveAtHandleDispose.Should().BeTrue();
        IsDisposed(reference).Should().BeTrue();
        attachment.Handle.Should().BeNull();
    }

    [Fact]
    public async Task DisposeAsync_WhenTheImportCompletesAfterTeardown_ShouldReleaseTheModuleOnceAndNeverAttach()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var import = javaScript.Hold("import");
        var attachment = new ModuleAttachment<Owner>(javaScript.Runtime, ModulePath);
        var attach = attachment.AttachAsync(new Owner(), "attach", reference => [reference]);

        // Act
        var dispose = attachment.DisposeAsync().AsTask();
        var disposedBeforeImport = dispose.IsCompleted;
        import.SetResult();
        var handle = await attach;
        await dispose;

        // Assert
        disposedBeforeImport.Should().BeFalse();
        handle.Should().BeNull();
        javaScript.Calls.Should().Equal("import", "import.DisposeAsync");
        javaScript.DisposeCount("import").Should().Be(1);
        javaScript.LastArguments.Should().NotContainKey("attach");
    }

    [Fact]
    public async Task DisposeAsync_WhenTheAttachIsPending_ShouldReleaseTheLateHandleModuleAndReferenceOnce()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var pendingAttach = javaScript.Hold("attach");
        var attachment = new ModuleAttachment<Owner>(javaScript.Runtime, ModulePath);
        var attach = attachment.AttachAsync(new Owner(), "attach", reference => [reference]);
        var reference = AttachedReference(javaScript);
        var referenceAliveAtHandleDispose = false;
        javaScript.OnCall = call =>
        {
            if (call == "import:attach:dispose") referenceAliveAtHandleDispose = !IsDisposed(reference);
        };

        // Act
        var dispose = attachment.DisposeAsync().AsTask();
        var disposedBeforeAttach = dispose.IsCompleted;
        pendingAttach.SetResult();
        var handle = await attach;
        await dispose;

        // Assert
        disposedBeforeAttach.Should().BeFalse();
        handle.Should().BeNull();
        javaScript.Calls.Should().Equal("import", "import:attach", "import:attach:dispose", "import:attach.DisposeAsync", "import.DisposeAsync");
        javaScript.DisposeCalls("import:attach").Should().Be(1);
        javaScript.DisposeCount("import:attach").Should().Be(1);
        javaScript.DisposeCount("import").Should().Be(1);
        referenceAliveAtHandleDispose.Should().BeTrue();
        IsDisposed(reference).Should().BeTrue();
    }

    [Fact]
    public async Task AttachAsync_WhenTheAttachFails_ShouldReleaseTheModuleAndReferenceAndRethrow()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        javaScript.Fail("attach", new JSException("The attach failed."));
        var attachment = new ModuleAttachment<Owner>(javaScript.Runtime, ModulePath);

        // Act
        var act = () => attachment.AttachAsync(new Owner(), "attach", reference => [reference]);

        // Assert
        await act.Should().ThrowAsync<JSException>();
        var reference = AttachedReference(javaScript);
        IsDisposed(reference).Should().BeTrue();
        javaScript.DisposeCount("import").Should().Be(1);
        await attachment.DisposeAsync();
        javaScript.DisposeCount("import").Should().Be(1);
        attachment.Handle.Should().BeNull();
    }

    [Fact]
    public async Task DisposeAsync_WhenAPendingAttachFails_ShouldReleaseWhatItAcquiredOnceAndLeaveTheFailureToTheAttach()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var pendingAttach = javaScript.Hold("attach");
        javaScript.Fail("attach", new JSException("The attach failed."));
        var attachment = new ModuleAttachment<Owner>(javaScript.Runtime, ModulePath);
        var attach = attachment.AttachAsync(new Owner(), "attach", reference => [reference]);
        var reference = AttachedReference(javaScript);

        // Act
        var dispose = attachment.DisposeAsync().AsTask();
        pendingAttach.SetResult();

        // Assert
        await attach.Invoking(task => task).Should().ThrowAsync<JSException>();
        await dispose;
        javaScript.DisposeCount("import").Should().Be(1);
        IsDisposed(reference).Should().BeTrue();
    }

    [Fact]
    public async Task AttachAsync_WhenTheDocumentIsGone_ShouldReturnNullAndReleaseWhatItAcquired()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        javaScript.Fail("attach", new JSDisconnectedException("The document is gone."));
        var attachment = new ModuleAttachment<Owner>(javaScript.Runtime, ModulePath);

        // Act
        var handle = await attachment.AttachAsync(new Owner(), "attach", reference => [reference]);

        // Assert
        handle.Should().BeNull();
        IsDisposed(AttachedReference(javaScript)).Should().BeTrue();
        javaScript.DisposeCount("import").Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenTheHandleRefusesItsDispose_ShouldStillReleaseTheRestAndThrow()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var attachment = new ModuleAttachment<Owner>(javaScript.Runtime, ModulePath);
        await attachment.AttachAsync(new Owner(), "attach", reference => [reference]);
        var reference = AttachedReference(javaScript);
        javaScript.Fail("dispose", new JSException("The dispose failed."));

        // Act
        var act = () => attachment.DisposeAsync().AsTask();

        // Assert
        await act.Should().ThrowAsync<JSException>();
        javaScript.DisposeCount("import:attach").Should().Be(1);
        javaScript.DisposeCount("import").Should().Be(1);
        IsDisposed(reference).Should().BeTrue();
        await attachment.DisposeAsync();
        javaScript.DisposeCount("import:attach").Should().Be(1);
        javaScript.DisposeCount("import").Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenTheDocumentIsGoneAtTeardown_ShouldReleaseEverythingWithoutThrowing()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var attachment = new ModuleAttachment<Owner>(javaScript.Runtime, ModulePath);
        await attachment.AttachAsync(new Owner(), "attach", reference => [reference]);
        var reference = AttachedReference(javaScript);
        javaScript.Fail("dispose", new JSDisconnectedException("The document is gone."));

        // Act
        await attachment.DisposeAsync();

        // Assert
        javaScript.DisposeCount("import:attach").Should().Be(1);
        javaScript.DisposeCount("import").Should().Be(1);
        IsDisposed(reference).Should().BeTrue();
    }

    [Fact]
    public async Task AttachAsync_WhenCalledAfterTeardown_ShouldImportNothing()
    {
        // Arrange
        var javaScript = new ControlledJavaScript();
        var attachment = new ModuleAttachment<Owner>(javaScript.Runtime, ModulePath);
        await attachment.DisposeAsync();

        // Act
        var handle = await attachment.AttachAsync(new Owner(), "attach", reference => [reference]);

        // Assert
        handle.Should().BeNull();
        javaScript.Calls.Should().BeEmpty();
    }

    private static DotNetObjectReference<Owner> AttachedReference(ControlledJavaScript javaScript)
    {
        return (DotNetObjectReference<Owner>)javaScript.LastArguments["attach"]![0]!;
    }

    private static bool IsDisposed(DotNetObjectReference<Owner> reference)
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

    private sealed class Owner;
}
