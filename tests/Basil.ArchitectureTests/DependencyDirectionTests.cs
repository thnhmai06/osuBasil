using System.Reflection;
using Basil.Domain;
using NetArchTest.Rules;

namespace Basil.ArchitectureTests;

/// <summary>
///     Enforces the two project-level dependency rules that survive the merge of the former
///     Application, Infrastructure, and Web projects into <c>Basil.Infrastructure</c> and
///     <c>Basil.Host</c>: Domain stays free of both and of persistence/web frameworks, and
///     Protocol depends on neither.
/// </summary>
/// <remarks>
///     Rules about the boundaries <em>inside</em> <c>Basil.Infrastructure</c> -- which slice may
///     reference which, and what <c>Shared</c> is allowed to contain -- live in
///     <see cref="SliceBoundaryTests" />, because a single assembly cannot express them as
///     assembly-level dependencies.
/// </remarks>
public class DependencyDirectionTests
{
	private static readonly Assembly DomainAssembly = typeof(AssemblyMarker).Assembly;
	private static readonly Assembly ProtocolBanchoAssembly = typeof(Protocol.AssemblyMarker).Assembly;
	private static readonly Assembly ProtocolIrcAssembly = typeof(Protocol.Irc.AssemblyMarker).Assembly;

	[Fact]
	public void Domain_Should_Not_HaveDependencyOn_Server()
	{
		var result = Types.InAssembly(DomainAssembly)
			.Should()
			.NotHaveDependencyOnAny("Basil.Application", "Basil.Infrastructure", "Basil.Host")
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
	public void ProtocolBancho_Should_Not_HaveDependencyOn_AnyOtherProject()
	{
		var result = Types.InAssembly(ProtocolBanchoAssembly)
			.Should()
			.NotHaveDependencyOnAny("Basil.Domain", "Basil.Infrastructure", "Basil.Host")
			.GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ProtocolIrc_Should_Not_HaveDependencyOn_AnyOtherProject()
	{
		var result = Types.InAssembly(ProtocolIrcAssembly)
			.Should()
			.NotHaveDependencyOnAny("Basil.Domain", "Basil.Infrastructure", "Basil.Host")
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