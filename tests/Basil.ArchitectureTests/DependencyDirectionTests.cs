using System.Reflection;
using Basil.Domain;
using NetArchTest.Rules;

namespace Basil.ArchitectureTests;

/// <summary>
///     Enforces Clean Architecture dependency direction: Domain and Application must never
///     depend on Infrastructure, Web, or framework/ORM/web assemblies (dep-inward-only,
///     frame-domain-purity).
/// </summary>
public class DependencyDirectionTests
{
    private static readonly Assembly DomainAssembly = typeof(AssemblyMarker).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.AssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.AssemblyMarker).Assembly;
    private static readonly Assembly ProtocolAssembly = typeof(Protocol.AssemblyMarker).Assembly;

    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_Infrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("Basil.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_Application()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("Basil.Application")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_Web()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("Basil.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_Frameworks()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Data.Sqlite",
                "Dapper")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Application_Should_Not_HaveDependencyOn_Infrastructure()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("Basil.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Application_Should_Not_HaveDependencyOn_Web()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("Basil.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Application_Should_Not_HaveDependencyOn_Frameworks()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Data.Sqlite",
                "Dapper")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Protocol_Should_Not_HaveDependencyOn_AnyOtherBanchoProject()
    {
        var result = Types.InAssembly(ProtocolAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Basil.Domain",
                "Basil.Application",
                "Basil.Infrastructure",
                "Basil.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Infrastructure_Should_Not_HaveDependencyOn_Web()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("Basil.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    private static string FailureMessage(TestResult result)
    {
        if (result.IsSuccessful) return string.Empty;

        var offenders = result.FailingTypes is null
            ? "unknown"
            : string.Join(", ", result.FailingTypes.Select(t => t.FullName));

        return $"Architecture rule violated by: {offenders}";
    }
}