using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Basil.Application.Users.Persistence;

namespace Basil.ArchitectureTests;

/// <summary>
///     The project graph of the hosts: every transport shares <c>Basil.Host.Shared</c> and the transports stay
///     independent of each other; the API and the console also stay independent of the Stable wire format. A transport
///     that breaks one of these edges is caught here, before the solution compiles against a wrong reference.
/// </summary>
public partial class HostGraphTests
{
	private const string Shared = "Basil.Host.Shared";
	private const string ProtocolStable = "Basil.Protocol.Stable";

	private static readonly string[] Transports =
		["Basil.Host.Stable", "Basil.Host.Irc", "Basil.Host.Rest", "Basil.Host.Cli"];

	[Fact]
	public void TransportSharedReference_Passes()
	{
		Assert.Empty(MissingShared(ProjectEdges()));
		// The control: a synthetic graph without the edge is reported.
		Assert.NotEmpty(MissingShared([("Basil.Host.Irc", "Basil.Host.Stable")]));
	}

	[Fact]
	public void TransportPeerReference_Fails()
	{
		var forbidden = ProjectEdges().Where(edge => ReferEachOther(edge.From, edge.To))
			.Select(edge => $"{edge.From} -> {edge.To}");

		Assert.Empty(forbidden);
	}

	[Fact]
	public void CliStableProtocol_Fails()
	{
		var forbidden = ProjectEdges().Where(edge => NeedsStableProtocol(edge.From, edge.To))
			.Select(edge => $"{edge.From} -> {edge.To}");

		Assert.Empty(forbidden);
	}

	[Fact]
	public void MeasureSeesEdge_PositiveControl()
	{
		Assert.Contains(("Basil.Host.Stable", ProtocolStable), ProjectEdges());
		Assert.Contains(("Basil.Host.Stable", Shared), ProjectEdges());
	}

	[Fact]
	public void RulesRefuseEdge_PositiveControl()
	{
		Assert.True(ReferEachOther("Basil.Host.Stable", "Basil.Host.Irc"));
		Assert.True(ReferEachOther("Basil.Host.Irc", "Basil.Host.Rest"));
		Assert.True(NeedsStableProtocol("Basil.Host.Rest", ProtocolStable));
		Assert.True(NeedsStableProtocol("Basil.Host.Cli", ProtocolStable));
		Assert.False(NeedsStableProtocol("Basil.Host.Stable", ProtocolStable));
		Assert.False(NeedsStableProtocol("Basil.Host.Irc", ProtocolStable));
		Assert.False(ReferEachOther("Basil.Host.Stable", Shared));
	}

	[Fact]
	public void HostStoreField_Absent()
	{
		var types = HostAssemblies().SelectMany(LoadableTypes).ToList();

		Assert.Empty(Offenders(types));
		Assert.NotEmpty(types);
	}

	[Fact]
	public void ProviderStoreLookup_Absent()
	{
		var root = Path.Combine(RepositoryRoot(), "src", "Hosts");
		var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
			               !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
			.Where(file => ResolvesAStore(File.ReadAllText(file)))
			.Select(file => Path.GetRelativePath(root, file));

		Assert.Empty(offenders);
	}

	[Fact]
	public void DetectorSeesStore_PositiveControl()
	{
		Assert.Equal([typeof(HostWithARepository).FullName], Offenders([typeof(HostWithARepository)]));
		Assert.Equal([typeof(HostWithAStaticRouteHandler).FullName], Offenders([typeof(HostWithAStaticRouteHandler)]));
		Assert.True(ResolvesAStore("var users = services.GetRequiredService<IUserRepository>();"));
		Assert.True(ResolvesAStore("call.Get<IBeatmapsetStorage>()"));
		Assert.False(ResolvesAStore("services.GetRequiredService<ISender>()"));
	}

	private static string[] HostAssemblies()
	{
		return ["Basil.Host.Rest", "Basil.Host.Stable", "Basil.Host.Irc", "Basil.Host.Cli", Shared];
	}

	private static IEnumerable<Type> LoadableTypes(string assemblyName)
	{
		var assembly = Assembly.Load(assemblyName);
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types.OfType<Type>();
		}
	}

	/// <summary>Names the types that hold a repository or a storage in a constructor, a field or a method parameter.</summary>
	private static List<string?> Offenders(IEnumerable<Type> types)
	{
		const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
		                         BindingFlags.Static | BindingFlags.DeclaredOnly;
		return
		[
			.. types.Where(type =>
					type.GetConstructors(all).SelectMany(constructor => constructor.GetParameters())
						.Select(parameter => parameter.ParameterType)
						.Concat(type.GetFields(all).Select(field => field.FieldType))
						.Concat(type.GetMethods(all).SelectMany(method => method.GetParameters())
							.Select(parameter => parameter.ParameterType))
						.Any(IsStore))
				.Select(type => type.FullName)
		];
	}

	private static bool IsStore(Type type)
	{
		return (type.IsGenericType ? type.GetGenericArguments().Append(type) : [type])
			.Any(candidate => candidate.Assembly.GetName().Name!.StartsWith("Basil.Application.") &&
			                  (candidate.Name.EndsWith("Repository") || candidate.Name.EndsWith("Storage")));
	}

	private static bool ResolvesAStore(string source)
	{
		return StoreLookup().IsMatch(source);
	}

	[GeneratedRegex(@"\b(?:Get|GetService|GetRequiredService)<I\w+(?:Repository|Storage)>")]
	private static partial Regex StoreLookup();

	private static IEnumerable<string> MissingShared(IEnumerable<(string From, string To)> edges)
	{
		var present = edges.ToHashSet();
		return Transports.Where(transport => !present.Contains((transport, Shared)));
	}

	private static bool ReferEachOther(string from, string to)
	{
		return Transports.Contains(from) && Transports.Contains(to);
	}

	private static bool NeedsStableProtocol(string from, string to)
	{
		return to == ProtocolStable && from is "Basil.Host.Rest" or "Basil.Host.Cli";
	}

	private static (string From, string To)[] ProjectEdges()
	{
		return Transports.SelectMany(project =>
		{
			var file = Path.Combine(RepositoryRoot(), "src", "Hosts", project["Basil.Host.".Length..],
				project + ".csproj");
			return XDocument.Load(file).Descendants()
				.Where(element => element.Name.LocalName == "ProjectReference")
				.Select(element => (From: project,
					To: Path.GetFileNameWithoutExtension(
						element.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))));
		}).ToArray();
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException("Basil.slnx was not found.");
	}

	private sealed class HostWithARepository(IUserRepository users)
	{
		public IUserRepository Users { get; } = users;
	}

	private sealed class HostWithAStaticRouteHandler
	{
		public static Task Handle(IUserRepository users)
		{
			return Task.CompletedTask;
		}
	}
}