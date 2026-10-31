using System.Reflection;
using System.Text.RegularExpressions;
using Basil.Domain;
using NetArchTest.Rules;
using TestResult = NetArchTest.Rules.TestResult;

namespace Basil.ArchitectureTests;

/// <summary>
///     Enforces the dependency direction inside and around the module projects: <c>Domain</c> knows no other project,
///     a module's <c>Persistence</c> is its lowest layer and sees none of the folders above it, the capabilities do not
///     react to events or run background work, the Application projects do not see Infrastructure, and the
///     Infrastructure projects do not see the Application objects, the hosts or the bot. Which project may reference
///     which is the business of <see cref="ModuleGraphTests" />.
/// </summary>
/// <remarks>The hosts are not covered yet: they are migrated after the tests.</remarks>
public class LayeringTests
{
	private const string Host = "Basil.Host";
	private const string Protocol = "Basil.Protocol";
	private const string Bot = "Basil.Bot";
	private const string Persistence = @"^Basil\.Application\.\w+\.Persistence";
	private const string Capabilities = @"^Basil\.Application\.\w+\.Capabilities";
	private const string Objects = @"^Basil\.Application\.\w+\.Objects";
	private const string Storage = @"^Basil\.Application\.\w+\.Storage";
	private const string InfrastructureWorkers = @"^Basil\.Infrastructure\.\w+\.Workers";
	private static readonly Assembly DomainAssembly = typeof(AssemblyMarker).Assembly;
	private static readonly Assembly ProtocolBancho = typeof(Protocol.Bancho.AssemblyMarker).Assembly;
	private static readonly Assembly ProtocolIrc = typeof(Protocol.Irc.AssemblyMarker).Assembly;
	private static readonly Assembly BotAssembly = Assembly.Load(Bot);

	private static readonly string[] ModuleRoots =
		ModuleAssemblies.Modules
			.SelectMany(module => new[] { $"Basil.Application.{module}", $"Basil.Infrastructure.{module}" })
			.Concat(["Basil.Application.Common", "Basil.Infrastructure.Common"]).ToArray();

	[Fact]
	public void Domain_DependsOnNoOtherProjectOrFramework()
	{
		AssertNoDependency(DomainAssembly,
		[
			.. ModuleRoots, Host, Protocol, Bot, "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore",
			"Npgsql", "Dapper"
		]);
	}

	[Theory]
	[InlineData("Bancho")]
	[InlineData("Irc")]
	public void Protocol_DependsOnNoOtherProject(string protocol)
	{
		var assembly = protocol == "Bancho" ? ProtocolBancho : ProtocolIrc;
		AssertNoDependency(assembly, ["Basil.Domain", .. ModuleRoots, Host, Bot]);
	}

	[Fact]
	public void ApplicationPersistence_DoesNotSeeTheLayersAboveItInfrastructureOrTheHosts()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Persistence)
			.ShouldNot().HaveDependencyOnAny(
			[
				.. ObjectNamespaces(), .. ReactionNamespaces(), .. CapabilityNamespaces(), Storage,
				.. InfrastructureNamespaces(), Host, Protocol, Bot
			]).GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ApplicationCapabilities_DoNotSeeHandlersOrWorkers()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Capabilities)
			.ShouldNot().HaveDependencyOnAny(ReactionNamespaces()).GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ApplicationCapabilities_PublicTypesArePortsDataOrScopedCapabilities()
	{
		var publicTypes = ModuleAssemblies.Applications.SelectMany(assembly => assembly.GetTypes())
			.Where(type => type is { IsPublic: true, IsNested: false } &&
			               type.Namespace is not null &&
			               Regex.IsMatch(type.Namespace, Capabilities))
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

		// A Scoped capability is the object's behaviour as one connection may do it; it is public by design.
		var logic = publicTypes.Where(type => !type.IsInterface && !exchanged.Contains(type) &&
		                                      !type.Name.StartsWith("Scoped", StringComparison.Ordinal))
			.Select(type => type.Name)
			.ToList();

		Assert.True(logic.Count == 0, "Public logic types that are neither ports, port data nor Scoped capabilities: " +
		                              string.Join(", ", logic));
	}

	[Fact]
	public void ApplicationScopedTypes_LiveInCapabilities()
	{
		var scopedTypes = ModuleAssemblies.Applications.SelectMany(assembly => assembly.GetTypes())
			.Where(type =>
				type is { IsPublic: true, IsNested: false } && type.Name.StartsWith("Scoped", StringComparison.Ordinal)
				                                            && type.Name != "Scoped")
			.ToList();
		var misplaced = scopedTypes.Where(type => type.Namespace is null ||
		                                          !Regex.IsMatch(type.Namespace,
			                                          Capabilities))
			.Select(type => type.FullName).ToList();

		Assert.NotEmpty(scopedTypes);
		Assert.Empty(misplaced);
	}

	[Fact]
	public void ApplicationCapabilities_DoDependOnPersistence_PositiveControl()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Capabilities)
			.Should().NotHaveDependencyOnAny(PersistenceNamespaces()).GetResult();

		Assert.False(result.IsSuccessful);
	}

	[Fact]
	public void ApplicationPersistence_DoesDependOnDomain_PositiveControl()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Persistence)
			.Should().NotHaveDependencyOnAny("Basil.Domain").GetResult();

		Assert.False(result.IsSuccessful);
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
				["Basil.Infrastructure.Common", .. InfrastructureNamespaces(), Host, Protocol, Bot]);
	}

	[Fact]
	public void ApplicationObjects_DoDependOnPersistence_PositiveControl()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Objects)
			.Should().NotHaveDependencyOnAny(PersistenceNamespaces()).GetResult();

		Assert.False(result.IsSuccessful);
	}

	[Fact]
	public void InfrastructureProjects_OutsideTheirWorkersFolderDoNotSeeApplicationObjectsHostsOrBot()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Infrastructures).That()
			.DoNotResideInNamespaceMatching(InfrastructureWorkers).ShouldNot()
			.HaveDependencyOnAny([.. ObjectNamespaces(), Host, Protocol, Bot]).GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void InfrastructureProjects_OutsideWorkers_DoDependOnApplicationPersistence_PositiveControl()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Infrastructures).That()
			.DoNotResideInNamespaceMatching(InfrastructureWorkers)
			.Should().NotHaveDependencyOnAny(PersistenceNamespaces()).GetResult();

		Assert.False(result.IsSuccessful);
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
			AssertNoDependency(assembly, [.. others]);
		}
	}

	[Fact]
	public void InfrastructureWorkers_DoNotSeeCapabilitiesOrTheHosts()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Infrastructures).That()
			.ResideInNamespaceMatching(InfrastructureWorkers).ShouldNot()
			.HaveDependencyOnAny([.. CapabilityNamespaces(), Host, Protocol, Bot]).GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void InfrastructureWorkers_DoDependOnApplicationStorage_PositiveControl()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Infrastructures).That()
			.ResideInNamespaceMatching(InfrastructureWorkers).Should().NotHaveDependencyOnAny(StorageNamespaces())
			.GetResult();

		Assert.False(result.IsSuccessful);
	}

	[Fact]
	public void Bot_DoesNotSeeInfrastructureHostsOrUnscopedCapabilities()
	{
		AssertNoDependency(BotAssembly,
		[
			"Basil.Infrastructure.Common", .. InfrastructureNamespaces(), .. NonScopedCapabilityTypeNames(), Host,
			Protocol
		]);
	}

	[Fact]
	public void Bot_DoesDependOnScopedCapabilities_PositiveControl()
	{
		var scopedRounds = ModuleAssemblies.Applications.SelectMany(assembly => assembly.GetTypes())
			.Single(type => type.Name == "ScopedRoomRounds");
		var result = Types.InAssembly(BotAssembly).Should().NotHaveDependencyOnAny(scopedRounds.FullName!).GetResult();

		Assert.False(result.IsSuccessful);
	}

	[Fact]
	public void ApplicationObjects_HasNoSessionNamedType()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Objects)
			.And().ArePublic().Should().NotHaveNameMatching(".*Session$").GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void ApplicationObjects_HasNoPublicServiceContract()
	{
		var result = Types.InAssemblies(ModuleAssemblies.Applications).That().ResideInNamespaceMatching(Objects)
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

	private static string[] NonScopedCapabilityTypeNames()
	{
		return ModuleAssemblies.Applications.SelectMany(assembly => assembly.GetTypes())
			.Where(type => type is { IsPublic: true, IsNested: false } &&
			               !type.Name.StartsWith("Scoped", StringComparison.Ordinal) &&
			               type.Namespace is not null &&
			               Regex.IsMatch(type.Namespace, Capabilities))
			.Select(type => type.FullName!)
			.ToArray();
	}

	/// <summary>The folders that hold live objects and the registries that keep them.</summary>
	private static string[] ObjectNamespaces()
	{
		return
		[
			.. ModuleAssemblies.Modules.Select(module => $"Basil.Application.{module}.Objects"),
			.. ModuleAssemblies.Modules.Select(module => $"Basil.Application.{module}.Registry")
		];
	}

	/// <summary>The folders that react to events and run background work.</summary>
	private static string[] ReactionNamespaces()
	{
		return
		[
			.. ModuleAssemblies.Modules.Select(module => $"Basil.Application.{module}.Handlers"),
			.. ModuleAssemblies.Modules.Select(module => $"Basil.Application.{module}.Workers")
		];
	}

	private static string[] InfrastructureNamespaces()
	{
		return ModuleAssemblies.Modules.Select(module => $"Basil.Infrastructure.{module}").ToArray();
	}

	private static string[] StorageNamespaces()
	{
		return ModuleAssemblies.Modules.Select(module => $"Basil.Application.{module}.Storage").ToArray();
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