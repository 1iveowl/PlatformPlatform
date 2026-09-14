using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Account.Tests.ArchitectureTests;

// The contracts and client assemblies are compiled into the Blazor WebAssembly client and a future native client, so
// they must never pull in a web host, a UI framework or a server dependency. The compiler drops a reference that no
// type uses, so the check reads what restore resolved (declared packages, framework references and the full transitive
// package and project graph) as well as the references compiled into the assembly. A localization assembly added for
// the clients joins PortableProjects.
public sealed class PortableAssemblyDependencyTests
{
    private static readonly string[] ForbiddenDependencyPrefixes =
    [
        "Microsoft.AspNetCore.",
        "Microsoft.EntityFrameworkCore",
        "MediatR",
        "NSwag",
        "Azure.",
        "Microsoft.JSInterop",
        "Microsoft.FluentUI",
        "Microsoft.Maui"
    ];

    public static TheoryData<string, string> PortableProjects => new()
    {
        { "shared-kernel/SharedKernel.Contracts", "SharedKernel.Contracts" },
        { "account/Contracts", "Account.Contracts" },
        { "account/Client", "Account.Client" }
    };

    [Theory]
    [MemberData(nameof(PortableProjects))]
    public void PortableProject_ShouldNotResolveForbiddenDependencies(string projectFolder, string projectName)
    {
        // Arrange
        var projectAssetsFile = FindProjectAssetsFile(projectFolder, projectName);
        using var projectAssets = JsonDocument.Parse(File.ReadAllText(projectAssetsFile));

        // Act
        var resolvedDependencies = GetResolvedDependencies(projectAssets.RootElement).ToArray();

        // Assert
        resolvedDependencies.Should().Contain(dependency => dependency == "Microsoft.NETCore.App", "the framework references must be read");
        var forbiddenDependencies = resolvedDependencies.Where(IsForbidden).Distinct().ToArray();
        forbiddenDependencies.Should().BeEmpty($"{projectName} is portable, but restore resolved {string.Join(", ", forbiddenDependencies)} in {projectAssetsFile}");
    }

    [Theory]
    [MemberData(nameof(PortableProjects))]
    public void PortableAssembly_ShouldNotReferenceForbiddenAssemblies(string projectFolder, string projectName)
    {
        // Arrange
        var assemblyFile = Path.Combine(AppContext.BaseDirectory, $"{projectName}.dll");
        File.Exists(assemblyFile).Should().BeTrue($"{projectName} must be copied to the test output of {projectFolder}");

        // Act
        var referencedAssemblyNames = GetReferencedAssemblyNames(assemblyFile);

        // Assert
        referencedAssemblyNames.Should().Contain("System.Runtime", "the compiled assembly references must be read");
        var forbiddenReferences = referencedAssemblyNames.Where(IsForbidden).ToArray();
        forbiddenReferences.Should().BeEmpty($"{projectName} is portable, but references {string.Join(", ", forbiddenReferences)}");
    }

    private static bool IsForbidden(string dependencyName)
    {
        return ForbiddenDependencyPrefixes.Any(prefix => dependencyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetResolvedDependencies(JsonElement projectAssets)
    {
        foreach (var target in projectAssets.GetProperty("targets").EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                yield return library.Name.Split('/')[0];
            }
        }

        foreach (var framework in projectAssets.GetProperty("project").GetProperty("frameworks").EnumerateObject())
        {
            if (framework.Value.TryGetProperty("dependencies", out var dependencies))
            {
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    yield return dependency.Name;
                }
            }

            if (framework.Value.TryGetProperty("frameworkReferences", out var frameworkReferences))
            {
                foreach (var frameworkReference in frameworkReferences.EnumerateObject())
                {
                    yield return frameworkReference.Name;
                }
            }
        }
    }

    private static string[] GetReferencedAssemblyNames(string assemblyFile)
    {
        using var stream = File.OpenRead(assemblyFile);
        using var portableExecutableReader = new PEReader(stream);
        var metadataReader = portableExecutableReader.GetMetadataReader();
        return metadataReader.AssemblyReferences
            .Select(handle => metadataReader.GetString(metadataReader.GetAssemblyReference(handle).Name))
            .ToArray();
    }

    // Projects under shared-kernel use the artifacts output layout, the others keep obj next to the project file
    private static string FindProjectAssetsFile(string projectFolder, string projectName)
    {
        var applicationFolder = FindApplicationFolder();
        string[] candidates =
        [
            Path.Combine(applicationFolder, projectFolder, "obj", "project.assets.json"),
            Path.Combine(applicationFolder, projectFolder.Split('/')[0], "artifacts", "obj", projectName, "project.assets.json")
        ];

        return candidates.FirstOrDefault(File.Exists)
               ?? throw new FileNotFoundException($"No project.assets.json for {projectName}; restore the solution first. Looked in: {string.Join(", ", candidates)}.");
    }

    private static string FindApplicationFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PlatformPlatform.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException($"PlatformPlatform.slnx was not found above {AppContext.BaseDirectory}.");
    }
}
