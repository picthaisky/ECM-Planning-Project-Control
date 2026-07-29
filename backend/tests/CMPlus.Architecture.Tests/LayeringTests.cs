using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;

namespace CMPlus.Architecture.Tests;

/// <summary>
/// Fitness functions for ADR-0001 (Clean Architecture layering). This project references every
/// layer, including WebApi, purely so these reflection-based checks can load their assemblies -
/// it contains no production code and is never referenced by anything else.
/// </summary>
public class LayeringTests
{
    private static readonly Assembly DomainAssembly = typeof(CMPlus.Domain.Common.Result).Assembly;

    private static readonly string[] DisallowedForDomain =
    [
        "CMPlus.Application",
        "CMPlus.Infrastructure",
        "CMPlus.WebApi",
        "Microsoft.EntityFrameworkCore",
        "MediatR",
        "FluentValidation",
        "Microsoft.AspNetCore",
    ];

    [Fact]
    public void Domain_Types_Do_Not_Depend_On_Outer_Layers_Or_Frameworks()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(DisallowedForDomain)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_Assembly_Manifest_References_No_Outer_Layer_Or_Framework_Assembly()
    {
        // Belt-and-braces structural check at the assembly-manifest level: catches a stray
        // PackageReference/ProjectReference even before any type in Domain actually uses it.
        var referenced = DomainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        var violations = referenced
            .Where(name => DisallowedForDomain.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(violations.Length == 0, $"CMPlus.Domain references disallowed assemblies: {string.Join(", ", violations)}");
    }

    [Fact]
    public void WebApi_Project_File_Does_Not_Reference_EfCore_Packages_Directly()
    {
        // Parsed as XML (not a raw substring search) so an explanatory comment mentioning EF
        // Core by name cannot produce a false positive - only an actual <PackageReference>
        // element counts.
        var csprojPath = SolutionRelativePath(Path.Combine("src", "CMPlus.WebApi", "CMPlus.WebApi.csproj"));
        var project = XDocument.Load(csprojPath);

        var efCoreReferences = project.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(include => include is not null && include.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(efCoreReferences.Length == 0, $"CMPlus.WebApi.csproj references EF Core package(s) directly: {string.Join(", ", efCoreReferences)}");
    }

    [Fact]
    public void WebApi_Assembly_Manifest_References_No_EfCore_Assembly()
    {
        var webApiAssembly = Assembly.Load("CMPlus.WebApi");

        var violation = webApiAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .FirstOrDefault(name => name is not null && name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        Assert.Null(violation);
    }

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is null ? "unknown failure" : string.Join(", ", result.FailingTypeNames);

    private static string SolutionRelativePath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CMPlus.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate CMPlus.sln from the test output directory.");
        }

        return Path.Combine(dir.FullName, relative);
    }
}
