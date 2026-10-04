using Microsoft.JSInterop;

namespace Blazor.Tests.Client.Components;

// Controls an IJSRuntime (Runtime) whose calls a test can hold open. A held identifier stays pending until the test releases
// its gate, so the test can tear its subject down between the import and the attach, or while the attach is pending. Every
// object reference the runtime hands out records the calls made on it and how often it was released. The controller itself is
// not a runtime, so a test drives it without making interop calls.
public sealed class ControlledJavaScript
{
    private readonly Dictionary<string, int> _disposeCounts = [];
    private readonly Dictionary<string, Exception> _failures = [];
    private readonly Dictionary<string, TaskCompletionSource> _gates = [];
    private readonly Dictionary<string, object?> _results = [];

    public ControlledJavaScript()
    {
        Runtime = new ControlledRuntime(this);
    }

    public IJSRuntime Runtime { get; }

    // Every call in order, prefixed by the reference it was made on ("import", "import:attach", "import:attach:dispose"), and
    // every release of a reference ("import.DisposeAsync")
    public List<string> Calls { get; } = [];

    // The arguments of the latest call to each identifier
    public Dictionary<string, object?[]?> LastArguments { get; } = [];

    // Called synchronously as each call starts, before its gate or result
    public Action<string>? OnCall { get; set; }

    public TaskCompletionSource Hold(string identifier)
    {
        var gate = new TaskCompletionSource();
        _gates[identifier] = gate;
        return gate;
    }

    public void Fail(string identifier, Exception exception)
    {
        _failures[identifier] = exception;
    }

    public void Return(string identifier, object? result)
    {
        _results[identifier] = result;
    }

    // How often the reference the call at this path returned was released
    public int DisposeCount(string path)
    {
        return _disposeCounts.GetValueOrDefault(path);
    }

    // How often the module's own dispose was called on the handle at this path, which removes its listeners
    public int DisposeCalls(string path)
    {
        return Calls.Count(call => call == $"{path}:dispose");
    }

    private async Task<TValue> InvokeAsync<TValue>(string path, string identifier, object?[]? args)
    {
        Calls.Add(path);
        OnCall?.Invoke(path);
        LastArguments[identifier] = args;
        if (_gates.Remove(identifier, out var gate)) await gate.Task;
        if (_failures.Remove(identifier, out var failure)) throw failure;
        if (_results.TryGetValue(identifier, out var result)) return (TValue)result!;
        if (typeof(TValue) != typeof(IJSObjectReference)) return default!;

        _disposeCounts[path] = 0;
        return (TValue)(object)new ControlledObjectReference(this, path);
    }

    private void Release(string path)
    {
        _disposeCounts[path]++;
        Calls.Add($"{path}.DisposeAsync");
    }

    private sealed class ControlledRuntime(ControlledJavaScript controller) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return new ValueTask<TValue>(controller.InvokeAsync<TValue>(identifier, identifier, args));
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return new ValueTask<TValue>(controller.InvokeAsync<TValue>(identifier, identifier, args));
        }
    }

    private sealed class ControlledObjectReference(ControlledJavaScript controller, string path) : IJSObjectReference
    {
        public ValueTask DisposeAsync()
        {
            controller.Release(path);
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return new ValueTask<TValue>(controller.InvokeAsync<TValue>($"{path}:{identifier}", identifier, args));
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return new ValueTask<TValue>(controller.InvokeAsync<TValue>($"{path}:{identifier}", identifier, args));
        }
    }
}
