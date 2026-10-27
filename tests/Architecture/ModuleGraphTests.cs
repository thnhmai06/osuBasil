using System.Xml.Linq;

namespace Basil.ArchitectureTests;

/// <summary>
///     The project graph of the server: every module is a pair of projects whose
///     references the compiler enforces, so this test reads the <c>ProjectReference</c> items of every project under
///     <c>src</c> (except the hosts, which are migrated separately) and fails on an edge outside the allowed graph or on a
///     cycle. Domain is shared by every project; the module Application projects follow
///     <c>Users -&gt; Auth, Content</c>, <c>Anticheat -&gt; Users</c>, <c>Scores -&gt; Users, Beatmaps</c>,
///     <c>Multiplayer -&gt; Users, Beatmaps, Content, Scores</c>, <c>Chat -&gt; Users, Multiplayer</c> and
///     <c>Servers</c> on top of all of them; a module Infrastructure project references only its own Application project
///     and the Common projects; <c>Composition</c> and <c>Bot</c> sit above every module and nothing references them.
/// </summary>
public class ModuleGraphTests
{
	private static readonly string[] Modules =
		["Anticheat", "Auth", "Beatmaps", "Chat", "Content", "Multiplayer", "Scores", "Servers", "Users"];

	private static readonly Dictionary<string, string[]> ModuleDependencies = new()
	{
		["Auth"] = [],
		["Beatmaps"] = [],
		["Content"] = [],
		["Users"] = ["Auth", "Content"],
		["Anticheat"] = ["Users"],
		["Scores"] = ["Users", "Beatmaps"],
		["Multiplayer"] = ["Users", "Beatmaps", "Content", "Scores"],
		["Chat"] = ["Users", "Multiplayer"],
		["Servers"] = ["Anticheat", "Auth", "Beatmaps", "Chat", "Content", "Multiplayer", "Scores", "Users"]
	};

	[Fact]
	public void EveryProjectReferenceIsInsideTheAllowedGraph()
	{
		var unexpected = ProjectEdges().Where(edge => !IsAllowed(edge.From, edge.To))
			.Select(edge => $"{edge.From} -> {edge.To}");

		Assert.Empty(unexpected);
	}

	[Fact]
	public void NoProjectIsLeftOutOfTheGraph()
	{
		var unknown = ProjectFiles().Select(Name).Where(name => !IsKnown(name));

		Assert.Empty(unknown);
	}

	[Fact]
	public void NoCycleJoinsTheProjects()
	{
		Assert.Empty(FindCycles(ProjectEdges()));
	}

	[Fact]
	public void TheMeasureSeesAnEdgeItShould()
	{
		Assert.Contains(("Basil.Infrastructure.Users", "Basil.Application.Users"), ProjectEdges());
		Assert.Contains(("Basil.Application.Users", "Basil.Application.Auth"), ProjectEdges());
	}

	[Fact]
	public void TheRulesRefuseAnEdgeTheyShould()
	{
		Assert.False(IsAllowed("Basil.Application.Auth", "Basil.Application.Users"));
		Assert.False(IsAllowed("Basil.Infrastructure.Scores", "Basil.Application.Users"));
		Assert.False(IsAllowed("Basil.Application.Users", "Basil.Composition"));
		Assert.False(IsAllowed("Basil.Domain", "Basil.Application.Common"));
		Assert.True(IsAllowed("Basil.Application.Multiplayer", "Basil.Application.Scores"));
	}

	[Fact]
	public void TheCycleScanFindsACycleItShould()
	{
		var edges = new[] { ("One", "Two"), ("Two", "Three"), ("Three", "One") };

		Assert.NotEmpty(FindCycles(edges));
	}

	private static bool IsKnown(string name)
	{
		return name is "Basil.Domain" or "Basil.Application.Common" or "Basil.Infrastructure.Common"
			or "Basil.Composition" or "Basil.Bot" or "Basil.Protocol.Bancho" or "Basil.Protocol.Irc"
			|| Modules.Any(module => name == $"Basil.Application.{module}" ||
			                         (name == $"Basil.Infrastructure.{module}" && module is not ("Anticheat" or "Servers")));
	}

	private static bool IsAllowed(string from, string to)
	{
		if (from == "Basil.Domain") return false;
		if (to == "Basil.Domain") return true;
		if (from == "Basil.Application.Common") return false;
		if (from == "Basil.Infrastructure.Common") return to == "Basil.Application.Common";
		if (to == "Basil.Application.Common") return true;
		if (from == "Basil.Composition") return IsModuleProject(to) || to == "Basil.Infrastructure.Common";
		if (from == "Basil.Bot")
			return to.StartsWith("Basil.Application.", StringComparison.Ordinal) && IsModuleProject(to);
		if (from is "Basil.Protocol.Bancho" or "Basil.Protocol.Irc") return false;

		var parts = from.Split('.');
		if (parts.Length != 3) return false;
		var module = parts[2];
		if (!ModuleDependencies.TryGetValue(module, out var dependencies)) return false;
		if (parts[1] == "Infrastructure")
			return to == $"Basil.Application.{module}" || to == "Basil.Infrastructure.Common";

		return dependencies.Any(dependency => to == $"Basil.Application.{dependency}");
	}

	private static bool IsModuleProject(string name)
	{
		var parts = name.Split('.');
		return parts.Length == 3 && Modules.Contains(parts[2]) && parts[1] is "Application" or "Infrastructure";
	}

	private static string[] ProjectFiles()
	{
		var source = Path.Combine(RepositoryRoot(), "src");
		var hosts = Path.Combine(source, "Hosts");

		return Directory.GetFiles(source, "*.csproj", SearchOption.AllDirectories)
			.Where(project => !IsUnder(project, hosts) && !project.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
			.ToArray();
	}

	private static string Name(string project)
	{
		return Path.GetFileNameWithoutExtension(project);
	}

	private static (string From, string To)[] ProjectEdges()
	{
		return ProjectFiles().SelectMany(project =>
				XDocument.Load(project).Descendants()
					.Where(element => element.Name.LocalName == "ProjectReference")
					.Select(element => (From: Name(project),
						To: Path.GetFileNameWithoutExtension(
							element.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))))
			.ToArray();
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException("Basil.slnx was not found.");
	}

	private static bool IsUnder(string path, string directory)
	{
		return !Path.GetRelativePath(directory, path).StartsWith("..", StringComparison.Ordinal);
	}

	/// <summary>Finds the cycles among a set of edges, each reported as the projects it passes through.</summary>
	private static List<string> FindCycles(IEnumerable<(string From, string To)> edges)
	{
		var adjacency = edges.GroupBy(edge => edge.From)
			.ToDictionary(group => group.Key, group => group.Select(edge => edge.To).ToArray());
		var visited = new HashSet<string>();
		var onPath = new List<string>();
		var cycles = new List<string>();

		foreach (var project in adjacency.Keys)
			Visit(project);

		return cycles;

		void Visit(string project)
		{
			if (onPath.Contains(project))
			{
				cycles.Add(string.Join(" -> ", onPath[onPath.IndexOf(project)..].Append(project)));
				return;
			}

			if (!visited.Add(project)) return;

			onPath.Add(project);
			foreach (var next in adjacency.GetValueOrDefault(project, []))
				Visit(next);
			onPath.RemoveAt(onPath.Count - 1);
		}
	}
}