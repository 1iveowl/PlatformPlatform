using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SharedKernel.Tests.Configuration;

// Composes a worker host through its real entry point, so the service graph under test is the one Program.cs registers and
// never a copied list. Service provider validation (build and scope) is forced on whatever the environment, and the entry
// point is stopped the moment the host is built: before it applies migrations, runs data migrations or reconciles feature flags,
// and before any connection to a database, blob storage or telemetry endpoint is opened. The hosted services are then resolved
// the way starting the host resolves them, which also covers factory registrations that build validation cannot inspect. The
// only replacement is the server: an unbound one stands in for Kestrel, whose listening port is infrastructure.
public static class WorkerHostComposition
{
    private const string HostingDiagnosticListenerName = "Microsoft.Extensions.Hosting";

    private static readonly AsyncLocal<CompositionRun?> CurrentRun = new();

    public static void Validate(string workerAssemblyName, IReadOnlyDictionary<string, string> configuration, Action<IServiceCollection>? changeServices = null)
    {
        var entryPoint = Assembly.Load(workerAssemblyName).EntryPoint
                         ?? throw new InvalidOperationException($"The assembly '{workerAssemblyName}' has no entry point.");
        var args = configuration.Select(setting => $"--{setting.Key}={setting.Value}").ToArray();

        var run = new CompositionRun(changeServices);
        CurrentRun.Value = run;
        using var subscription = DiagnosticListener.AllListeners.Subscribe(run);
        try
        {
            InvokeEntryPoint(entryPoint, args);
        }
        catch (HostBuiltSignal)
        {
            // The host is built and the entry point is stopped before it could reach any external system
        }
        catch (Exception exception)
        {
            throw new WorkerHostCompositionException($"The worker host '{workerAssemblyName}' failed to build: {Flatten(exception)}", exception);
        }
        finally
        {
            CurrentRun.Value = null;
            run.Dispose();
        }

        if (run.Host is null)
        {
            throw new WorkerHostCompositionException($"The entry point of '{workerAssemblyName}' finished without building a host.", null);
        }

        using var host = run.Host;
        try
        {
            _ = host.Services.GetServices<IHostedService>().ToArray();
        }
        catch (Exception exception)
        {
            throw new WorkerHostCompositionException($"The worker host '{workerAssemblyName}' cannot resolve its hosted services: {Flatten(exception)}", exception);
        }
    }

    private static void InvokeEntryPoint(MethodInfo entryPoint, string[] args)
    {
        try
        {
            var result = entryPoint.Invoke(null, entryPoint.GetParameters().Length == 0 ? null : [args]);
            if (result is Task task) task.GetAwaiter().GetResult();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static string Flatten(Exception exception)
    {
        return exception is AggregateException aggregate
            ? string.Join(" | ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
            : exception.InnerException is null
                ? exception.Message
                : $"{exception.Message} ({Flatten(exception.InnerException)})";
    }

    private sealed class CompositionRun(Action<IServiceCollection>? changeServices)
        : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _listenerSubscriptions = [];

        public IHost? Host { get; private set; }

        public void Dispose()
        {
            foreach (var subscription in _listenerSubscriptions) subscription.Dispose();
        }

        void IObserver<DiagnosticListener>.OnNext(DiagnosticListener listener)
        {
            if (listener.Name == HostingDiagnosticListenerName) _listenerSubscriptions.Add(listener.Subscribe(this));
        }

        void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> hostingEvent)
        {
            // The listener is process wide, so hosts that other tests build in parallel are ignored
            if (CurrentRun.Value != this) return;

            if (hostingEvent is { Key: "HostBuilding", Value: IHostBuilder hostBuilder })
            {
                hostBuilder.UseDefaultServiceProvider(options =>
                    {
                        options.ValidateOnBuild = true;
                        options.ValidateScopes = true;
                    }
                );
                hostBuilder.ConfigureServices(services =>
                    {
                        services.Replace(ServiceDescriptor.Singleton<IServer, UnboundServer>());
                        changeServices?.Invoke(services);
                    }
                );
            }
            else if (hostingEvent is { Key: "HostBuilt", Value: IHost host })
            {
                Host = host;
                throw new HostBuiltSignal();
            }
        }

        void IObserver<DiagnosticListener>.OnCompleted()
        {
        }

        void IObserver<DiagnosticListener>.OnError(Exception error)
        {
        }

        void IObserver<KeyValuePair<string, object?>>.OnCompleted()
        {
        }

        void IObserver<KeyValuePair<string, object?>>.OnError(Exception error)
        {
        }
    }

    private sealed class UnboundServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull
        {
            throw new InvalidOperationException("The composition host is never started.");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class HostBuiltSignal() : Exception("The host is built; the entry point stops here.");
}

public sealed class WorkerHostCompositionException(string message, Exception? innerException) : Exception(message, innerException);
