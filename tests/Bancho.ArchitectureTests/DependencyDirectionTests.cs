using System.Reflection;
using NetArchTest.Rules;

namespace Bancho.ArchitectureTests;

/// <summary>
/// Enforces Clean Architecture dependency direction: Domain and Application must never
/// depend on Infrastructure, Web, or framework/ORM/web assemblies (dep-inward-only,
/// frame-domain-purity).
/// </summary>
public class DependencyDirectionTests
{
    private static readonly Assembly DomainAssembly = typeof(Bancho.Domain.AssemblyMarker).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Bancho.Application.AssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Bancho.Infrastructure.AssemblyMarker).Assembly;
    private static readonly Assembly ProtocolAssembly = typeof(Bancho.Protocol.AssemblyMarker).Assembly;

    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_Infrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("Bancho.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_Application()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("Bancho.Application")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Domain_Should_Not_HaveDependencyOn_Web()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("Bancho.Web")
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
                "StackExchange.Redis",
                "MySqlConnector",
                "Dapper")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Application_Should_Not_HaveDependencyOn_Infrastructure()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("Bancho.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Application_Should_Not_HaveDependencyOn_Web()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("Bancho.Web")
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
                "StackExchange.Redis",
                "MySqlConnector",
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
                "Bancho.Domain",
                "Bancho.Application",
                "Bancho.Infrastructure",
                "Bancho.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    [Fact]
    public void Infrastructure_Should_Not_HaveDependencyOn_Web()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("Bancho.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, FailureMessage(result));
    }

    private static string FailureMessage(TestResult result)
    {
        if (result.IsSuccessful)
        {
            return string.Empty;
        }

        var offenders = result.FailingTypes is null
            ? "unknown"
            : string.Join(", ", result.FailingTypes.Select(t => t.FullName));

        return $"Architecture rule violated by: {offenders}";
    }
}
