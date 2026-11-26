using System.Collections.Concurrent;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Simulates an in-game spectator: each scenario copy signs in once and keeps the session, then every invocation watches one real playing user for one round.</summary>
public sealed class SpectatorPersona(WorkloadContext workload)
{
	private readonly ConcurrentDictionary<int, Held> _held = new();
	private volatile bool _closing;

	/// <summary>Runs one spectating window for the copy's signed-in spectator, signing in first when the copy has no live session.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		if (_closing) return;

		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var instance = context.ScenarioInfo.InstanceNumber;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName, instance, context.InvocationNumber);

		var held = await EnsureSessionAsync(instance, context, ct);
		if (held is null)
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		var client = held.Client;
		var target = workload.World.PickPlayingUser(random, client.UserId)
		             ?? workload.World.PickOnlineUser(random, client.UserId);
		if (target is null)
		{
			await workload.PollStableAsync(client, context, ct);
			await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
			return;
		}

		client.Enqueue(ClientPackets.StartSpectating(target.UserId));
		var ticks = Math.Max(3, (int)(workload.RoundDuration.TotalSeconds / Math.Max(0.5, workload.PollInterval.TotalSeconds)));
		for (var tick = 0; tick < ticks && !ct.IsCancellationRequested; tick++)
		{
			if (tick == ticks / 2 && random.Next(3) == 0)
				client.Enqueue(ClientPackets.SendPublicMessage("#spectator", "nice play!"));
			await workload.PollStableAsync(client, context, ct);
			await WorkloadContext.SafeDelayAsync(workload.PollInterval, ct);
		}

		client.Enqueue(ClientPackets.StopSpectating());
		await workload.PollStableAsync(client, context, ct);
	}

	/// <summary>Signs out every session this persona still holds and returns every account lease; best effort, never throws.</summary>
	public async Task ShutdownAsync()
	{
		_closing = true;
		var all = _held.Values.ToArray();
		_held.Clear();
		await Task.WhenAll(all.Select(CloseAsync));
	}

	private async Task<Held?> EnsureSessionAsync(int instance, IScenarioContext context, CancellationToken ct)
	{
		if (_held.TryGetValue(instance, out var existing))
		{
			if (!workload.IsDropped(existing.Client)) return existing;

			_held.TryRemove(instance, out _);
			await CloseAsync(existing);
		}

		if (!workload.Accounts.TryAcquire(AccountRole.Player, out var lease)) return null;

		var client = new StableClient(workload.Endpoint, workload.Http, lease.Identity());
		var login = await workload.LoginStableAsync(client, context, ct, lease: lease);
		if (login is not { Accepted: true })
		{
			lease.Dispose();
			return null;
		}

		var held = new Held(lease, client);
		_held[instance] = held;
		return held;
	}

	private async Task CloseAsync(Held held)
	{
		await workload.SignOutQuietAsync(held.Client);
		held.Lease.Dispose();
	}

	private sealed record Held(AccountLease Lease, StableClient Client);
}
