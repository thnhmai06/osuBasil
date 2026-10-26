using System.Reflection;
using NetArchTest.Rules;

namespace Basil.ArchitectureTests;

/// <summary>
///     The module graph of Application.Runtime, measured on its namespaces (plan <c>module-events-plan-20261027.md</c>):
///     every area may use the shared kernel (<c>Common</c>, <c>Events</c>); beyond that <c>Sessions -&gt; Users</c>,
///     <c>Anticheat -&gt; Sessions, Users</c>, <c>Scores -&gt; Sessions, Users, Beatmaps</c>,
///     <c>
///         Multiplayer -&gt; Sessions,
///         Users, Beatmaps, Scores
///     </c>
///     , <c>Chat -&gt; Sessions, Users, Multiplayer</c> and <c>Servers</c> on top of all of
///     them; <c>Users</c> and <c>Beatmaps</c> depend on nothing. The target is reached in steps, so the edges that are
///     still wrong are listed as debt: a new edge outside the target and the debt fails the test, and a debt edge that
///     is gone fails it too, so the list can only shrink.
/// </summary>
public class ModuleGraphTests
{
	private static readonly string[] Areas =
	[
		"Anticheat", "Beatmaps", "Chat", "Multiplayer", "Scores", "Servers", "Sessions", "Users"
	];

	/// <summary>Namespaces every area may use without it being an edge.</summary>
	private static readonly string[] SharedKernel = ["Common", "Events"];

	private static readonly HashSet<(string From, string To)> Target =
	[
		("Sessions", "Users"),
		("Anticheat", "Sessions"), ("Anticheat", "Users"),
		("Scores", "Sessions"), ("Scores", "Users"), ("Scores", "Beatmaps"),
		("Multiplayer", "Sessions"), ("Multiplayer", "Users"), ("Multiplayer", "Beatmaps"), ("Multiplayer", "Scores"),
		("Chat", "Sessions"), ("Chat", "Users"), ("Chat", "Multiplayer"),
		("Servers", "Users"), ("Servers", "Sessions"), ("Servers", "Anticheat"), ("Servers", "Beatmaps"),
		("Servers", "Scores"), ("Servers", "Multiplayer"), ("Servers", "Chat")
	];

	/// <summary>Edges that exist today and are not in the target; each one is to be removed.</summary>
	private static readonly HashSet<(string From, string To)> Debt =
	[
		// The room owns its channel: Chat should react to room events instead.
		("Multiplayer", "Chat"),
		// A connection joins channels and sets its spectators through sessions: spectator channels belong to Chat.
		("Sessions", "Chat"),
		// The online user holds its connections and reaches channels and rooms: the online user belongs to Sessions.
		("Users", "Chat"), ("Users", "Multiplayer"), ("Users", "Sessions")
	];

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
	public void TheMeasureSeesAnEdgeItShould()
	{
		Assert.Contains(("Multiplayer", "Sessions"), Measure());
	}
}