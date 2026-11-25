using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Simulates an in-game spectator watching a real playing user from the shared world.</summary>
public sealed class SpectatorPersona(WorkloadContext workload)
{
	/// <summary>Runs one spectating cycle.</summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName,
			context.ScenarioInfo.InstanceNumber, context.InvocationNumber);

		if (!workload.Accounts.TryAcquire(AccountRole.Player, out var lease))
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		using (lease)
		{
			var client = new StableClient(workload.Endpoint, workload.Http, lease.Identity());
			var login = await workload.LoginStableAsync(client, context, ct);
			if (login is not { Accepted: true }) return;

			try
			{
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
			finally
			{
				await workload.LogoutStableAsync(client, context);
			}
		}
	}
}
