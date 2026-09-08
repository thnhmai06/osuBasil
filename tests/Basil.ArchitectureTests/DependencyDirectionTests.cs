using System.Reflection;
using Basil.Domain;
using NetArchTest.Rules;

namespace Basil.ArchitectureTests;

/// <summary>
///     Enforces the remaining Clean Architecture-era dependency rules that still apply after the
///     Application/Infrastructure/Web merge into Basil.Server: Domain must stay pure, and Protocol
///     must not depend on any other Bancho project.
/// </summary>
/// <remarks>
///     The Application- and Infrastructure-assembly variants of these checks (and the
///     <c>Application.AssemblyMarker</c> / <c>Infrastructure.AssemblyMarker</c> types they used to
///     assert against) were removed when those two projects merged into <c>Basil.Server</c> --
///     there is no longer a separate assembly for those rules to name. A later task replaces that
///     coverage with rules over the slice boundaries inside <c>Basil.Server</c> itself.
/// </remarks>
public class DependencyDirectionTests
{
	private static readonly Assembly DomainAssembly = typeof(AssemblyMarker).Assembly;
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
			.NotHaveDependencyOn("Basil.Server")
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
	public void Protocol_Should_Not_HaveDependencyOn_AnyOtherBanchoProject()
	{
		var result = Types.InAssembly(ProtocolAssembly)
			.Should()
			.NotHaveDependencyOnAny(
				"Basil.Domain",
				"Basil.Application",
				"Basil.Infrastructure",
				"Basil.Server")
			.GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	private static string FailureMessage(NetArchTest.Rules.TestResult result)
	{
		if (result.IsSuccessful) return string.Empty;

		var offenders = result.FailingTypes is null
			? "unknown"
			: string.Join(", ", result.FailingTypes.Select(t => t.FullName));

		return $"Architecture rule violated by: {offenders}";
	}
}