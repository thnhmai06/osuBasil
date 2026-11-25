using System.Text.RegularExpressions;
using Basil.Domain.Spectating;
using Basil.Protocol.Irc.Models;
using Basil.Tests.Shared.Clients.Irc;
using Basil.Tests.Shared.Clients.OsuWeb;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Configuration of a simulated tournament room.</summary>
/// <param name="MinPlayers">Minimum players per room (2 = 1v1).</param>
/// <param name="MaxPlayers">Maximum players per room (8 = 4v4).</param>
/// <param name="RoundsPerCycle">Rounds played before the room closes.</param>
/// <param name="TourneyClusters">Number of osu!tourney clusters attached to the room (0 to 2).</param>
/// <param name="SpectatorsPerRoom">In-game spectators watching players in this room.</param>
/// <param name="OverlayViewersPerRoom">SSE overlay viewers subscribed to the room and match report.</param>
/// <param name="ForceIrcReferee">When set, forces IRC or REST referee instead of alternating 50/50.</param>
public sealed record MatchRoomConfig(
	int MinPlayers = 2,
	int MaxPlayers = 8,
	int RoundsPerCycle = 2,
	int TourneyClusters = 1,
	int SpectatorsPerRoom = 1,
	int OverlayViewersPerRoom = 2,
	bool? ForceIrcReferee = null);

/// <summary>Simulates a live tournament room: 2–8 players, one IRC or REST referee, 0–2 tourney clusters, spectators and SSE overlay viewers.</summary>
public sealed partial class MatchRoomPersona(WorkloadContext workload, MatchRoomConfig? config = null)
{
	private readonly MatchRoomConfig _config = config ?? new MatchRoomConfig();

	[GeneratedRegex(@"Created room #(\d+)", RegexOptions.CultureInvariant)]
	private static partial Regex CreatedRoomRegex();

	/// <summary>Runs one complete tournament room cycle.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName,
			context.ScenarioInfo.InstanceNumber, context.InvocationNumber);

		var playerCount = PickTeamSize(random, _config.MinPlayers, _config.MaxPlayers);
		var useIrcRef = _config.ForceIrcReferee ?? (context.ScenarioInfo.InstanceNumber % 2 == 0);

		if (!workload.Accounts.TryAcquire(AccountRole.Referee, out var refLease))
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		if (!workload.Accounts.TryAcquireMany(AccountRole.Player, playerCount, out var playerLeases))
		{
			refLease.Dispose();
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		var spectatorLeases = new List<AccountLease>();
		for (var i = 0; i < _config.SpectatorsPerRoom; i++)
			if (workload.Accounts.TryAcquire(AccountRole.Player, out var specLease))
				spectatorLeases.Add(specLease);

		var tourneyClusterLeases = new List<List<AccountLease>>();
		for (var c = 0; c < _config.TourneyClusters; c++)
		{
			// One manager + one child client per player slot.
			var clusterSize = 1 + playerCount;
			if (workload.Accounts.TryAcquireMany(AccountRole.Tourney, clusterSize, out var cluster))
				tourneyClusterLeases.Add(cluster);
		}

		var roomName = $"Tourney {context.ScenarioInfo.InstanceNumber}-{context.InvocationNumber}";
		IrcClient? ircRef = null;
		RestClient? restRef = null;
		var players = new List<StableClient>(playerCount);
		var spectators = new List<StableClient>(spectatorLeases.Count);
		var tourneyClients = new List<StableClient>();
		using var overlayStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
		var overlayTasks = new List<Task>();
		var roomId = 0;

		try
		{
			var beatmap = workload.PickBeatmap(random);

			// 1. Referee opens and configures the room.
			if (useIrcRef)
			{
				(ircRef, roomId) = await OpenIrcRoomAsync(refLease.Account, roomName, playerCount, beatmap, context, ct);
			}
			else
			{
				(restRef, roomId) = await OpenRestRoomAsync(refLease.Account, roomName, playerCount, beatmap, context, ct);
			}

			if (roomId <= 0 || ct.IsCancellationRequested) return;

			workload.World.PutRoom(new LiveRoom(roomId, roomName, string.Empty, beatmap.Id, beatmap.Md5, true, false, []));

			// 2. Players sign in and join the room concurrently.
			var joinedPlayers = await Task.WhenAll(playerLeases.Select((lease, index) =>
				JoinPlayerAsync(lease, roomId, index, playerCount, context, ct)));
			players.AddRange(joinedPlayers.Where(p => p is not null)!);
			if (players.Count < 2 || ct.IsCancellationRequested) return;

			var playerIds = players.Select(p => p.UserId).ToArray();
			workload.World.PutRoom(new LiveRoom(roomId, roomName, string.Empty, beatmap.Id, beatmap.Md5, true, false, playerIds));

			// 3. Attach tourney clusters (manager + 1 child client per player slot) and spectators.
			var auxTasks = new List<Task>();
			foreach (var cluster in tourneyClusterLeases)
			{
				var manager = new StableClient(workload.Endpoint, workload.Http, cluster[0].Identity(tourney: true));
				var children = cluster.Skip(1)
					.Select(lease => new StableClient(workload.Endpoint, workload.Http, lease.Identity(tourney: true)))
					.ToList();
				tourneyClients.Add(manager);
				tourneyClients.AddRange(children);
				auxTasks.Add(RunTourneyClusterAsync(manager, children, roomId, playerIds, context, overlayStop.Token));
			}

			foreach (var specLease in spectatorLeases)
			{
				var spec = new StableClient(workload.Endpoint, workload.Http, specLease.Identity());
				spectators.Add(spec);
				var targetId = playerIds[random.Next(playerIds.Length)];
				auxTasks.Add(RunSpectatorAsync(spec, targetId, context, overlayStop.Token));
			}

			// 4. Attach overlay SSE viewers (/v1/matches/{id} and /v1/rooms/{id}/live).
			for (var v = 0; v < _config.OverlayViewersPerRoom; v++)
			{
				var streamMatch = v % 2 == 0;
				overlayTasks.Add(RunOverlayAsync(roomId, streamMatch, restRef, context, overlayStop.Token));
			}

			// 5. Play rounds.
			for (var round = 0; round < _config.RoundsPerCycle && !ct.IsCancellationRequested; round++)
			{
				if (round > 0)
				{
					beatmap = workload.PickBeatmap(random);
					if (useIrcRef && ircRef is not null)
						await SetIrcMapAsync(ircRef, roomId, beatmap, context, ct);
					else if (restRef is not null)
						await SetRestMapAsync(restRef, roomId, beatmap, context, ct);
				}

				// Ready up players.
				await Task.WhenAll(players.Select(p => ReadyPlayerAsync(p, context, ct)));

				// In Spike mode, wait for the shared tournament clock while keeping sessions alive.
				if (workload.TournamentClock.IsEnabled)
				{
					var waitTask = workload.TournamentClock.WaitForNextWaveAsync(ct);
					while (!waitTask.IsCompleted && !ct.IsCancellationRequested)
					{
						await Task.WhenAll(players.Select(p => workload.PollStableAsync(p, context, ct)));
						await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
					}
				}

				if (ct.IsCancellationRequested) break;

				// Start round via referee.
				var started = useIrcRef && ircRef is not null
					? await StartIrcRoundAsync(ircRef, roomId, context, ct)
					: restRef is not null && await StartRestRoundAsync(restRef, roomId, context, ct);
				if (!started || ct.IsCancellationRequested) break;

				workload.World.PutRoom(new LiveRoom(roomId, roomName, string.Empty, beatmap.Id, beatmap.Md5, true, true, playerIds));

				// Run the round for every player concurrently.
				await Task.WhenAll(players.Select((player, slot) =>
					PlayRoundAsync(player, slot, beatmap, SeededRandom.For(workload.Seed, "round", roomId * 16 + slot, round),
						context, ct)));

				workload.World.PutRoom(new LiveRoom(roomId, roomName, string.Empty, beatmap.Id, beatmap.Md5, true, false, playerIds));
			}

			// Stop auxiliary loops before closing room.
			await overlayStop.CancelAsync();
			await Task.WhenAll(auxTasks.Concat(overlayTasks));
		}
		finally
		{
			await overlayStop.CancelAsync();

			// Players leave and sign out.
			await Task.WhenAll(players.Select(async player =>
			{
				if (player.Token is not null)
				{
					player.Enqueue(ClientPackets.PartMatch());
					await workload.LogoutStableAsync(player, context);
				}
			}));

			// Spectators and tourney clients sign out.
			await Task.WhenAll(spectators.Concat(tourneyClients).Select(async client =>
			{
				if (client.Token is not null)
				{
					client.Enqueue(ClientPackets.StopSpectating());
					await workload.LogoutStableAsync(client, context);
				}
			}));

			// Referee closes the room and disconnects.
			if (roomId > 0)
			{
				workload.World.RemoveRoom(roomId);
				using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				if (ircRef is not null)
				{
					await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
					{
						await ircRef.PrivmsgAsync($"#mp_{roomId}", "!mp close", closeTimeout.Token);
						return StepOutcome.Ok("mp_close");
					}, closeTimeout.Token);
				}
				else if (restRef is not null)
				{
					await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
					{
						var reply = await restRef.PostAsync($"/v1/rooms/{roomId}/close", null, closeTimeout.Token);
						return OutcomeClassifier.FromRest(reply, refusalExpected: true);
					}, closeTimeout.Token);
				}
			}

			if (ircRef is not null) await ircRef.DisposeAsync();
			if (restRef is { Token: not null })
			{
				try
				{
					using var outTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
					await restRef.SignOutAsync(outTimeout.Token);
				}
				catch (Exception)
				{
					// Best-effort cleanup.
				}
			}

			foreach (var lease in playerLeases) lease.Dispose();
			foreach (var lease in spectatorLeases) lease.Dispose();
			foreach (var cluster in tourneyClusterLeases)
				foreach (var lease in cluster)
					lease.Dispose();
			refLease.Dispose();
		}
	}

	private async Task<(IrcClient? Client, int RoomId)> OpenIrcRoomAsync(AccountEntry referee, string roomName,
		int size, BeatmapEntry map, IScenarioContext context, CancellationToken ct)
	{
		var client = new IrcClient(workload.Endpoint, workload.IrcPort, referee.Name, referee.Password);
		var (regOutcome, reg) = await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
		{
			var result = await client.ConnectAsync(workload.WaitTimeout, ct);
			return (OutcomeClassifier.FromIrcRegistration(result), result);
		}, ct);

		if (regOutcome.Outcome != Outcome.Ok || reg is not { Welcomed: true })
		{
			await client.DisposeAsync();
			return (null, 0);
		}

		var (_, createdId) = await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
		{
			var line = await client.RequestAsync(
				new IrcMessage("PRIVMSG", null, "BasilBot", $"!mp make {roomName}"),
				msg => msg.Command == "PRIVMSG" && msg.Parameters.Count >= 2 && CreatedRoomRegex().IsMatch(msg.Parameters[^1]),
				workload.WaitTimeout, ct);
			if (line is null) return (StepOutcome.Error("mp_make_timeout"), 0);

			var match = CreatedRoomRegex().Match(line.Parameters[^1]);
			return match.Success && int.TryParse(match.Groups[1].Value, out var id)
				? (StepOutcome.Ok("mp_make"), id)
				: (StepOutcome.Error("mp_make_parse"), 0);
		}, ct);

		if (createdId <= 0)
		{
			await client.DisposeAsync();
			return (null, 0);
		}

		var channel = $"#mp_{createdId}";
		await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
		{
			await client.JoinAsync(channel, ct);
			await client.PrivmsgAsync(channel, $"!mp set 2 0 {size}", ct);
			await client.PrivmsgAsync(channel, $"!mp map {map.Id} {map.Mode}", ct);
			await client.PrivmsgAsync(channel, "!mp mods None", ct);
			return StepOutcome.Ok("mp_setup");
		}, ct);

		return (client, createdId);
	}

	private async Task<(RestClient? Client, int RoomId)> OpenRestRoomAsync(AccountEntry referee, string roomName,
		int size, BeatmapEntry map, IScenarioContext context, CancellationToken ct)
	{
		var client = new RestClient(workload.Endpoint, workload.Http);
		var loginOutcome = await StepExecution.RunAsync(Channels.Login, context, workload.Outcomes, async () =>
		{
			var reply = await client.SignInAsync(referee.Name, referee.Password, ct);
			return OutcomeClassifier.FromRest(reply);
		}, ct);

		if (loginOutcome.Outcome != Outcome.Ok || client.Token is null) return (null, 0);

		var (_, roomId) = await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
		{
			var reply = await client.PostAsync("/v1/rooms", new { name = roomName, password = "", isPrivate = false }, ct);
			var classified = OutcomeClassifier.FromRest(reply);
			var id = reply is { Success: true } && reply.Data.TryGetProperty("id", out var prop) ? prop.GetInt32() : 0;
			return (id > 0 ? classified : StepOutcome.Error("room_id_missing"), id);
		}, ct);

		if (roomId <= 0) return (client, 0);

		await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
		{
			var setReply = await client.PostAsync($"/v1/rooms/{roomId}/set",
				new { teamType = "teamVs", winCondition = "score", size }, ct);
			return OutcomeClassifier.FromRest(setReply);
		}, ct);

		await SetRestMapAsync(client, roomId, map, context, ct);

		await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
		{
			var modsReply = await client.PostAsync($"/v1/rooms/{roomId}/mods",
				new { mods = Array.Empty<string>(), freemod = false }, ct);
			return OutcomeClassifier.FromRest(modsReply);
		}, ct);

		return (client, roomId);
	}

	private async Task SetIrcMapAsync(IrcClient irc, int roomId, BeatmapEntry map, IScenarioContext context,
		CancellationToken ct)
	{
		await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
		{
			await irc.PrivmsgAsync($"#mp_{roomId}", $"!mp map {map.Id} {map.Mode}", ct);
			return StepOutcome.Ok("mp_map");
		}, ct);
	}

	private async Task SetRestMapAsync(RestClient rest, int roomId, BeatmapEntry map, IScenarioContext context,
		CancellationToken ct)
	{
		await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
		{
			var reply = await rest.PostAsync($"/v1/rooms/{roomId}/map",
				new { beatmap = map.Id, mode = WorkloadContext.RestModeName(map.Mode) }, ct);
			return OutcomeClassifier.FromRest(reply);
		}, ct);
	}

	private async Task<bool> StartIrcRoundAsync(IrcClient irc, int roomId, IScenarioContext context, CancellationToken ct)
	{
		var outcome = await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
		{
			await irc.PrivmsgAsync($"#mp_{roomId}", "!mp start", ct);
			return StepOutcome.Ok("mp_start");
		}, ct);
		return outcome.Outcome == Outcome.Ok;
	}

	private async Task<bool> StartRestRoundAsync(RestClient rest, int roomId, IScenarioContext context, CancellationToken ct)
	{
		var outcome = await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
		{
			var reply = await rest.PostAsync($"/v1/rooms/{roomId}/start", new { }, ct);
			return OutcomeClassifier.FromRest(reply);
		}, ct);
		return outcome.Outcome == Outcome.Ok;
	}

	private async Task<StableClient?> JoinPlayerAsync(AccountLease lease, int roomId, int index, int playerCount,
		IScenarioContext context, CancellationToken ct)
	{
		var client = new StableClient(workload.Endpoint, workload.Http, lease.Identity());
		var login = await workload.LoginStableAsync(client, context, ct);
		if (login is not { Accepted: true }) return null;

		client.Enqueue(ClientPackets.JoinMatch(roomId));
		var joined = await workload.PollUntilAsync<MatchJoined>(client, null, workload.WaitTimeout, context, ct);
		if (joined is null)
		{
			await workload.LogoutStableAsync(client, context);
			return null;
		}

		// Split players into Red and Blue teams.
		if (index >= playerCount / 2)
		{
			client.Enqueue(ClientPackets.MatchChangeTeam());
			await workload.PollStableAsync(client, context, ct);
		}

		return client;
	}

	private async Task ReadyPlayerAsync(StableClient player, IScenarioContext context, CancellationToken ct)
	{
		player.Enqueue(ClientPackets.MatchHasBeatmap(), ClientPackets.MatchReady());
		await workload.PollStableAsync(player, context, ct);
	}

	private async Task PlayRoundAsync(StableClient player, int slot, BeatmapEntry map, Random random,
		IScenarioContext context, CancellationToken ct)
	{
		var started = await workload.PollUntilAsync<MatchStarted>(player, null, workload.WaitTimeout, context, ct);
		if (started is null || ct.IsCancellationRequested) return;

		player.Enqueue(
			ClientPackets.ChangeAction(WorkloadContext.PlayingAction(map)),
			ClientPackets.MatchLoadComplete());
		await workload.PollUntilAsync<MatchAllPlayersLoaded>(player, null, workload.WaitTimeout, context, ct);
		if (ct.IsCancellationRequested) return;

		workload.World.PutOnline(new OnlineUser(player.UserId, player.Identity.Name, true, map.Md5));

		var ticks = Math.Max(2, (int)(workload.RoundDuration.TotalSeconds / Math.Max(0.5, workload.PollInterval.TotalSeconds)));
		for (var tick = 0; tick < ticks && !ct.IsCancellationRequested; tick++)
		{
			var action = tick == 0 ? SpectatorAction.NewSong : SpectatorAction.Standard;
			player.Enqueue(
				ClientPackets.MatchScoreUpdate(WorkloadContext.BuildScoreFrame(tick, slot, random)),
				ClientPackets.SpectateFrames(WorkloadContext.BuildSpectatorBundle(tick, slot, random, action)));
			await workload.PollStableAsync(player, context, ct);
			await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
		}

		if (ct.IsCancellationRequested) return;

		player.Enqueue(
			ClientPackets.SpectateFrames(WorkloadContext.BuildSpectatorBundle(ticks, slot, random, SpectatorAction.Completion)),
			ClientPackets.MatchComplete(),
			ClientPackets.ChangeAction(WorkloadContext.IdleAction()));
		await workload.PollStableAsync(player, context, ct);

		// Submit the played score with replay.
		var web = new OsuWebClient(workload.Endpoint, workload.Http, player.Identity);
		var score = PlayedScore.Generate(random, map.Md5, passed: true, mode: map.Mode, mods: 0,
			storyboardMd5: map.StoryboardMd5);
		var replay = SampleFiles.Replay(random);
		await StepExecution.RunAsync(Channels.Submit, context, workload.Outcomes, async () =>
		{
			var reply = await web.SubmitScoreAsync(score, replay, ct);
			return OutcomeClassifier.FromScoreSubmission(reply.Status, reply.Body);
		}, ct);

		workload.World.PutOnline(new OnlineUser(player.UserId, player.Identity.Name, false, string.Empty));
		await workload.PollUntilAsync<MatchCompleted>(player, null, workload.WaitTimeout, context, ct);
	}

	private async Task RunTourneyClusterAsync(StableClient manager, IReadOnlyList<StableClient> children, int roomId,
		IReadOnlyList<int> playerIds, IScenarioContext context, CancellationToken ct)
	{
		var managerLogin = await workload.LoginStableAsync(manager, context, ct);
		if (managerLogin is not { Accepted: true }) return;

		manager.Enqueue(
			ClientPackets.TournamentMatchInfoRequest(roomId),
			ClientPackets.TournamentJoinMatchChannel(roomId));
		await workload.PollStableAsync(manager, context, ct);

		var activeChildren = new List<StableClient>();
		for (var i = 0; i < children.Count && i < playerIds.Count && !ct.IsCancellationRequested; i++)
		{
			var child = children[i];
			var childLogin = await workload.LoginStableAsync(child, context, ct);
			if (childLogin is not { Accepted: true }) continue;

			child.Enqueue(ClientPackets.StartSpectating(playerIds[i]));
			await workload.PollStableAsync(child, context, ct);
			activeChildren.Add(child);
		}

		while (!ct.IsCancellationRequested)
		{
			await workload.PollStableAsync(manager, context, ct);
			foreach (var child in activeChildren)
			{
				if (ct.IsCancellationRequested) break;
				await workload.PollStableAsync(child, context, ct);
			}

			await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
		}
	}

	private async Task RunSpectatorAsync(StableClient spectator, int targetUserId, IScenarioContext context,
		CancellationToken ct)
	{
		var login = await workload.LoginStableAsync(spectator, context, ct);
		if (login is not { Accepted: true }) return;

		spectator.Enqueue(ClientPackets.StartSpectating(targetUserId));
		while (!ct.IsCancellationRequested)
		{
			await workload.PollStableAsync(spectator, context, ct);
			await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
		}
	}

	private async Task RunOverlayAsync(int roomId, bool matchReport, RestClient? existingRest,
		IScenarioContext context, CancellationToken ct)
	{
		var path = matchReport ? $"/v1/matches/{roomId}" : $"/v1/rooms/{roomId}/live";
		var rest = existingRest ?? new RestClient(workload.Endpoint, workload.Http);
		if (!matchReport && rest.Token is null)
		{
			// Public match stream needs no token; /rooms/{id}/live needs a signed-in viewer.
			if (!workload.Accounts.TryAcquire(AccountRole.Player, out var viewerLease)) return;
			using (viewerLease)
			{
				var loginReply = await rest.SignInAsync(viewerLease.Account.Name, viewerLease.Account.Password, ct);
				if (!loginReply.Success) return;
				await ConsumeSseAsync(rest, path, authorize: true, context, ct);
				try
				{
					using var outTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
					await rest.SignOutAsync(outTimeout.Token);
				}
				catch (Exception)
				{
					// Cleanup.
				}
			}

			return;
		}

		await ConsumeSseAsync(rest, path, authorize: !matchReport, context, ct);
	}

	private async Task ConsumeSseAsync(RestClient rest, string path, bool authorize, IScenarioContext context,
		CancellationToken ct)
	{
		await StepExecution.RunAsync(Channels.Sse, context, workload.Outcomes, async () =>
		{
			using var stream = await rest.OpenStreamAsync(path, authorize, ct);
			var open = OutcomeClassifier.FromHttpStatus(stream.Status);
			if (open.Outcome != Outcome.Ok) return open;

			long bytes = 0;
			var events = 0;
			await foreach (var ev in stream.ReadAsync(ct))
			{
				bytes += ev.Data.Length;
				events++;
			}

			return events > 0 || ct.IsCancellationRequested
				? StepOutcome.Ok("sse", bytes)
				: StepOutcome.Error("sse_empty");
		}, ct);
	}

	private static int PickTeamSize(Random random, int minPlayers, int maxPlayers)
	{
		int[] allowed = [2, 4, 8];
		var candidates = allowed.Where(n => n >= minPlayers && n <= maxPlayers).ToArray();
		return candidates.Length == 0 ? Math.Max(2, minPlayers) : candidates[random.Next(candidates.Length)];
	}
}
