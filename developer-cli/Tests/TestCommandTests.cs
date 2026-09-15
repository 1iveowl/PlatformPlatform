using DeveloperCli.Commands;
using FluentAssertions;

namespace DeveloperCli.Tests;

public sealed class TestCommandTests
{
    [Fact]
    public void SelectTestTargets_WhenNoTargetFlag_ShouldRunApplicationSolutionAndBlazor()
    {
        // Act
        var targets = TestCommand.SelectTestTargets(false, null, false, false, false);

        // Assert
        targets.Should().Equal(TestTarget.Application, TestTarget.Blazor);
    }

    [Fact]
    public void SelectTestTargets_WhenBlazor_ShouldRunOnlyBlazor()
    {
        // Act
        var targets = TestCommand.SelectTestTargets(false, null, false, true, false);

        // Assert
        targets.Should().Equal(TestTarget.Blazor);
    }

    [Fact]
    public void SelectTestTargets_WhenBackend_ShouldRunOnlyApplicationSolution()
    {
        // Act
        var targets = TestCommand.SelectTestTargets(true, null, false, false, false);

        // Assert
        targets.Should().Equal(TestTarget.Application);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectTestTargets_WhenSelfContainedSystem_ShouldRunOnlyThatSystem(bool backend)
    {
        // Act
        var targets = TestCommand.SelectTestTargets(backend, "account", false, false, false);

        // Assert
        targets.Should().Equal(TestTarget.SelfContainedSystem);
    }

    [Fact]
    public void SelectTestTargets_WhenGateway_ShouldRunOnlyGatewayTests()
    {
        // Act
        var targets = TestCommand.SelectTestTargets(false, null, true, false, false);

        // Assert
        targets.Should().Equal(TestTarget.Gateway);
    }

    [Fact]
    public void SelectTestTargets_WhenCli_ShouldRunOnlyDeveloperCliTests()
    {
        // Act
        var targets = TestCommand.SelectTestTargets(false, null, false, false, true);

        // Assert
        targets.Should().Equal(TestTarget.DeveloperCli);
    }

    [Fact]
    public void SelectTestTargets_WhenBackendBlazorAndCli_ShouldRunTheUnionInOrder()
    {
        // Act
        var targets = TestCommand.SelectTestTargets(true, null, false, true, true);

        // Assert
        targets.Should().Equal(TestTarget.Application, TestTarget.Blazor, TestTarget.DeveloperCli);
    }

    [Theory]
    [InlineData("PlatformPlatform.slnx")]
    [InlineData("Account.slnf")]
    [InlineData("AppGateway.Tests/AppGateway.Tests.csproj")]
    [InlineData("Blazor.slnx")]
    [InlineData("DeveloperCli.slnx")]
    public void BuildTestCommand_WhenNoFilter_ShouldExcludeNoisyCategoryForEveryTarget(string targetName)
    {
        // Act
        var command = TestCommand.BuildTestCommand(targetName, null, null);

        // Assert
        command.Should().Be($"dotnet test {targetName} --no-build --no-restore --logger \"console;verbosity=normal\" --filter \"Category!=Noisy\" ");
    }

    [Theory]
    [InlineData("PlatformPlatform.slnx")]
    [InlineData("Blazor.slnx")]
    public void BuildTestCommand_WhenFilterAndExcludedCategory_ShouldCombineBothForEveryTarget(string targetName)
    {
        // Act
        var command = TestCommand.BuildTestCommand(targetName, "FullyQualifiedName~LoginTests", "RequiresDocker");

        // Assert
        command.Should().Be($"dotnet test {targetName} --no-build --no-restore --logger \"console;verbosity=normal\" --filter \"(FullyQualifiedName~LoginTests)&Category!=RequiresDocker\" ");
    }

    [Theory]
    [InlineData(null, null, " --filter \"Category!=Noisy\" ")]
    [InlineData("Name~Tests", null, " --filter \"(Name~Tests)&Category!=Noisy\" ")]
    [InlineData("Name~Tests", "", " --filter \"Name~Tests\" ")]
    [InlineData(null, "", "")]
    [InlineData(null, "RequiresDocker", " --filter \"Category!=RequiresDocker\" ")]
    public void BuildFilterArgument_ShouldComposeFilterAndCategoryExclusion(string? filter, string? excludeCategory, string expected)
    {
        // Act
        var filterArgument = TestCommand.BuildFilterArgument(filter, excludeCategory);

        // Assert
        filterArgument.Should().Be(expected);
    }

    [Theory]
    [InlineData(true, "dotnet build Blazor.slnx")]
    [InlineData(false, "dotnet build Blazor.slnx --verbosity quiet")]
    public void BuildBuildCommand_ShouldKeepTheQuietAndVerboseForms(bool quiet, string expected)
    {
        // Act
        var command = TestCommand.BuildBuildCommand("Blazor.slnx", quiet);

        // Assert
        command.Should().Be(expected);
    }

    [Theory]
    [InlineData("c12-public-client", true)]
    [InlineData("b2-render-split", true)]
    [InlineData("../application", false)]
    [InlineData("C12-Public-Client", false)]
    [InlineData("c12/public-client", false)]
    [InlineData("c12-", false)]
    [InlineData("", false)]
    public void IsValidSpikeName_ShouldAcceptOnlyOneKebabCaseFolderName(string spikeName, bool expected)
    {
        // Act
        var isValid = TestCommand.IsValidSpikeName(spikeName);

        // Assert
        isValid.Should().Be(expected);
    }

    [Theory]
    [InlineData(new[] { 0 }, 0)]
    [InlineData(new[] { 0, 0 }, 0)]
    [InlineData(new[] { 1, 0 }, 1)]
    [InlineData(new[] { 0, 3 }, 3)]
    [InlineData(new[] { 2, 1 }, 2)]
    public void CombineExitCodes_WhenAnyTargetFailed_ShouldReturnTheFirstNonZeroExitCode(int[] exitCodes, int expected)
    {
        // Act
        var exitCode = TestCommand.CombineExitCodes(exitCodes);

        // Assert
        exitCode.Should().Be(expected);
    }
}
