using System.Collections.Concurrent;
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
/// <param name="RoundsPerCycle">Not used: the room lives for the whole scenario copy and every invocation plays one round.</param>
/// <param name="TourneyClusters">
///     Number of osu!tourney clusters attached to each room, or <see langword="null" /> to draw
///     0 to 2 for each room.
/// </param>
/// <param name="SpectatorsPerRoom">In-game spectators watching players in this room.</param>
/// <param name="OverlayViewersPerRoom">SSE overlay viewers subscribed to the room and match report.</param>
/// <param name="ForceIrcReferee">When set, forces IRC or REST referee instead of alternating 50/50.</param>
public sealed record MatchRoomConfig(
	int MinPlayers = 2,
	int MaxPlayers = 8,
	int RoundsPerCycle = 2,
	int? TourneyClusters = null,
	int SpectatorsPerRoom = 1,
	int OverlayViewersPerRoom = 2,
	bool? ForceIrcReferee = null);

/// <summary>
///     Simulates a live tournament room: 2–8 players, one IRC or REST referee, 0–2 tourney clusters, spectators and
///     SSE overlay viewers. Each scenario copy opens its room on the first invocation, keeps every session, and plays one
///     round per invocation.
/// </summary>
public sealed partial class MatchRoomPersona(WorkloadContext workload, MatchRoomConfig? config = null)
{
	private readonly MatchRoomConfig _config = config ?? new MatchRoomConfig();
	private readonly ConcurrentDictionary<int, Room> _rooms = new();
	private volatile bool _closing;

	[GeneratedRegex(@"Created room #(\d+)", RegexOptions.CultureInvariant)]
	private static partial Regex CreatedRoomRegex();

	/// <summary>
	///     Plays one round in the copy's room, opening the room and signing everyone in first when the copy has none or
	///     the server dropped a session of it.
	/// </summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		if (_closing) return;

		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var instance = context.ScenarioInfo.InstanceNumber;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName, instance,
			context.InvocationNumber);

		await ReapIdleAsync(instance);

		var room = await EnsureRoomAsync(instance, random, context, ct);
		if (room is null)
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		try
		{
			room.Context = context;
			foreach (var loop in room.Loops) loop.EnsureRunning();

			await PlayRoundAsync(room, random, context, ct);
		}
		finally
		{
			room.Gate.Exit();
		}
	}

	/// <summary>
	///     Closes every room this persona still holds, signs every session out and returns every account lease; best
	///     effort, never throws.
	/// </summary>
	public async Task ShutdownAsync()
	{
		_closing = true;
		var all = _rooms.Values.ToArray();
		_rooms.Clear();
		await Task.WhenAll(all.Select(TeardownAsync));
	}

	/// <summary>
	///     Tears down the rooms of the other scenario copies that have not run for longer than
	///     <see cref="WorkloadContext.IdleReapAfter" />; never throws.
	/// </summary>
	private async Task ReapIdleAsync(int instance)
	{
		foreach (var pair in _rooms)
		{
			if (pair.Key == instance || !pair.Value.Gate.TryReap(workload.IdleReapAfter)) continue;
			if (!_rooms.TryRemove(pair)) continue;

			try
			{
				await TeardownAsync(pair.Value);
			}
			catch (Exception)
			{
				// Best effort.
			}
		}
	}

	private async Task<Room?> EnsureRoomAsync(int instance, Random random, IScenarioContext context,
		CancellationToken ct)
	{
		if (_rooms.TryGetValue(instance, out var existing))
		{
			if (!existing.Gate.TryEnter())
			{
				// Another invocation of this persona reaped the room while it sat idle.
				_rooms.TryRemove(new KeyValuePair<int, Room>(instance, existing));
			}
			else
			{
				if (!existing.Broken && !existing.Players.Any(workload.IsDropped)) return existing;

				_rooms.TryRemove(instance, out _);
				await TeardownAsync(existing);
			}
		}

		var room = await BuildRoomAsync(instance, random, context, ct);
		if (room is not null) _rooms[instance] = room;
		return room;
	}

	private async Task<Room?> BuildRoomAsync(int instance, Random random, IScenarioContext context,
		CancellationToken ct)
	{
		var playerCount = PickTeamSize(random, _config.MinPlayers, _config.MaxPlayers);
		var useIrcRef = _config.ForceIrcReferee ?? instance % 2 == 0;

		if (!workload.Accounts.TryAcquire(AccountRole.Referee, out var refLease)) return null;
		if (!workload.Accounts.TryAcquireMany(AccountRole.Player, playerCount, out var playerLeases))
		{
			refLease.Dispose();
			return null;
		}

		var room = new Room(refLease, playerLeases, context.ScenarioCancellationToken, workload.AbortToken)
		{
			Name = $"Tourney {instance}-{context.InvocationNumber}",
			Context = context,
			Beatmap = workload.PickBeatmap(random)
		};

		// Spectators, tourney clusters and overlay viewers; their accounts are taken now and held for the room's life.
		for (var i = 0; i < _config.SpectatorsPerRoom; i++)
			if (workload.Accounts.TryAcquire(AccountRole.Player, out var specLease))
				room.Spectators.Add(new Spectator(specLease));

		var clusters = _config.TourneyClusters ?? random.Next(3);
		for (var c = 0; c < clusters; c++)
			if (workload.Accounts.TryAcquire(AccountRole.Tourney, out var tourneyLease))
				room.Clusters.Add(new Cluster(tourneyLease, playerCount));

		for (var v = 0; v < _config.OverlayViewersPerRoom; v++)
			room.Overlays.Add(new Overlay(v % 2 == 0));

		try
		{
			// 1. Referee opens and configures the room.
			if (useIrcRef)
				await OpenIrcRoomAsync(room, playerCount, context, ct);
			else
				await OpenRestRoomAsync(room, playerCount, context, ct);

			if (room.RoomId <= 0 || ct.IsCancellationRequested)
			{
				await TeardownAsync(room);
				return null;
			}

			workload.World.PutRoom(new LiveRoom(room.RoomId, room.Name, string.Empty, room.Beatmap.Id, room.Beatmap.Md5,
				true, false, []));

			// 2. Players sign in and join the room concurrently.
			var joined = await Task.WhenAll(playerLeases.Select((lease, index) =>
				JoinPlayerAsync(lease, room.RoomId, index, playerCount, context, ct)));
			room.Players.AddRange(joined.Where(p => p is not null)!);
			if (room.Players.Count < 2 || ct.IsCancellationRequested)
			{
				await TeardownAsync(room);
				return null;
			}

			room.PlayerIds = [.. room.Players.Select(p => p.UserId)];
			PutRoom(room, false);

			// 3. Background loops that live as long as the room: tourney clusters, spectators, overlay viewers.
			foreach (var cluster in room.Clusters)
				room.Loops.Add(new RoomLoop(room, () => RunTourneyClusterAsync(room, cluster)));
			foreach (var spectator in room.Spectators)
			{
				spectator.TargetId = room.PlayerIds[random.Next(room.PlayerIds.Count)];
				room.Loops.Add(new RoomLoop(room, () => RunSpectatorAsync(room, spectator)));
			}

			foreach (var overlay in room.Overlays)
				room.Loops.Add(new RoomLoop(room, () => RunOverlayAsync(room, overlay)));

			return room;
		}
		catch
		{
			await TeardownAsync(room);
			throw;
		}
	}

	private async Task PlayRoundAsync(Room room, Random random, IScenarioContext context, CancellationToken ct)
	{
		var round = room.Round++;
		if (round > 0)
		{
			room.Beatmap = workload.PickBeatmap(random);
			if (room.Irc is not null)
				await MpAsync(room, $"!mp map {room.Beatmap.Id} {room.Beatmap.Mode}", "mp_map", context, ct);
			else
				await SetRestMapAsync(room, room.Beatmap, context, ct);
		}

		var beatmap = room.Beatmap;

		// Ready up players.
		await Task.WhenAll(room.Players.Select(p => ReadyPlayerAsync(p, context, ct)));

		// In Spike mode, wait for the shared tournament clock while keeping sessions alive.
		if (workload.TournamentClock.IsEnabled)
		{
			var waitTask = workload.TournamentClock.WaitForNextWaveAsync(ct);
			while (!waitTask.IsCompleted && !ct.IsCancellationRequested)
			{
				await Task.WhenAll(room.Players.Select(p => workload.PollStableAsync(p, context, ct)));
				await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
			}
		}

		if (ct.IsCancellationRequested) return;

		// Start the round through the referee.
		var started = room.Irc is not null
			? await MpAsync(room, "!mp start", "mp_start", context, ct) is { Outcome: Outcome.Ok }
			: await StartRestRoundAsync(room, context, ct);
		if (!started)
		{
			if (!ct.IsCancellationRequested) room.Broken = true;
			return;
		}

		PutRoom(room, true);

		// Run the round for every player concurrently; a player's MatchStarted is what confirms the start.
		var confirmations = await Task.WhenAll(room.Players.Select((player, slot) =>
			PlayPlayerRoundAsync(player, slot, beatmap,
				SeededRandom.For(workload.Seed, "round", room.RoomId * 16 + slot, round),
				context, ct)));

		PutRoom(room, false);

		if (!ct.IsCancellationRequested && !confirmations.Any(confirmed => confirmed))
		{
			room.Broken = true;
			await StepExecution.RunAsync(room.Irc is not null ? Channels.Irc : Channels.Rest, context,
				workload.Outcomes,
				() => Task.FromResult(StepOutcome.Error("mp_start_unconfirmed")), ct);
		}
	}

	private void PutRoom(Room room, bool inProgress)
	{
		workload.World.PutRoom(new LiveRoom(room.RoomId, room.Name, string.Empty, room.Beatmap.Id, room.Beatmap.Md5,
			true, inProgress, room.PlayerIds));
	}

	private async Task TeardownAsync(Room room)
	{
		await room.StopLoopsAsync();

		// Players leave and sign out; spectators and tourney clients were signed out by their loops.
		await Task.WhenAll(room.Players.Select(async player =>
		{
			if (player.Token is null) return;

			player.Enqueue(ClientPackets.PartMatch());
			await workload.SignOutQuietAsync(player);
		}));

		// Referee closes the room and disconnects.
		if (room.RoomId > 0)
		{
			workload.World.RemoveRoom(room.RoomId);
			try
			{
				using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				if (room.Irc is not null)
					await IrcSteps.CommandAsync(room.Irc, $"#mp_{room.RoomId}", "!mp close", "mp_close",
						TimeSpan.FromSeconds(5),
						closeTimeout.Token);
				else if (room.Rest is not null)
					await room.Rest.PostAsync($"/v1/rooms/{room.RoomId}/close", null, closeTimeout.Token);
			}
			catch (Exception)
			{
				// Best effort: the server closes an empty room by itself.
			}
		}

		if (room.Irc is not null) await room.Irc.DisposeAsync();
		if (room.Rest is not null) await WorkloadContext.SignOutQuietAsync(room.Rest);
		foreach (var overlay in room.Overlays)
		{
			if (overlay.Rest is not null) await WorkloadContext.SignOutQuietAsync(overlay.Rest);
			overlay.Lease?.Dispose();
		}

		foreach (var lease in room.PlayerLeases) lease.Dispose();
		foreach (var spectator in room.Spectators) spectator.Lease.Dispose();
		foreach (var cluster in room.Clusters) cluster.Lease.Dispose();
		room.RefereeLease.Dispose();
		room.Dispose();
	}

	private async Task OpenIrcRoomAsync(Room room, int size, IScenarioContext context, CancellationToken ct)
	{
		var referee = room.RefereeLease.Account;
		var client = new IrcClient(workload.Endpoint, workload.IrcPort, referee.Name, referee.Password);
		room.Irc = client;
		var (regOutcome, reg) = await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
		{
			var result = await client.ConnectAsync(workload.WaitTimeout, ct);
			return (OutcomeClassifier.FromIrcRegistration(result), result);
		}, ct);

		if (regOutcome.Outcome != Outcome.Ok || reg is not { Welcomed: true })
		{
			if (reg is { Line: not null }) room.RefereeLease.Quarantine();
			return;
		}

		var (_, createdId) = await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes, async () =>
		{
			var line = await client.RequestAsync(
				new IrcMessage("PRIVMSG", null, "BasilBot", $"!mp make {room.Name}"),
				msg => msg is { Command: "PRIVMSG", Parameters.Count: >= 2 } &&
				       CreatedRoomRegex().IsMatch(msg.Parameters[^1]),
				workload.WaitTimeout, ct);
			if (line is null) return (StepOutcome.Error("mp_make_timeout"), 0);

			var match = CreatedRoomRegex().Match(line.Parameters[^1]);
			return match.Success && int.TryParse(match.Groups[1].Value, out var id)
				? (StepOutcome.Ok("mp_make"), id)
				: (StepOutcome.Error("mp_make_parse"), 0);
		}, ct);

		if (createdId <= 0) return;

		room.RoomId = createdId;
		var channel = $"#mp_{createdId}";
		await StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes,
			() => IrcSteps.JoinAsync(client, channel, workload.WaitTimeout, ct), ct);
		await MpAsync(room, $"!mp set 2 0 {size}", "mp_set", context, ct);
		await MpAsync(room, $"!mp map {room.Beatmap.Id} {room.Beatmap.Mode}", "mp_map", context, ct);
		await MpAsync(room, "!mp mods None", "mp_mods", context, ct);
	}

	private async Task OpenRestRoomAsync(Room room, int size, IScenarioContext context, CancellationToken ct)
	{
		var referee = room.RefereeLease.Account;
		var client = new RestClient(workload.Endpoint, workload.Http);
		room.Rest = client;
		var loginOutcome = await StepExecution.RunAsync(Channels.Login, context, workload.Outcomes, async () =>
		{
			var reply = await client.SignInAsync(referee.Name, referee.Password, ct);
			return OutcomeClassifier.FromRest(reply);
		}, ct);

		if (loginOutcome.Outcome != Outcome.Ok || client.Token is null) return;

		var (_, roomId) = await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
		{
			var reply = await client.PostAsync("/v1/rooms", new { name = room.Name, password = "", isPrivate = false },
				ct);
			var classified = OutcomeClassifier.FromRest(reply);
			var id = reply is { Success: true } && reply.Data.TryGetProperty("id", out var prop) ? prop.GetInt32() : 0;
			return (id > 0 ? classified : StepOutcome.Error("room_id_missing"), id);
		}, ct);

		if (roomId <= 0) return;

		room.RoomId = roomId;
		await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
		{
			var setReply = await client.PostAsync($"/v1/rooms/{roomId}/set",
				new { teamType = "teamVs", winCondition = "score", size }, ct);
			return OutcomeClassifier.FromRest(setReply);
		}, ct);

		await SetRestMapAsync(room, room.Beatmap, context, ct);

		await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
		{
			var modsReply = await client.PostAsync($"/v1/rooms/{roomId}/mods",
				new { mods = Array.Empty<string>(), freemod = false }, ct);
			return OutcomeClassifier.FromRest(modsReply);
		}, ct);
	}

	// Sends a !mp command to the room channel; the step is ok when BasilBot answers it.
	private Task<StepOutcome> MpAsync(Room room, string text, string tag, IScenarioContext context,
		CancellationToken ct)
	{
		return StepExecution.RunAsync(Channels.Irc, context, workload.Outcomes,
			() => IrcSteps.CommandAsync(room.Irc!, $"#mp_{room.RoomId}", text, tag, workload.WaitTimeout, ct), ct);
	}

	private async Task SetRestMapAsync(Room room, BeatmapEntry map, IScenarioContext context, CancellationToken ct)
	{
		await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
		{
			var reply = await room.Rest!.PostAsync($"/v1/rooms/{room.RoomId}/map",
				new { beatmap = map.Id, mode = WorkloadContext.RestModeName(map.Mode) }, ct);
			return OutcomeClassifier.FromRest(reply);
		}, ct);
	}

	private async Task<bool> StartRestRoundAsync(Room room, IScenarioContext context, CancellationToken ct)
	{
		var outcome = await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
		{
			var reply = await room.Rest!.PostAsync($"/v1/rooms/{room.RoomId}/start", new { }, ct);
			return OutcomeClassifier.FromRest(reply);
		}, ct);
		return outcome.Outcome == Outcome.Ok;
	}

	private async Task<StableClient?> JoinPlayerAsync(AccountLease lease, int roomId, int index, int playerCount,
		IScenarioContext context, CancellationToken ct)
	{
		var client = new StableClient(workload.Endpoint, workload.Http, lease.Identity());
		var login = await workload.LoginStableAsync(client, context, ct, lease: lease);
		if (login is not { Accepted: true }) return null;

		client.Enqueue(ClientPackets.JoinMatch(roomId));
		var joined = await workload.PollUntilAsync<MatchJoined>(client, null, workload.WaitTimeout, context, ct);
		if (joined is null)
		{
			await workload.SignOutQuietAsync(client);
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

	private async Task<bool> PlayPlayerRoundAsync(StableClient player, int slot, BeatmapEntry map, Random random,
		IScenarioContext context, CancellationToken ct)
	{
		var started = await workload.PollUntilAsync<MatchStarted>(player, null, workload.WaitTimeout, context, ct);
		if (started is null || ct.IsCancellationRequested) return started is not null;

		player.Enqueue(
			ClientPackets.ChangeAction(WorkloadContext.PlayingAction(map)),
			ClientPackets.MatchLoadComplete());
		await workload.PollUntilAsync<MatchAllPlayersLoaded>(player, null, workload.WaitTimeout, context, ct);
		if (ct.IsCancellationRequested) return true;

		workload.World.PutOnline(new OnlineUser(player.UserId, player.Identity.Name, true, map.Md5));

		var ticks = Math.Max(2,
			(int)(workload.RoundDuration.TotalSeconds / Math.Max(0.5, workload.PollInterval.TotalSeconds)));
		for (var tick = 0; tick < ticks && !ct.IsCancellationRequested; tick++)
		{
			var action = tick == 0 ? SpectatorAction.NewSong : SpectatorAction.Standard;
			player.Enqueue(
				ClientPackets.MatchScoreUpdate(WorkloadContext.BuildScoreFrame(tick, slot, random)),
				ClientPackets.SpectateFrames(WorkloadContext.BuildSpectatorBundle(tick, slot, random, action)));
			await workload.PollStableAsync(player, context, ct);
			await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
		}

		if (ct.IsCancellationRequested) return true;

		player.Enqueue(
			ClientPackets.SpectateFrames(
				WorkloadContext.BuildSpectatorBundle(ticks, slot, random, SpectatorAction.Completion)),
			ClientPackets.MatchComplete(),
			ClientPackets.ChangeAction(WorkloadContext.IdleAction()));
		await workload.PollStableAsync(player, context, ct);

		// Submit the played score with replay.
		var web = new OsuWebClient(workload.Endpoint, workload.Http, player.Identity);
		var score = PlayedScore.Generate(random, map.Md5, true, map.Mode, 0,
			map.StoryboardMd5);
		var replay = SampleFiles.Replay(random);
		await StepExecution.RunAsync(Channels.Submit, context, workload.Outcomes, async () =>
		{
			var reply = await web.SubmitScoreAsync(score, replay, ct);
			return OutcomeClassifier.FromScoreSubmission(reply.Status, reply.Body);
		}, ct);

		workload.World.PutOnline(new OnlineUser(player.UserId, player.Identity.Name, false, string.Empty));
		await workload.PollUntilAsync<MatchCompleted>(player, null, workload.WaitTimeout, context, ct);
		return true;
	}

	private async Task RunTourneyClusterAsync(Room room, Cluster cluster)
	{
		var ct = room.StopToken;
		try
		{
			// One Tourney account signs in the manager and every child client, as the osu!tourney client does.
			var identity = cluster.Lease.Identity(true);
			var manager = new StableClient(workload.Endpoint, workload.Http, identity);
			cluster.Clients.Add(manager);
			var managerLogin = await workload.LoginStableAsync(manager, room.Context, ct, lease: cluster.Lease);
			if (managerLogin is not { Accepted: true }) return;

			manager.Enqueue(
				ClientPackets.TournamentMatchInfoRequest(room.RoomId),
				ClientPackets.TournamentJoinMatchChannel(room.RoomId));
			await workload.PollStableAsync(manager, room.Context, ct);

			for (var i = 0; i < cluster.ChildCount && i < room.PlayerIds.Count && !ct.IsCancellationRequested; i++)
			{
				var child = new StableClient(workload.Endpoint, workload.Http, identity);
				cluster.Clients.Add(child);
				var childLogin = await workload.LoginStableAsync(child, room.Context, ct, lease: cluster.Lease);
				if (childLogin is not { Accepted: true }) continue;

				child.Enqueue(ClientPackets.StartSpectating(room.PlayerIds[i]));
				await workload.PollStableAsync(child, room.Context, ct);
			}

			while (!ct.IsCancellationRequested && !cluster.Clients.Any(workload.IsDropped))
			{
				foreach (var client in cluster.Clients.Where(client => client.Token is not null))
				{
					if (ct.IsCancellationRequested) break;
					await workload.PollStableAsync(client, room.Context, ct);
				}

				await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
			}
		}
		finally
		{
			var clients = cluster.Clients.ToArray();
			cluster.Clients.Clear();
			await Task.WhenAll(clients.Select(async client =>
			{
				client.Enqueue(ClientPackets.StopSpectating());
				await workload.SignOutQuietAsync(client);
			}));
		}
	}

	private async Task RunSpectatorAsync(Room room, Spectator spectator)
	{
		var ct = room.StopToken;
		var client = spectator.Client ??=
			new StableClient(workload.Endpoint, workload.Http, spectator.Lease.Identity());
		try
		{
			var login = await workload.LoginStableAsync(client, room.Context, ct, lease: spectator.Lease);
			if (login is not { Accepted: true }) return;

			client.Enqueue(ClientPackets.StartSpectating(spectator.TargetId));
			while (!ct.IsCancellationRequested && !workload.IsDropped(client))
			{
				await workload.PollStableAsync(client, room.Context, ct);
				await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
			}
		}
		finally
		{
			client.Enqueue(ClientPackets.StopSpectating());
			await workload.SignOutQuietAsync(client);
		}
	}

	private async Task RunOverlayAsync(Room room, Overlay overlay)
	{
		var ct = room.StopToken;
		// A REST referee's own signed-in client watches too; otherwise the viewer has a client of its own.
		var rest = overlay.Rest ??= room.Rest ?? new RestClient(workload.Endpoint, workload.Http);
		if (!overlay.MatchReport && rest.Token is null)
		{
			// The room stream needs a signed-in viewer; the match report is public.
			if (overlay.Lease is null)
			{
				if (!workload.Accounts.TryAcquire(AccountRole.Player, out var viewerLease)) return;
				overlay.Lease = viewerLease;
			}

			var login = await StepExecution.RunAsync(Channels.Login, room.Context, workload.Outcomes, async () =>
			{
				var reply = await rest.SignInAsync(overlay.Lease.Account.Name, overlay.Lease.Account.Password, ct);
				return OutcomeClassifier.FromRest(reply);
			}, ct);
			if (login.Outcome != Outcome.Ok) return;
		}

		var path = overlay.MatchReport ? $"/v1/matches/{room.RoomId}" : $"/v1/rooms/{room.RoomId}/live";
		await SseWatch.WatchAsync(workload, rest, path, !overlay.MatchReport, () => room.Context, ct);
	}

	private static int PickTeamSize(Random random, int minPlayers, int maxPlayers)
	{
		int[] allowed = [2, 4, 8];
		var candidates = allowed.Where(n => n >= minPlayers && n <= maxPlayers).ToArray();
		return candidates.Length == 0 ? Math.Max(2, minPlayers) : candidates[random.Next(candidates.Length)];
	}

	/// <summary>The sessions, accounts and background loops of one room, kept for the life of its scenario copy.</summary>
	private sealed class Room(
		AccountLease refereeLease,
		List<AccountLease> playerLeases,
		CancellationToken scenarioStopped,
		CancellationToken runAborted)
		: IDisposable
	{
		private readonly CancellationTokenSource _stop =
			CancellationTokenSource.CreateLinkedTokenSource(scenarioStopped, runAborted);

		public IdleGate Gate { get; } = new();

		public AccountLease RefereeLease { get; } = refereeLease;

		public List<AccountLease> PlayerLeases { get; } = playerLeases;

		public string Name { get; init; } = string.Empty;

		public IScenarioContext Context { get; set; } = null!;

		public BeatmapEntry Beatmap { get; set; } = null!;

		public IrcClient? Irc { get; set; }

		public RestClient? Rest { get; set; }

		public int RoomId { get; set; }

		public int Round { get; set; }

		public bool Broken { get; set; }

		public IReadOnlyList<int> PlayerIds { get; set; } = [];

		public List<StableClient> Players { get; } = [];

		public List<Spectator> Spectators { get; } = [];

		public List<Cluster> Clusters { get; } = [];

		public List<Overlay> Overlays { get; } = [];

		public List<RoomLoop> Loops { get; } = [];

		/// <summary>Gets the token that stops the background loops.</summary>
		public CancellationToken StopToken => _stop.Token;

		public void Dispose()
		{
			_stop.Dispose();
		}

		public async Task StopLoopsAsync()
		{
			await _stop.CancelAsync();
			try
			{
				await Task.WhenAll(Loops.Select(loop => loop.Running).OfType<Task>())
					.WaitAsync(TimeSpan.FromSeconds(10), StopToken);
			}
			catch (Exception)
			{
				// A loop that does not end in time is left to stop with the process.
			}
		}
	}

	/// <summary>A background loop of a room that is started again when it ended before the room was stopped.</summary>
	private sealed class RoomLoop(Room room, Func<Task> start)
	{
		public Task? Running { get; private set; }

		public void EnsureRunning()
		{
			if (room.StopToken.IsCancellationRequested) return;
			if (Running is null || Running.IsCompleted) Running = Task.Run(start);
		}
	}

	private sealed class Spectator(AccountLease lease)
	{
		public AccountLease Lease { get; } = lease;

		public StableClient? Client { get; set; }

		public int TargetId { get; set; }
	}

	private sealed class Cluster(AccountLease lease, int childCount)
	{
		public AccountLease Lease { get; } = lease;

		public int ChildCount { get; } = childCount;

		public List<StableClient> Clients { get; } = [];
	}

	private sealed class Overlay(bool matchReport)
	{
		public bool MatchReport { get; } = matchReport;

		public RestClient? Rest { get; set; }

		public AccountLease? Lease { get; set; }
	}
}