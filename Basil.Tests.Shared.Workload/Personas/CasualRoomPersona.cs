using System.Collections.Concurrent;
using Basil.Tests.Shared.Clients.OsuWeb;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>
///     Simulates a casual in-game multiplayer room: each scenario copy creates the room and signs its players in
///     once, keeps them, and every invocation plays one round.
/// </summary>
public sealed class CasualRoomPersona(WorkloadContext workload, int playersPerRoom = 2)
{
	private readonly ConcurrentDictionary<int, Room> _rooms = new();
	private volatile bool _closing;

	/// <summary>Plays one round in the copy's room, creating the room and signing its players in first when the copy has none.</summary>
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
			await PlayRoundAsync(room, context, ct);
			// The players look at the result before the next round; a round that failed is not restarted at once.
			await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
		}
		finally
		{
			room.Gate.Exit();
		}
	}

	private async Task PlayRoundAsync(Room room, IScenarioContext context, CancellationToken ct)
	{
		var round = room.Rounds++;
		var clients = room.Clients;
		var host = clients[0];

		// Everyone readies up, then the host starts.
		await Task.WhenAll(clients.Select(async client =>
		{
			client.Enqueue(ClientPackets.MatchHasBeatmap(), ClientPackets.MatchReady());
			await workload.PollStableAsync(client, context, ct);
		}));
		if (ct.IsCancellationRequested) return;

		host.Enqueue(ClientPackets.MatchStart());
		await workload.PollStableAsync(host, context, ct);

		await Task.WhenAll(clients.Select((client, slot) =>
			PlayCasualRoundAsync(client, slot, room.Map,
				SeededRandom.For(workload.Seed, "casual", room.RoomId * 16 + slot, round), context, ct)));
	}

	/// <summary>
	///     Closes every room this persona still holds, signs its players out and returns every account lease; best
	///     effort, never throws.
	/// </summary>
	public async Task ShutdownAsync()
	{
		_closing = true;
		var all = _rooms.Values.ToArray();
		_rooms.Clear();
		await Task.WhenAll(all.Select(CloseAsync));
	}

	/// <summary>
	///     Closes the rooms of the other scenario copies that have not run for longer than
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
				await CloseAsync(pair.Value);
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
				if (!existing.Clients.Any(workload.IsDropped)) return existing;

				_rooms.TryRemove(instance, out _);
				await CloseAsync(existing);
			}
		}

		var count = Math.Max(2, playersPerRoom);
		if (!workload.Accounts.TryAcquireMany(AccountRole.Player, count, out var leases)) return null;

		var room = new Room(leases, workload.PickBeatmap(random));
		var host = new StableClient(workload.Endpoint, workload.Http, leases[0].Identity());
		var hostLogin = await workload.LoginStableAsync(host, context, ct, lease: leases[0]);
		if (hostLogin is not { Accepted: true })
		{
			await CloseAsync(room);
			return null;
		}

		room.Clients.Add(host);
		var map = room.Map;
		var roomName = $"Casual {context.ScenarioInfo.InstanceNumber}-{context.InvocationNumber}";
		var blank = ClientPackets.BlankRoom(roomName, mode: map.Mode, mapMd5: map.Md5, mapId: map.Id,
			mapName: map.Version);
		host.Enqueue(ClientPackets.JoinLobby(), ClientPackets.CreateMatch(blank));
		var created = await workload.PollUntilAsync<MatchJoined>(host, null, workload.WaitTimeout, context, ct);
		if (created is null)
		{
			await CloseAsync(room);
			return null;
		}

		room.RoomId = created.Room.Id;
		workload.World.PutRoom(new LiveRoom(room.RoomId, roomName, string.Empty, map.Id, map.Md5, false, false,
			[host.UserId]));

		// Guests join and ready up.
		for (var i = 1; i < leases.Count && !ct.IsCancellationRequested; i++)
		{
			var guest = new StableClient(workload.Endpoint, workload.Http, leases[i].Identity());
			var login = await workload.LoginStableAsync(guest, context, ct, lease: leases[i]);
			if (login is not { Accepted: true }) continue;

			guest.Enqueue(ClientPackets.JoinMatch(room.RoomId));
			var joined = await workload.PollUntilAsync<MatchJoined>(guest, null, workload.WaitTimeout, context, ct);
			if (joined is null)
			{
				await workload.SignOutQuietAsync(guest);
				continue;
			}

			room.Clients.Add(guest);
		}

		if (room.Clients.Count < 2 || ct.IsCancellationRequested)
		{
			await CloseAsync(room);
			return null;
		}

		_rooms[instance] = room;
		return room;
	}

	private async Task CloseAsync(Room room)
	{
		if (room.RoomId > 0) workload.World.RemoveRoom(room.RoomId);
		await Task.WhenAll(room.Clients.Select(async client =>
		{
			if (client.Token is null) return;

			client.Enqueue(ClientPackets.PartMatch(), ClientPackets.PartLobby());
			await workload.SignOutQuietAsync(client);
		}));
		foreach (var lease in room.Leases) lease.Dispose();
	}

	private async Task PlayCasualRoundAsync(StableClient client, int slot, BeatmapEntry map, Random random,
		IScenarioContext context, CancellationToken ct)
	{
		var started = await workload.PollUntilAsync<MatchStarted>(client, null, workload.WaitTimeout, context, ct);
		if (started is null || ct.IsCancellationRequested) return;

		client.Enqueue(ClientPackets.ChangeAction(WorkloadContext.PlayingAction(map)),
			ClientPackets.MatchLoadComplete());
		await workload.PollUntilAsync<MatchAllPlayersLoaded>(client, null, workload.WaitTimeout, context, ct);
		if (ct.IsCancellationRequested) return;

		var ticks = Math.Max(2,
			(int)(workload.RoundDuration.TotalSeconds / Math.Max(0.5, workload.PollInterval.TotalSeconds)));
		for (var tick = 0; tick < ticks && !ct.IsCancellationRequested; tick++)
		{
			client.Enqueue(ClientPackets.MatchScoreUpdate(WorkloadContext.BuildScoreFrame(tick, slot, random)));
			await workload.PollStableAsync(client, context, ct);
			await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
		}

		if (ct.IsCancellationRequested) return;

		client.Enqueue(ClientPackets.MatchComplete(), ClientPackets.ChangeAction(WorkloadContext.IdleAction()));
		await workload.PollStableAsync(client, context, ct);

		var web = new OsuWebClient(workload.Endpoint, workload.Http, client.Identity);
		var score = PlayedScore.Generate(random, map.Md5, true, map.Mode, 0,
			map.StoryboardMd5);
		await StepExecution.RunAsync(Channels.Submit, context, workload.Outcomes, async () =>
		{
			var reply = await web.SubmitScoreAsync(score, SampleFiles.Replay(random), ct);
			return OutcomeClassifier.FromScoreSubmission(reply.Status, reply.Body);
		}, ct);

		await workload.PollUntilAsync<MatchCompleted>(client, null, workload.WaitTimeout, context, ct);
	}

	private sealed class Room(List<AccountLease> leases, BeatmapEntry map)
	{
		public IdleGate Gate { get; } = new();

		public List<AccountLease> Leases { get; } = leases;

		public BeatmapEntry Map { get; } = map;

		public List<StableClient> Clients { get; } = [];

		public int RoomId { get; set; }

		public int Rounds { get; set; }
	}
}