using System.Reflection;
using Basil.Domain;
using NetArchTest.Rules;
using TestResult = NetArchTest.Rules.TestResult;

namespace Basil.ArchitectureTests;

/// <summary>
///     Enforces the dependency direction inside and around the module projects: <c>Domain</c> knows no other project,
///     the <c>Persistence</c> namespaces of a module Application project know neither its capabilities nor its runtime,
///     the capabilities do not see the runtime, the Application projects do not see Infrastructure, and the
///     Infrastructure projects do not see the runtime, the hosts or the bot. Which project may reference which is the
///     business of <see cref="ModuleGraphTests" />.
/// </summary>
/// <remarks>The hosts are not covered yet: they are migrated after the tests.</remarks>
public class LayeringTests
{
	private const string Host = "Basil.Host";
	private const string Protocol = "Basil.Protocol";
	private const string Bot = "Basil.Bot";
	private const string Persistence = @"^Basil\.Application\.\w+\.Persistence";
	private const string Capabilities = @"^Basil\.Application\.\w+\.Capabilities";
	private const string Runtime = @"^Basil\.Application\.\w+\.(Runtime|Users|Connections)";
	private const string InfrastructureRuntime = @"^Basil\.Infrastructure\.\w+\.Runtime";
	private static readonly Assembly DomainAssembly = typeof(AssemblyMarker).Assembly;
	private static readonly Assembly ProtocolBancho = typeof(Protocol.Bancho.AssemblyMarker).Assembly;
	private static readonly Assembly ProtocolIrc = typeof(Protocol.Irc.AssemblyMarker).Assembly;
	private static readonly Assembly BotAssembly = Assembly.Load(Bot);

	private static readonly string[] ModuleRoots =
		ModuleAssemblies.Modules.SelectMany(module => new[] { $"Basil.Application.{module}", $"Basil.Infrastructure.{module}" })
			.Concat(["Basil.Application.Common", "Basil.Infrastructure.Common"]).ToArray();

	[Fact]
	public void Domain_DependsOnNoOtherProjectOrFramework()
	{
		AssertNoDependency(DomainAssembly,
			[..ModuleRoots, Host, Protocol, Bot, "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore",
				"Npgsql", "Dapper"]);
	}

	[Theory]
	[InlineData("Bancho")]
	[InlineData("Irc")]
	public void Protocol_DependsOnNoOtherProject(string protocol)
	{
		var assembly = protocol == "Bancho" ? ProtocolBancho : ProtocolIrc;
		AssertNoDependency(assembly, ["Basil.Domain", ..ModuleRoots, Host, Bot]);
	}

	[Fact]
	public void ApplicationPersistence_DoesNotSeeCapabilitiesRuntimeInfrastructureOrTheHosts()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Persistence)
			.ShouldNot().HaveDependencyOnAny([..CapabilityAndRuntimeNamespaces(), Host]).GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ApplicationCapabilities_DoNotSeeRuntime()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Capabilities)
			.ShouldNot().HaveDependencyOnAny(RuntimeNamespaces()).GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ApplicationCapabilities_PublicTypesAreOnlyThePortsAndTheDataTheyExchange()
	{
		var publicTypes = ModuleAssemblies.Applications.SelectMany(assembly => assembly.GetTypes())
			.Where(type => type is { IsPublic: true, IsNested: false } &&
			               type.Namespace is not null &&
			               System.Text.RegularExpressions.Regex.IsMatch(type.Namespace, Capabilities))
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
	public void ApplicationProjects_DoNotDependOnInfrastructureHostsOrProtocol()
	{
		foreach (var assembly in ModuleAssemblies.Applications)
			AssertNoDependency(assembly,
				["Basil.Infrastructure.Common", ..InfrastructureNamespaces(), Host, Protocol, Bot]);
	}

	[Fact]
	public void ApplicationRuntime_DoesDependOnPersistence_PositiveControl()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Runtime)
			.Should().NotHaveDependencyOnAny(PersistenceNamespaces()).GetResult();

		Assert.False(result.IsSuccessful);
	}

	[Fact]
	public void InfrastructureProjects_OutsideTheirRuntimeFolderDoNotSeeRuntimeHostsOrBot()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Infrastructures).That()
			.DoNotResideInNamespaceMatching(InfrastructureRuntime).ShouldNot()
			.HaveDependencyOnAny([..RuntimeNamespaces(), Host, Protocol, Bot]).GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void InfrastructureProjects_DoNotSeeCapabilitiesOutsideTheirOwnModule()
	{
		foreach (var assembly in ModuleAssemblies.Infrastructures.Where(assembly =>
			         assembly.GetName().Name != "Basil.Infrastructure.Common"))
		{
			var module = assembly.GetName().Name!.Split('.')[1];
			var others = ModuleAssemblies.Modules.Where(other => other != module)
				.Select(other => $"Basil.{other}.");
			AssertNoDependency(assembly, [..others]);
		}
	}

	[Fact]
	public void InfrastructureRuntime_DoesNotSeeCapabilitiesOrTheHosts()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Infrastructures).That()
			.ResideInNamespaceMatching(InfrastructureRuntime).ShouldNot()
			.HaveDependencyOnAny(CapabilityNamespaces()).GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void Bot_DoesNotSeeCapabilitiesInfrastructureOrTheHosts()
	{
		AssertNoDependency(BotAssembly, [..CapabilityNamespaces(), "Basil.Infrastructure.Common", ..InfrastructureNamespaces(),
			Host, Protocol]);
	}

	[Fact]
	public void ApplicationRuntime_HasNoSessionNamedType()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Runtime)
			.And().ArePublic().Should().NotHaveNameMatching(".*Session$").GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ApplicationRuntime_HasNoPublicServiceContract()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Runtime)
			.And().AreInterfaces().And().ArePublic().Should().NotHaveNameMatching("^I.*Service$").GetResult();

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

	private static string[] PersistenceNamespaces()
	{
		return ModuleAssemblies.Modules.Select(module => $"Basil.Application.{module}.Persistence").ToArray();
	}

	private static string[] CapabilityNamespaces()
	{
		return ModuleAssemblies.Modules.Select(module => $"Basil.Application.{module}.Capabilities").ToArray();
	}

	private static string[] RuntimeNamespaces()
	{
		return
		[
			..ModuleAssemblies.Modules.Select(module => $"Basil.Application.{module}.Runtime"),
			"Basil.Application.Users.Users", "Basil.Application.Users.Connections"
		];
	}

	private static string[] CapabilityAndRuntimeNamespaces()
	{
		return [..CapabilityNamespaces(), ..RuntimeNamespaces(), ..InfrastructureNamespaces(), Host, Protocol, Bot];
	}

	private static string[] InfrastructureNamespaces()
	{
		return ModuleAssemblies.Modules.Select(module => $"Basil.Infrastructure.{module}").ToArray();
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