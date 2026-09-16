using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using NetArchTest.Rules;
using Xunit;

namespace Account.Tests.ArchitectureTests;

// Handlers in Core are registered in the API host and in the worker host alike, so a handler that depends on a service
// only the API host provides makes the worker host fail dependency injection validation at startup. The worker serves
// no browser, so antiforgery is the service that is easiest to reach for by mistake; handlers issue tokens through
// IAntiforgeryTokenIssuer instead, which both hosts register.
public sealed class WorkerHostHandlerDependencyTests
{
    [Fact]
    public void CoreTypes_ShouldNotDependOnAntiforgeryServices()
    {
        // Act
        var result = Types
            .InAssembly(Configuration.Assembly)
            .Should().NotHaveDependencyOn(typeof(IAntiforgery).Namespace)
            .GetResult();

        // Assert
        var failingTypes = string.Join(", ", result.FailingTypes?.Select(t => t.Name) ?? []);
        result.IsSuccessful.Should().BeTrue($"The worker host has no antiforgery services, so these types break it: {failingTypes}");
    }
}
