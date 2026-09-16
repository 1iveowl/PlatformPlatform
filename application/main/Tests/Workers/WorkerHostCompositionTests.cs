using FluentAssertions;
using SharedKernel.Tests.Configuration;
using Xunit;

namespace Main.Tests.Workers;

// The main worker host is built through the same shared worker registration path as the account worker. It is composed through
// its real entry point with validation forced on; the connection strings point at nothing and nothing connects before the
// entry point is stopped. The account worker tests prove that this guard fails on a missing or captive dependency.
public sealed class WorkerHostCompositionTests
{
    [Fact]
    public void Validate_WhenMainWorkerHostIsComposed_ShouldBuildWithValidationAndResolveHostedServices()
    {
        // Arrange
        var unreachableInfrastructure = new Dictionary<string, string>
        {
            ["environment"] = "Development",
            ["ConnectionStrings:main-database"] = "Host=127.0.0.1;Port=1;Database=main;Username=composition;Password=composition",
            ["ConnectionStrings:blob-storage"] = "UseDevelopmentStorage=true"
        };

        // Act
        var act = () => WorkerHostComposition.Validate("Main.Workers", unreachableInfrastructure);

        // Assert
        act.Should().NotThrow();
    }
}
