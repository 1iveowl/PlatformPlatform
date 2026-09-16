using FluentAssertions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Authentication;
using SharedKernel.ExecutionContext;
using SharedKernel.Tests.Configuration;
using Xunit;

namespace Account.Tests.Workers;

// The account worker host registers the same Core handlers as the API host but not the API-only services, and it builds its
// service provider before it migrates the database. These tests compose it through its real entry point with validation
// forced on, so a registration the worker cannot satisfy fails here rather than when the stack starts. Only external
// infrastructure is replaced: the connection strings point at nothing and nothing connects before the entry point is stopped.
public sealed class WorkerHostCompositionTests
{
    private const string WorkerAssemblyName = "Account.Workers";

    private static readonly Dictionary<string, string> UnreachableInfrastructure = new()
    {
        ["environment"] = "Development",
        ["ConnectionStrings:account-database"] = "Host=127.0.0.1;Port=1;Database=account;Username=composition;Password=composition",
        ["ConnectionStrings:blob-storage"] = "UseDevelopmentStorage=true"
    };

    [Fact]
    public void Validate_WhenAccountWorkerHostIsComposed_ShouldBuildWithValidationAndResolveHostedServices()
    {
        // Act
        var act = () => WorkerHostComposition.Validate(WorkerAssemblyName, UnreachableInfrastructure);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WhenHandlerDependencyNeedsApiOnlyAntiforgery_ShouldFail()
    {
        // Arrange: the composition before the host specific issuer, where bootstrap issued tokens through IAntiforgery itself
        void RegisterApiHostIssuer(IServiceCollection services)
        {
            services.AddSingleton<IAntiforgeryTokenIssuer, AntiforgeryTokenIssuer>();
        }

        // Act
        var act = () => WorkerHostComposition.Validate(WorkerAssemblyName, UnreachableInfrastructure, RegisterApiHostIssuer);

        // Assert
        act.Should().Throw<WorkerHostCompositionException>().WithMessage($"*{typeof(IAntiforgery).FullName}*");
    }

    [Fact]
    public void Validate_WhenSingletonCapturesScopedService_ShouldFail()
    {
        // Arrange
        void RegisterCaptiveDependency(IServiceCollection services)
        {
            services.AddSingleton<SingletonCapturingExecutionContext>();
        }

        // Act
        var act = () => WorkerHostComposition.Validate(WorkerAssemblyName, UnreachableInfrastructure, RegisterCaptiveDependency);

        // Assert
        act.Should().Throw<WorkerHostCompositionException>().WithMessage($"*scoped service '{typeof(IExecutionContext).FullName}'*");
    }

    // Constructed only by the container, which is the point: its singleton lifetime captures the scoped execution context
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed record SingletonCapturingExecutionContext(IExecutionContext ExecutionContext);
}
