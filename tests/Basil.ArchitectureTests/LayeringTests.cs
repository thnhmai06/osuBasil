using System.Reflection;
using NetArchTest.Rules;

namespace Basil.ArchitectureTests;

/// <summary>
///     Enforces the dependency direction of the solution:
///     <c>Domain &lt;- Application.Storage &lt;- Application.Services (internal) &lt;- Application.Objects</c>, with the
///     Infrastructure projects implementing the ports and the hosts and the bot on top.
/// </summary>
/// <remarks>The hosts are not covered yet: they are migrated after the tests.</remarks>
public class LayeringTests
{
	private static readonly Assembly Domain = typeof(Basil.Domain.AssemblyMarker).Assembly;
	private static readonly Assembly ProtocolBancho = typeof(Basil.Protocol.Bancho.AssemblyMarker).Assembly;
	private static readonly Assembly ProtocolIrc = typeof(Basil.Protocol.Irc.AssemblyMarker).Assembly;
	private static readonly Assembly Storage = Assembly.Load("Basil.Application.Persistence");
	private static readonly Assembly Services = Assembly.Load("Basil.Application.Capabilities");
	private static readonly Assembly Objects = Assembly.Load("Basil.Application.Runtime");
	private static readonly Assembly InfrastructurePersistence = Assembly.Load("Basil.Infrastructure.Persistence");
	private static readonly Assembly InfrastructureCapabilities = Assembly.Load("Basil.Infrastructure.Capabilities");
	private static readonly Assembly InfrastructureRuntime = Assembly.Load("Basil.Infrastructure.Runtime");
	private static readonly Assembly Bot = Assembly.Load("Basil.Bot");

	private const string ApplicationRuntime = "Basil.Application.Runtime";
	private const string ApplicationCapabilities = "Basil.Application.Capabilities";
	private const string ApplicationPersistence = "Basil.Application.Persistence";
	private const string Infrastructure = "Basil.Infrastructure";
	private const string Host = "Basil.Host";
	private const string Protocol = "Basil.Protocol";

	[Fact]
	public void Domain_DependsOnNoOtherProjectOrFramework()
	{
		AssertNoDependency(Domain, "Basil.Application", Infrastructure, Host, Protocol, "Basil.Bot",
			"Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql", "Dapper");
	}

	[Theory]
	[InlineData("Bancho")]
	[InlineData("Irc")]
	public void Protocol_DependsOnNoOtherProject(string protocol)
	{
		var assembly = protocol == "Bancho" ? ProtocolBancho : ProtocolIrc;
		AssertNoDependency(assembly, "Basil.Domain", "Basil.Application", Infrastructure, Host, "Basil.Bot");
	}

	[Fact]
	public void ApplicationPersistence_DependsOnlyOnDomain()
	{
		AssertNoDependency(Storage, ApplicationRuntime, ApplicationCapabilities, Infrastructure, Host, Protocol,
			"Basil.Bot");
	}

	[Fact]
	public void ApplicationCapabilities_DoNotSeeObjects()
	{
		AssertNoDependency(Services, ApplicationRuntime, Infrastructure, Host, Protocol, "Basil.Bot");
	}

	[Fact]
	public void ApplicationCapabilities_HaveNoPublicType()
	{
		var result = Types.InAssembly(Services).That().AreNotNested().Should().NotBePublic().GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ApplicationRuntime_DoNotDependOnInfrastructureHostsOrProtocol()
	{
		AssertNoDependency(Objects, Infrastructure, Host, Protocol, "Basil.Bot");
	}

	[Theory]
	[InlineData("Storage")]
	[InlineData("Capabilities")]
	public void InfrastructurePortImplementations_DependOnlyOnTheStoragePorts(string project)
	{
		var assembly = project == "Storage" ? InfrastructurePersistence : InfrastructureCapabilities;
		AssertNoDependency(assembly, ApplicationRuntime, ApplicationCapabilities, Host, Protocol, "Basil.Bot");
	}

	[Fact]
	public void InfrastructureRuntime_DoesNotSeeServicesOrTheHosts()
	{
		AssertNoDependency(InfrastructureRuntime, ApplicationCapabilities, Host, Protocol);
	}

	[Fact]
	public void Bot_DoesNotSeeServicesInfrastructureOrTheHosts()
	{
		AssertNoDependency(Bot, ApplicationCapabilities, Infrastructure, Host, Protocol);
	}

	[Fact]
	public void ApplicationRuntime_HaveNoSessionNamedType()
	{
		var result = Types.InAssembly(Objects).That().ArePublic().Should().NotHaveNameMatching(".*Session$")
			.GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ApplicationRuntime_HaveNoPublicServiceContract()
	{
		var result = Types.InAssembly(Objects).That().AreInterfaces().And().ArePublic().Should()
			.NotHaveNameMatching("^I.*Service$").GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
	{
		var result = Types.InAssembly(assembly).Should().NotHaveDependencyOnAny(forbidden).GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	private static string FailureMessage(NetArchTest.Rules.TestResult result)
	{
		return result.IsSuccessful
			? string.Empty
			: "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
	}
}
