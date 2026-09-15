using DeveloperCli.Commands;
using FluentAssertions;

namespace DeveloperCli.Tests;

public sealed class CheckCommandTests
{
    [Fact]
    public void BuildCheckSteps_WhenNoFlags_ShouldCheckBackendFrontendAndBlazorAndTestWithoutTarget()
    {
        // Act
        var steps = CheckCommand.BuildCheckSteps(false, false, false, false, null);

        // Assert
        steps.Should().Equal(
            new CheckStep("Build", "dotnet run build --backend --frontend --blazor"),
            new CheckStep("Test", "dotnet run test"),
            new CheckStep("Format", "dotnet run format --backend --frontend --blazor"),
            new CheckStep("Lint", "dotnet run lint --backend --frontend --blazor")
        );
    }

    [Fact]
    public void BuildCheckSteps_WhenBackend_ShouldTestOnlyBackend()
    {
        // Act
        var steps = CheckCommand.BuildCheckSteps(true, false, false, false, null);

        // Assert
        steps.Should().Equal(
            new CheckStep("Build", "dotnet run build --backend"),
            new CheckStep("Test", "dotnet run test --backend"),
            new CheckStep("Format", "dotnet run format --backend"),
            new CheckStep("Lint", "dotnet run lint --backend")
        );
    }

    [Fact]
    public void BuildCheckSteps_WhenFrontend_ShouldNotRunTests()
    {
        // Act
        var steps = CheckCommand.BuildCheckSteps(false, true, false, false, null);

        // Assert
        steps.Should().Equal(
            new CheckStep("Build", "dotnet run build --frontend"),
            new CheckStep("Format", "dotnet run format --frontend"),
            new CheckStep("Lint", "dotnet run lint --frontend")
        );
    }

    [Fact]
    public void BuildCheckSteps_WhenCli_ShouldNotRunTests()
    {
        // Act
        var steps = CheckCommand.BuildCheckSteps(false, false, true, false, null);

        // Assert
        steps.Should().Equal(
            new CheckStep("Build", "dotnet run build --cli"),
            new CheckStep("Format", "dotnet run format --cli"),
            new CheckStep("Lint", "dotnet run lint --cli")
        );
    }

    [Fact]
    public void BuildCheckSteps_WhenBlazor_ShouldCheckOnlyBlazor()
    {
        // Act
        var steps = CheckCommand.BuildCheckSteps(false, false, false, true, null);

        // Assert
        steps.Should().Equal(
            new CheckStep("Build", "dotnet run build --blazor"),
            new CheckStep("Test", "dotnet run test --blazor"),
            new CheckStep("Format", "dotnet run format --blazor"),
            new CheckStep("Lint", "dotnet run lint --blazor")
        );
    }

    [Fact]
    public void BuildCheckSteps_WhenBackendFrontendAndBlazor_ShouldCheckTheUnion()
    {
        // Act
        var steps = CheckCommand.BuildCheckSteps(true, true, false, true, null);

        // Assert
        steps.Should().Equal(
            new CheckStep("Build", "dotnet run build --backend --frontend --blazor"),
            new CheckStep("Test", "dotnet run test --backend --blazor"),
            new CheckStep("Format", "dotnet run format --backend --frontend --blazor"),
            new CheckStep("Lint", "dotnet run lint --backend --frontend --blazor")
        );
    }

    [Fact]
    public void BuildCheckSteps_WhenOnlySelfContainedSystem_ShouldKeepThePreviousCommandsWithoutBlazor()
    {
        // Act
        var steps = CheckCommand.BuildCheckSteps(false, false, false, false, "account");

        // Assert
        steps.Should().Equal(
            new CheckStep("Build", "dotnet run build --backend --frontend --self-contained-system account"),
            new CheckStep("Test", "dotnet run test --self-contained-system account"),
            new CheckStep("Format", "dotnet run format --backend --frontend --self-contained-system account"),
            new CheckStep("Lint", "dotnet run lint --backend --frontend --self-contained-system account")
        );
    }

    [Fact]
    public void BuildCheckSteps_WhenBackendAndSelfContainedSystem_ShouldPassTheSystemToEveryStep()
    {
        // Act
        var steps = CheckCommand.BuildCheckSteps(true, false, false, false, "account");

        // Assert
        steps.Should().Equal(
            new CheckStep("Build", "dotnet run build --backend --self-contained-system account"),
            new CheckStep("Test", "dotnet run test --backend --self-contained-system account"),
            new CheckStep("Format", "dotnet run format --backend --self-contained-system account"),
            new CheckStep("Lint", "dotnet run lint --backend --self-contained-system account")
        );
    }
}
