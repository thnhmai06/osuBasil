using Basil.Tests.Shared.Clients.OsuWeb;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Simulates a casual in-game multiplayer room created by a player host with one or more guests.</summary>
public sealed class CasualRoomPersona(WorkloadContext workload, int playersPerRoom = 2)
{
	/// <summary>Runs one casual room cycle from in-game creation through one round to teardown.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName,
			context.ScenarioInfo.InstanceNumber, context.InvocationNumber);

		var count = Math.Max(2, playersPerRoom);
		if (!workload.Accounts.TryAcquireMany(AccountRole.Player, count, out var leases))
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		var clients = new List<StableClient>(count);
		var roomId = 0;

		try
		{
			var map = workload.PickBeatmap(random);
			var host = new StableClient(workload.Endpoint, workload.Http, leases[0].Identity());
			var hostLogin = await workload.LoginStableAsync(host, context, ct);
			if (hostLogin is not { Accepted: true }) return;
			clients.Add(host);

			var roomName = $"Casual {context.ScenarioInfo.InstanceNumber}-{context.InvocationNumber}";
			var blank = ClientPackets.BlankRoom(roomName, mode: map.Mode, mapMd5: map.Md5, mapId: map.Id, mapName: map.Version);
			host.Enqueue(ClientPackets.JoinLobby(), ClientPackets.CreateMatch(blank));
			var created = await workload.PollUntilAsync<MatchJoined>(host, null, workload.WaitTimeout, context, ct);
			if (created is null || ct.IsCancellationRequested) return;

			roomId = created.Room.Id;
			workload.World.PutRoom(new LiveRoom(roomId, roomName, string.Empty, map.Id, map.Md5, false, false, [host.UserId]));

			// Guests join, ready up, and play with the host.
			for (var i = 1; i < leases.Count && !ct.IsCancellationRequested; i++)
			{
				var guest = new StableClient(workload.Endpoint, workload.Http, leases[i].Identity());
				var login = await workload.LoginStableAsync(guest, context, ct);
				if (login is not { Accepted: true }) continue;

				clients.Add(guest);
				guest.Enqueue(ClientPackets.JoinMatch(roomId));
				var joined = await workload.PollUntilAsync<MatchJoined>(guest, null, workload.WaitTimeout, context, ct);
				if (joined is null) continue;

				guest.Enqueue(ClientPackets.MatchHasBeatmap(), ClientPackets.MatchReady());
				await workload.PollStableAsync(guest, context, ct);
			}

			if (clients.Count < 2 || ct.IsCancellationRequested) return;

			host.Enqueue(ClientPackets.MatchHasBeatmap(), ClientPackets.MatchReady(), ClientPackets.MatchStart());
			await workload.PollStableAsync(host, context, ct);

			await Task.WhenAll(clients.Select((client, slot) =>
				PlayCasualRoundAsync(client, slot, map, SeededRandom.For(workload.Seed, "casual", roomId * 16 + slot, 0),
					context, ct)));
		}
		finally
		{
			if (roomId > 0) workload.World.RemoveRoom(roomId);
			await Task.WhenAll(clients.Select(async client =>
			{
				if (client.Token is not null)
				{
					client.Enqueue(ClientPackets.PartMatch(), ClientPackets.PartLobby());
					await workload.LogoutStableAsync(client, context);
				}
			}));
			foreach (var lease in leases) lease.Dispose();
		}
	}

	private async Task PlayCasualRoundAsync(StableClient client, int slot, BeatmapEntry map, Random random,
		IScenarioContext context, CancellationToken ct)
	{
		var started = await workload.PollUntilAsync<MatchStarted>(client, null, workload.WaitTimeout, context, ct);
		if (started is null || ct.IsCancellationRequested) return;

		client.Enqueue(ClientPackets.ChangeAction(WorkloadContext.PlayingAction(map)), ClientPackets.MatchLoadComplete());
		await workload.PollUntilAsync<MatchAllPlayersLoaded>(client, null, workload.WaitTimeout, context, ct);
		if (ct.IsCancellationRequested) return;

		var ticks = Math.Max(2, (int)(workload.RoundDuration.TotalSeconds / Math.Max(0.5, workload.PollInterval.TotalSeconds)));
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
		var score = PlayedScore.Generate(random, map.Md5, passed: true, mode: map.Mode, mods: 0,
			storyboardMd5: map.StoryboardMd5);
		await StepExecution.RunAsync(Channels.Submit, context, workload.Outcomes, async () =>
		{
			var reply = await web.SubmitScoreAsync(score, SampleFiles.Replay(random), ct);
			return OutcomeClassifier.FromScoreSubmission(reply.Status, reply.Body);
		}, ct);
	}
}
