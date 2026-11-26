using Basil.Tests.Shared.Environment.Monitor;
using Basil.Tests.Shared.Workload.Personas;
using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests;

/// <summary>Translates a target concurrent session count <c>N</c> into copy counts per persona.</summary>
public readonly record struct PersonaAllocation(
	int MatchRooms,
	int FreePlayers,
	int CasualRooms,
	int Spectators,
	int IrcChatters,
	int OverlayViewers,
	int TourneyClusters,
	int BackgroundRatePerMinute,
	int ChurnRatePerMinute,
	int EdgeCaseRatePerMinute)
{
	/// <summary>
	/// Gets the expected number of concurrent sessions one match room holds: the players, the referee, the spectators,
	/// the tourney clusters (each one manager plus one child client per player) and the overlay viewers that sign in.
	/// </summary>
	/// <remarks>
	/// Players are 2, 4 or 8 with equal chance inside <c>[MinPlayers, MaxPlayers]</c>. The referee is one session over IRC
	/// or REST. Without a fixed cluster count a room has 0 to 2 clusters at random, one on average. Of the overlay viewers
	/// every second one watches <c>/v1/rooms/{id}/live</c>; it signs in as an extra session only when the referee is on
	/// IRC, which is half of the rooms unless the referee is forced. The default configuration gives about 12.8 sessions.
	/// </remarks>
	/// <param name="matchRoom">The room configuration; <see langword="null"/> means the defaults.</param>
	public static double SessionsPerMatchRoom(MatchRoomConfig? matchRoom = null)
	{
		var config = matchRoom ?? new MatchRoomConfig();
		var sizes = new[] { 2, 4, 8 }.Where(size => size >= config.MinPlayers && size <= config.MaxPlayers).ToArray();
		var players = sizes.Length == 0 ? Math.Max(2, config.MinPlayers) : sizes.Average();

		int? fixedClusters = config.TourneyClusters;
		var clusters = fixedClusters ?? 1.0;

		var liveViewers = config.OverlayViewersPerRoom / 2;
		var ircReferee = config.ForceIrcReferee is { } forced ? forced ? 1.0 : 0.0 : 0.5;

		return players + 1 + config.SpectatorsPerRoom + clusters * (1 + players) + liveViewers * ircReferee;
	}

	/// <summary>Computes a realistic live-tournament workload mix for <paramref name="targetSessions"/> concurrent sessions.</summary>
	/// <param name="targetSessions">The concurrent sessions <c>N</c> the whole mix should hold, tourney child clients included.</param>
	/// <param name="matchRoom">The room configuration the match rooms use; <see langword="null"/> means the defaults.</param>
	public static PersonaAllocation ForSessions(int targetSessions, MatchRoomConfig? matchRoom = null)
	{
		var n = Math.Max(10, targetSessions);
		var matchRooms = Math.Max(1, (int)Math.Round(n * 0.55 / SessionsPerMatchRoom(matchRoom)));
		var freePlayers = Math.Max(1, (int)Math.Round(n * 0.20));
		var casualRooms = Math.Max(1, (int)Math.Round(n * 0.08 / 2.0));
		var spectators = Math.Max(1, (int)Math.Round(n * 0.06));
		var ircChatters = Math.Max(1, (int)Math.Round(n * 0.05));
		var overlays = Math.Max(1, (int)Math.Round(n * 0.03));
		var tourney = Math.Max(1, (int)Math.Round(n * 0.03 / 3.0));

		var bgRate = Math.Max(2, n / 15);
		var churnRate = Math.Max(2, n / 25);
		var edgeRate = Math.Max(1, n / 60);

		return new PersonaAllocation(matchRooms, freePlayers, casualRooms, spectators, ircChatters, overlays, tourney,
			bgRate, churnRate, edgeRate);
	}
}

/// <summary>Builds NBomber scenarios for all ten personas with caller-supplied load shapes and optional SLO thresholds.</summary>
public static class ScenarioCatalog
{
	/// <summary>Creates the complete live-tournament suite of scenarios using the given closed/open simulation builders.</summary>
	public static IReadOnlyList<ScenarioProps> BuildFullSuite(
		WorkloadContext workload,
		DiagnosticsMonitor? monitor,
		Func<Func<PersonaAllocation, int>, LoadSimulation[]> closedSimulations,
		Func<Func<PersonaAllocation, int>, LoadSimulation[]> openSimulations,
		bool enforceSlo,
		TimeSpan? warmUp = null,
		MatchRoomConfig? matchRoomConfig = null)
	{
		var matchRoom = new MatchRoomPersona(workload, matchRoomConfig);
		var freePlayer = new FreePlayerPersona(workload);
		var casualRoom = new CasualRoomPersona(workload);
		var spectator = new SpectatorPersona(workload);
		var tourney = new TourneyClusterPersona(workload);
		var overlay = new OverlayViewerPersona(workload);
		var irc = new IrcChatterPersona(workload);
		var background = new BackgroundPersona(workload);
		var churn = new ChurnPersona(workload);
		var edgeCases = new EdgeCasesPersona(workload);

		ScenarioProps Configure(ScenarioProps s, LoadSimulation[] sims, Threshold[] thresholds, Func<Task> shutdown,
			bool attachMonitor = false)
		{
			s = s.WithClean(_ => shutdown());
			s = warmUp is { } w && w > TimeSpan.Zero ? s.WithWarmUpDuration(w) : s.WithoutWarmUp();
			s = s.WithLoadSimulations(sims);
			if (enforceSlo && thresholds.Length > 0) s = s.WithThresholds(thresholds);
			if (attachMonitor && monitor is not null)
			{
				s = s.WithInit(ctx =>
				{
					monitor.RegisterMetrics(ctx);
					monitor.Start();
					return Task.CompletedTask;
				});
			}

			return s;
		}

		return
		[
			Configure(
				Scenario.Create("match_room", async ctx =>
				{
					await matchRoom.RunCycleAsync(ctx);
					return Response.Ok();
				}),
				closedSimulations(a => a.MatchRooms),
				SloThresholds.ForScenario(includePoll: true, includeSubmit: true, includeRest: true,
					includeLogin: true, includeServerCpu: monitor is not null),
				matchRoom.ShutdownAsync,
				attachMonitor: true),

			Configure(
				Scenario.Create("free_player", async ctx =>
				{
					await freePlayer.RunCycleAsync(ctx);
					return Response.Ok();
				}),
				closedSimulations(a => a.FreePlayers),
				SloThresholds.ForScenario(includePoll: true, includeSubmit: true, includeGetScores: true,
					includeLogin: true),
				freePlayer.ShutdownAsync),

			Configure(
				Scenario.Create("casual_room", async ctx =>
				{
					await casualRoom.RunCycleAsync(ctx);
					return Response.Ok();
				}),
				closedSimulations(a => a.CasualRooms),
				SloThresholds.ForScenario(includePoll: true, includeSubmit: true, includeLogin: true),
				casualRoom.ShutdownAsync),

			Configure(
				Scenario.Create("spectator", async ctx =>
				{
					await spectator.RunCycleAsync(ctx);
					return Response.Ok();
				}),
				closedSimulations(a => a.Spectators),
				SloThresholds.ForScenario(includePoll: true, includeLogin: true),
				spectator.ShutdownAsync),

			Configure(
				Scenario.Create("tourney_cluster", async ctx =>
				{
					await tourney.RunCycleAsync(ctx);
					return Response.Ok();
				}),
				closedSimulations(a => a.TourneyClusters),
				SloThresholds.ForScenario(includePoll: true, includeLogin: true),
				tourney.ShutdownAsync),

			Configure(
				Scenario.Create("overlay_viewer", async ctx =>
				{
					await overlay.RunCycleAsync(ctx);
					return Response.Ok();
				}),
				closedSimulations(a => a.OverlayViewers),
				SloThresholds.ForScenario(includeLogin: true),
				overlay.ShutdownAsync),

			Configure(
				Scenario.Create("irc_chatter", async ctx =>
				{
					await irc.RunCycleAsync(ctx);
					return Response.Ok();
				}),
				closedSimulations(a => a.IrcChatters),
				SloThresholds.ForScenario(),
				irc.ShutdownAsync),

			Configure(
				Scenario.Create("background", async ctx =>
				{
					await background.RunCycleAsync(ctx);
					return Response.Ok();
				}),
				openSimulations(a => a.BackgroundRatePerMinute),
				SloThresholds.ForScenario(includeGetScores: true, includeRest: true),
				background.ShutdownAsync),

			Configure(
				Scenario.Create("churn", async ctx =>
				{
					await churn.RunCycleAsync(ctx);
					return Response.Ok();
				}),
				openSimulations(a => a.ChurnRatePerMinute),
				SloThresholds.ForScenario(includeLogin: true, includeRest: true),
				churn.ShutdownAsync),

			Configure(
				Scenario.Create("edge_cases", async ctx =>
				{
					await edgeCases.RunCycleAsync(ctx);
					return Response.Ok();
				}),
				openSimulations(a => a.EdgeCaseRatePerMinute),
				SloThresholds.ForScenario(),
				edgeCases.ShutdownAsync)
		];
	}
}
