using System.Reflection;
using NetArchTest.Rules;

namespace Basil.ArchitectureTests;

/// <summary>
///     The module graph of Application.Runtime, measured on its namespaces (plan <c>module-events-plan-20261027.md</c>):
///     every area may use the shared kernel (<c>Common</c>, <c>Events</c>); beyond that <c>Anticheat -&gt; Connections,
///     Users</c>, <c>Scores -&gt; Connections, Users, Beatmaps</c>, <c>Multiplayer -&gt; Connections, Users, Beatmaps,
///     Scores</c>, <c>Chat -&gt; Connections, Users, Multiplayer</c> and <c>Servers</c> on top of all of them;
///     <c>Beatmaps</c> depends on nothing, and <c>Users</c> and <c>Connections</c> are one cluster that uses only each
///     other. The target is reached in steps, so an edge that is still wrong would be listed as debt: a new edge outside
///     the target and the debt fails the test, and a debt edge that is gone fails it too, so the list can only shrink.
/// </summary>
public class ModuleGraphTests
{
	private static readonly string[] Areas =
	[
		"Anticheat", "Beatmaps", "Chat", "Connections", "Multiplayer", "Scores", "Servers", "Users"
	];

	/// <summary>Namespaces every area may use without it being an edge.</summary>
	private static readonly string[] SharedKernel = ["Common", "Events"];

	/// <summary>The one pair of areas that may reference each other: a user holds its connections, a connection its user.</summary>
	private static readonly (string From, string To) UserConnectionCycle = ("Users", "Connections");

	private static readonly HashSet<(string From, string To)> Target =
	[
		("Anticheat", "Connections"), ("Anticheat", "Users"),
		("Scores", "Connections"), ("Scores", "Users"), ("Scores", "Beatmaps"),
		("Multiplayer", "Connections"), ("Multiplayer", "Users"), ("Multiplayer", "Beatmaps"),
		("Multiplayer", "Scores"),
		("Chat", "Connections"), ("Chat", "Users"), ("Chat", "Multiplayer"),
		("Servers", "Connections"), ("Servers", "Users"), ("Servers", "Anticheat"), ("Servers", "Beatmaps"),
		("Servers", "Scores"), ("Servers", "Multiplayer"), ("Servers", "Chat"),
		UserConnectionCycle, (UserConnectionCycle.To, UserConnectionCycle.From)
	];

	/// <summary>Edges that exist today and are not in the target; there is no debt left.</summary>
	private static readonly HashSet<(string From, string To)> Debt = [];

	private static readonly Assembly Runtime = Assembly.Load("Basil.Application.Runtime");

	private static HashSet<(string From, string To)> Measure()
	{
		var edges = new HashSet<(string From, string To)>();
		foreach (var from in Areas)
		foreach (var to in Areas)
		{
			if (from == to) continue;
			var result = Types.InAssembly(Runtime).That()
				.ResideInNamespaceStartingWith($"Basil.Application.Runtime.{from}")
				.ShouldNot().HaveDependencyOn($"Basil.Application.Runtime.{to}").GetResult();
			if (!result.IsSuccessful) edges.Add((from, to));
		}

		return edges;
	}

	[Fact]
	public void NoAreaDependsOnAnotherOneOutsideTheTargetAndTheDebt()
	{
		var unexpected = Measure().Where(edge => !Target.Contains(edge) && !Debt.Contains(edge))
			.Select(edge => $"{edge.From} -> {edge.To}");

		Assert.Empty(unexpected);
	}

	[Fact]
	public void EveryDebtEdgeStillExists()
	{
		var measured = Measure();
		var gone = Debt.Where(edge => !measured.Contains(edge)).Select(edge => $"{edge.From} -> {edge.To}");

		Assert.Empty(gone);
	}

	[Fact]
	public void TheSharedKernelDependsOnNoArea()
	{
		var edges = SharedKernel.SelectMany(kernel => Areas.Select(area => (Kernel: kernel, Area: area)))
			.Where(pair => !Types.InAssembly(Runtime).That()
				.ResideInNamespaceStartingWith($"Basil.Application.Runtime.{pair.Kernel}")
				.ShouldNot().HaveDependencyOn($"Basil.Application.Runtime.{pair.Area}").GetResult().IsSuccessful)
			.Select(pair => $"{pair.Kernel} -> {pair.Area}");

		Assert.Empty(edges);
	}

	[Fact]
	public void NoCycleJoinsAreasBeyondTheUsersAndConnectionsPair()
	{
		var edges = Measure()
			.Where(edge => edge != UserConnectionCycle && edge != (UserConnectionCycle.To, UserConnectionCycle.From));

		Assert.Empty(FindCycles(edges));
	}

	[Fact]
	public void TheCycleScanFindsACycleItShould()
	{
		var edges = new[] { ("One", "Two"), ("Two", "Three"), ("Three", "One") };

		Assert.NotEmpty(FindCycles(edges));
	}

	[Fact]
	public void TheMeasureSeesAnEdgeItShould()
	{
		Assert.Contains(("Multiplayer", "Connections"), Measure());
	}

	/// <summary>Finds the cycles among a set of edges, each reported as the areas it passes through.</summary>
	private static List<string> FindCycles(IEnumerable<(string From, string To)> edges)
	{
		var adjacency = edges.GroupBy(edge => edge.From)
			.ToDictionary(group => group.Key, group => group.Select(edge => edge.To).ToArray());
		var visited = new HashSet<string>();
		var onPath = new List<string>();
		var cycles = new List<string>();

		foreach (var area in adjacency.Keys)
			Visit(area);

		return cycles;

		void Visit(string area)
		{
			if (onPath.Contains(area))
			{
				cycles.Add(string.Join(" -> ", onPath[onPath.IndexOf(area)..].Append(area)));
				return;
			}

			if (!visited.Add(area)) return;

			onPath.Add(area);
			foreach (var next in adjacency.GetValueOrDefault(area, []))
				Visit(next);
			onPath.RemoveAt(onPath.Count - 1);
		}
	}
}