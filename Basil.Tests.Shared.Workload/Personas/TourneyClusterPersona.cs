using System.Collections.Concurrent;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>
///     Simulates a standalone osu!tourney cluster (one manager plus child clients, all signed in with one Tourney
///     account): each scenario copy signs the cluster in once and keeps it, then every invocation watches a live room.
/// </summary>
public sealed class TourneyClusterPersona(WorkloadContext workload, int slotsToWatch = 4)
{
	private readonly ConcurrentDictionary<int, Cluster> _clusters = new();
	private volatile bool _closing;

	/// <summary>
	///     Observes one live room for one round with the copy's cluster, signing the cluster in first when the copy has
	///     none.
	/// </summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		if (_closing) return;

		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var instance = context.ScenarioInfo.InstanceNumber;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName, instance,
			context.InvocationNumber);

		_clusters.TryGetValue(instance, out var cluster);
		if (cluster is not null && cluster.All.Any(workload.IsDropped))
		{
			_clusters.TryRemove(instance, out _);
			await CloseAsync(cluster);
			cluster = null;
		}

		var room = cluster is { WireId: > 0 } && workload.World.Rooms.TryGetValue(cluster.WireId, out var current)
			? current
			: workload.World.PickRoom(random, true) ?? workload.World.PickRoom(random);
		if (room is null || room.PlayerIds.Count == 0)
		{
			if (cluster is not null) await PollAllAsync(cluster, context, ct);
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(2), ct);
			return;
		}

		if (cluster is null)
		{
			cluster = await SignInAsync(Math.Min(Math.Max(1, slotsToWatch), room.PlayerIds.Count), context, ct);
			if (cluster is null)
			{
				await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
				return;
			}

			_clusters[instance] = cluster;
		}

		if (cluster.WireId != room.WireId) await AttachAsync(cluster, room, context, ct);

		var ticks = Math.Max(3,
			(int)(workload.RoundDuration.TotalSeconds / Math.Max(0.5, workload.PollInterval.TotalSeconds)));
		for (var tick = 0; tick < ticks && !ct.IsCancellationRequested; tick++)
		{
			await PollAllAsync(cluster, context, ct);
			await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
		}
	}

	/// <summary>Signs out every client this persona still holds and returns every account lease; best effort, never throws.</summary>
	public async Task ShutdownAsync()
	{
		_closing = true;
		var all = _clusters.Values.ToArray();
		_clusters.Clear();
		await Task.WhenAll(all.Select(CloseAsync));
	}

	private async Task<Cluster?> SignInAsync(int childCount, IScenarioContext context, CancellationToken ct)
	{
		if (!workload.Accounts.TryAcquire(AccountRole.Tourney, out var lease)) return null;

		// The osu!tourney client signs in the manager and every child with the same account.
		var identity = lease.Identity(true);
		var cluster = new Cluster(lease, new StableClient(workload.Endpoint, workload.Http, identity));
		var login = await workload.LoginStableAsync(cluster.Manager, context, ct, lease: lease);
		if (login is not { Accepted: true })
		{
			await CloseAsync(cluster);
			return null;
		}

		for (var i = 0; i < childCount && !ct.IsCancellationRequested; i++)
		{
			var child = new StableClient(workload.Endpoint, workload.Http, identity);
			if (await workload.LoginStableAsync(child, context, ct, lease: lease) is { Accepted: true })
				cluster.Children.Add(child);
		}

		return cluster;
	}

	private async Task AttachAsync(Cluster cluster, LiveRoom room, IScenarioContext context, CancellationToken ct)
	{
		if (cluster.WireId > 0)
		{
			cluster.Manager.Enqueue(ClientPackets.TournamentLeaveMatchChannel(cluster.WireId));
			foreach (var child in cluster.Children) child.Enqueue(ClientPackets.StopSpectating());
		}

		cluster.Manager.Enqueue(
			ClientPackets.TournamentMatchInfoRequest(room.WireId),
			ClientPackets.TournamentJoinMatchChannel(room.WireId));
		for (var i = 0; i < cluster.Children.Count && i < room.PlayerIds.Count; i++)
			cluster.Children[i].Enqueue(ClientPackets.StartSpectating(room.PlayerIds[i]));

		await PollAllAsync(cluster, context, ct);
		cluster.WireId = room.WireId;
	}

	private async Task PollAllAsync(Cluster cluster, IScenarioContext context, CancellationToken ct)
	{
		foreach (var client in cluster.All)
		{
			if (ct.IsCancellationRequested) break;
			await workload.PollStableAsync(client, context, ct);
		}
	}

	private async Task CloseAsync(Cluster cluster)
	{
		await Task.WhenAll(cluster.Children.Select(async child =>
		{
			child.Enqueue(ClientPackets.StopSpectating());
			await workload.SignOutQuietAsync(child);
		}));
		await workload.SignOutQuietAsync(cluster.Manager);
		cluster.Lease.Dispose();
	}

	private sealed class Cluster(AccountLease lease, StableClient manager)
	{
		public AccountLease Lease { get; } = lease;

		public StableClient Manager { get; } = manager;

		public List<StableClient> Children { get; } = [];

		public int WireId { get; set; }

		public IEnumerable<StableClient> All => Children.Prepend(Manager);
	}
}