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
	private static readonly Assembly Storage = Assembly.Load("Basil.Application.Storage");
	private static readonly Assembly Services = Assembly.Load("Basil.Application.Services");
	private static readonly Assembly Objects = Assembly.Load("Basil.Application.Objects");
	private static readonly Assembly InfrastructureStorage = Assembly.Load("Basil.Infrastructure.Storage");
	private static readonly Assembly InfrastructureCapabilities = Assembly.Load("Basil.Infrastructure.Capabilities");
	private static readonly Assembly InfrastructureRuntime = Assembly.Load("Basil.Infrastructure.Runtime");
	private static readonly Assembly Bot = Assembly.Load("Basil.Bot.Application");

	private const string ApplicationObjects = "Basil.Application.Objects";
	private const string ApplicationServices = "Basil.Application.Services";
	private const string ApplicationStorage = "Basil.Application.Storage";
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
	public void ApplicationStorage_DependsOnlyOnDomain()
	{
		AssertNoDependency(Storage, ApplicationObjects, ApplicationServices, Infrastructure, Host, Protocol,
			"Basil.Bot");
	}

	[Fact]
	public void ApplicationServices_DoNotSeeObjects()
	{
		AssertNoDependency(Services, ApplicationObjects, Infrastructure, Host, Protocol, "Basil.Bot");
	}

	[Fact]
	public void ApplicationServices_HaveNoPublicType()
	{
		var result = Types.InAssembly(Services).That().AreNotNested().Should().NotBePublic().GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ApplicationObjects_DoNotDependOnInfrastructureHostsOrProtocol()
	{
		AssertNoDependency(Objects, Infrastructure, Host, Protocol, "Basil.Bot");
	}

	[Theory]
	[InlineData("Storage")]
	[InlineData("Capabilities")]
	public void InfrastructurePortImplementations_DependOnlyOnTheStoragePorts(string project)
	{
		var assembly = project == "Storage" ? InfrastructureStorage : InfrastructureCapabilities;
		AssertNoDependency(assembly, ApplicationObjects, ApplicationServices, Host, Protocol, "Basil.Bot");
	}

	[Fact]
	public void InfrastructureRuntime_DoesNotSeeServicesOrTheHosts()
	{
		AssertNoDependency(InfrastructureRuntime, ApplicationServices, Host, Protocol);
	}

	[Fact]
	public void BotApplication_DoesNotSeeServicesInfrastructureOrTheHosts()
	{
		AssertNoDependency(Bot, ApplicationServices, Infrastructure, Host, Protocol);
	}

	[Fact]
	public void ApplicationObjects_HaveNoSessionNamedType()
	{
		var result = Types.InAssembly(Objects).That().ArePublic().Should().NotHaveNameMatching(".*Session$")
			.GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ApplicationObjects_HaveNoPublicServiceContract()
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
