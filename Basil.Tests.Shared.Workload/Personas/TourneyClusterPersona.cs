using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Simulates a standalone osu!tourney cluster (one manager plus child clients) watching a live room from the shared world.</summary>
public sealed class TourneyClusterPersona(WorkloadContext workload, int slotsToWatch = 4)
{
	/// <summary>Runs one tournament observation cycle.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName,
			context.ScenarioInfo.InstanceNumber, context.InvocationNumber);

		var room = workload.World.PickRoom(random, tournamentOnly: true) ?? workload.World.PickRoom(random);
		if (room is null || room.PlayerIds.Count == 0)
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(2), ct);
			return;
		}

		var childCount = Math.Min(Math.Max(1, slotsToWatch), room.PlayerIds.Count);
		if (!workload.Accounts.TryAcquireMany(AccountRole.Tourney, 1 + childCount, out var leases))
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		var manager = new StableClient(workload.Endpoint, workload.Http, leases[0].Identity(tourney: true));
		var children = leases.Skip(1)
			.Select(lease => new StableClient(workload.Endpoint, workload.Http, lease.Identity(tourney: true)))
			.ToList();

		try
		{
			var login = await workload.LoginStableAsync(manager, context, ct);
			if (login is not { Accepted: true }) return;

			manager.Enqueue(
				ClientPackets.TournamentMatchInfoRequest(room.RoomId),
				ClientPackets.TournamentJoinMatchChannel(room.RoomId));
			await workload.PollStableAsync(manager, context, ct);

			var activeChildren = new List<StableClient>(childCount);
			for (var i = 0; i < children.Count && !ct.IsCancellationRequested; i++)
			{
				var child = children[i];
				var childLogin = await workload.LoginStableAsync(child, context, ct);
				if (childLogin is not { Accepted: true }) continue;

				child.Enqueue(ClientPackets.StartSpectating(room.PlayerIds[i]));
				await workload.PollStableAsync(child, context, ct);
				activeChildren.Add(child);
			}

			var ticks = Math.Max(3, (int)(workload.RoundDuration.TotalSeconds / Math.Max(0.5, workload.PollInterval.TotalSeconds)));
			for (var tick = 0; tick < ticks && !ct.IsCancellationRequested; tick++)
			{
				await workload.PollStableAsync(manager, context, ct);
				foreach (var child in activeChildren)
				{
					if (ct.IsCancellationRequested) break;
					await workload.PollStableAsync(child, context, ct);
				}

				await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
			}

			manager.Enqueue(ClientPackets.TournamentLeaveMatchChannel(room.RoomId));
			await workload.PollStableAsync(manager, context, ct);
		}
		finally
		{
			await Task.WhenAll(children.Select(async child =>
			{
				if (child.Token is not null)
				{
					child.Enqueue(ClientPackets.StopSpectating());
					await workload.LogoutStableAsync(child, context);
				}
			}));
			await workload.LogoutStableAsync(manager, context);
			foreach (var lease in leases) lease.Dispose();
		}
	}
}
