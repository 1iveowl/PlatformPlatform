using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Account.Tests.ArchitectureTests;

public class IdPrefixForAllStronglyTypedUlidTests
{
    [Fact]
    public void StronglyTypedUlidsInApplication_ShouldHaveIdPrefixAttribute()
    {
        // Act
        var result = Types
            .InAssemblies([Configuration.ContractsAssembly, Configuration.Assembly])
            .That().Inherit(typeof(StronglyTypedUlid<>))
            .Should().HaveCustomAttribute(typeof(IdPrefixAttribute))
            .GetResult();

        // Assert
        var idsWithoutPrefix = string.Join(", ", result.FailingTypes?.Select(t => t.Name) ?? []);
        result.IsSuccessful.Should().BeTrue($"The following strongly typed IDs does not have an IdPrefixAttribute: {idsWithoutPrefix}");
    }

    [Fact]
    public void StronglyTypedUlidsInApplication_ShouldHaveValidIdPrefix()
    {
        // Arrange
        var stronglyTypedUlidIds = Types
            .InAssemblies([Configuration.ContractsAssembly, Configuration.Assembly])
            .That().Inherit(typeof(StronglyTypedUlid<>))
            .GetTypes();

        // Assert
        foreach (var stronglyTypedId in stronglyTypedUlidIds)
        {
            var newId = typeof(StronglyTypedUlidGeneration).GetMethod(nameof(StronglyTypedUlidGeneration.NewUlidId))!.MakeGenericMethod(stronglyTypedId).Invoke(null, null);

            // Ids must follow the pattern: {prefix}_{ULID} where prefix is lowercase and ULID is uppercase
            newId.Should().NotBeNull();
            newId.ToString().Should().MatchRegex("^[a-z0-9]+_[A-Z0-9]{26}$");
        }
    }
}
