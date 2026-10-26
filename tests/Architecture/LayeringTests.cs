using System.Reflection;
using Basil.Domain;
using NetArchTest.Rules;
using TestResult = NetArchTest.Rules.TestResult;

namespace Basil.ArchitectureTests;

/// <summary>
///     Enforces the dependency direction of the solution:
///     <c>Domain &lt;- Application.Persistence &lt;- Application.Capabilities &lt;- Application.Runtime</c>, with
///     each Infrastructure project implementing the Application project of the same name and the hosts and the bot
///     on top.
/// </summary>
/// <remarks>The hosts are not covered yet: they are migrated after the tests.</remarks>
public class LayeringTests
{
	private const string ApplicationRuntime = "Basil.Application.Runtime";
	private const string ApplicationCapabilities = "Basil.Application.Capabilities";
	private const string ApplicationPersistence = "Basil.Application.Persistence";
	private const string Infrastructure = "Basil.Infrastructure";
	private const string Host = "Basil.Host";
	private const string Protocol = "Basil.Protocol";
	private const string Bot = "Basil.Bot";
	private static readonly Assembly DomainAssembly = typeof(AssemblyMarker).Assembly;
	private static readonly Assembly ProtocolBancho = typeof(Protocol.Bancho.AssemblyMarker).Assembly;
	private static readonly Assembly ProtocolIrc = typeof(Protocol.Irc.AssemblyMarker).Assembly;
	private static readonly Assembly PersistenceAssembly = Assembly.Load(ApplicationPersistence);
	private static readonly Assembly CapabilitiesAssembly = Assembly.Load(ApplicationCapabilities);
	private static readonly Assembly RuntimeAssembly = Assembly.Load(ApplicationRuntime);
	private static readonly Assembly InfrastructurePersistence = Assembly.Load("Basil.Infrastructure.Persistence");
	private static readonly Assembly InfrastructureCapabilities = Assembly.Load("Basil.Infrastructure.Capabilities");
	private static readonly Assembly InfrastructureRuntime = Assembly.Load("Basil.Infrastructure.Runtime");
	private static readonly Assembly BotAssembly = Assembly.Load(Bot);

	[Fact]
	public void Domain_DependsOnNoOtherProjectOrFramework()
	{
		AssertNoDependency(DomainAssembly, "Basil.Application", Infrastructure, Host, Protocol, Bot,
			"Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql", "Dapper");
	}

	[Theory]
	[InlineData("Bancho")]
	[InlineData("Irc")]
	public void Protocol_DependsOnNoOtherProject(string protocol)
	{
		var assembly = protocol == "Bancho" ? ProtocolBancho : ProtocolIrc;
		AssertNoDependency(assembly, "Basil.Domain", "Basil.Application", Infrastructure, Host, Bot);
	}

	[Fact]
	public void ApplicationPersistence_DependsOnlyOnDomain()
	{
		AssertNoDependency(PersistenceAssembly, ApplicationRuntime, ApplicationCapabilities, Infrastructure, Host,
			Protocol, Bot);
	}

	[Fact]
	public void ApplicationCapabilities_DoNotSeeRuntime()
	{
		AssertNoDependency(CapabilitiesAssembly, ApplicationRuntime, Infrastructure, Host, Protocol, Bot);
	}

	[Fact]
	public void ApplicationCapabilities_PublicTypesAreOnlyThePortsAndTheDataTheyExchange()
	{
		var publicTypes = CapabilitiesAssembly.GetTypes().Where(type => type is { IsPublic: true, IsNested: false })
			.ToList();
		var exchanged = new HashSet<Type>();
		var pending = new Stack<Type>(publicTypes.Where(type => type.IsInterface)
			.SelectMany(port => port.GetMethods())
			.SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
				.Append(method.ReturnType))
			.SelectMany(Unwrap));
		while (pending.TryPop(out var type))
		{
			if (!exchanged.Add(type)) continue;
			foreach (var property in type.GetProperties())
			foreach (var inner in Unwrap(property.PropertyType))
				pending.Push(inner);
		}

		var logic = publicTypes.Where(type => !type.IsInterface && !exchanged.Contains(type)).Select(type => type.Name)
			.ToList();

		Assert.True(logic.Count == 0, "Public logic types that are not port data: " + string.Join(", ", logic));
	}

	private static IEnumerable<Type> Unwrap(Type type)
	{
		yield return type;
		var inner = type.HasElementType ? [type.GetElementType()!] : type.GetGenericArguments();
		foreach (var nested in inner.SelectMany(Unwrap)) yield return nested;
	}

	[Fact]
	public void ApplicationRuntime_DoesNotDependOnInfrastructureHostsOrProtocol()
	{
		AssertNoDependency(RuntimeAssembly, Infrastructure, Host, Protocol, Bot);
	}

	[Fact]
	public void ApplicationRuntime_DoesDependOnPersistence_PositiveControl()
	{
		var result = Types.InAssembly(RuntimeAssembly).Should().NotHaveDependencyOnAny(ApplicationPersistence)
			.GetResult();

		Assert.False(result.IsSuccessful);
	}

	[Theory]
	[InlineData("Persistence")]
	[InlineData("Capabilities")]
	public void InfrastructurePortImplementations_DoNotSeeRuntimeHostsOrBot(string project)
	{
		var assembly = project == "Persistence" ? InfrastructurePersistence : InfrastructureCapabilities;
		AssertNoDependency(assembly, ApplicationRuntime, Host, Protocol, Bot);
	}

	[Fact]
	public void InfrastructurePersistence_DoesNotSeeCapabilities()
	{
		AssertNoDependency(InfrastructurePersistence, ApplicationCapabilities);
	}

	[Fact]
	public void InfrastructureRuntime_DoesNotSeeCapabilitiesOrTheHosts()
	{
		AssertNoDependency(InfrastructureRuntime, ApplicationCapabilities, Host, Protocol);
	}

	[Fact]
	public void Bot_DoesNotSeeCapabilitiesInfrastructureOrTheHosts()
	{
		AssertNoDependency(BotAssembly, ApplicationCapabilities, Infrastructure, Host, Protocol);
	}

	[Fact]
	public void ApplicationRuntime_HasNoSessionNamedType()
	{
		var result = Types.InAssembly(RuntimeAssembly).That().ArePublic().Should().NotHaveNameMatching(".*Session$")
			.GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ApplicationRuntime_HasNoPublicServiceContract()
	{
		var result = Types.InAssembly(RuntimeAssembly).That().AreInterfaces().And().ArePublic().Should()
			.NotHaveNameMatching("^I.*Service$").GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void Sources_DoNotUseConfigureAwait()
	{
		var sources = Path.Combine(FindRepositoryRoot(), "src");
		var offenders = Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories)
			.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
			               && File.ReadAllText(path).Contains("ConfigureAwait(", StringComparison.Ordinal))
			.ToList();

		Assert.True(offenders.Count == 0, "ConfigureAwait is not used: " + string.Join(", ", offenders));
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			directory = directory.Parent;

		return directory?.FullName ??
		       throw new InvalidOperationException("Basil.slnx was not found above the test binaries.");
	}

	private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
	{
		var result = Types.InAssembly(assembly).Should().NotHaveDependencyOnAny(forbidden).GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	private static string FailureMessage(TestResult result)
	{
		return result.IsSuccessful
			? string.Empty
			: "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
	}
}