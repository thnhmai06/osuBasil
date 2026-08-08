using Basil.LoadTests.Configuration;
using Basil.LoadTests.Hosting;
using Basil.LoadTests.Infrastructure.Metrics;

namespace Basil.LoadTests.Scenarios;

/// <summary>Startup time plus idle resource usage over repeated start/settle/stop cycles. Not an NBomber scenario — there is no load to simulate here.</summary>
/// <param name="StartupTimes">One entry per iteration where the host could measure it; empty for a host that didn't start the server itself.</param>
/// <param name="IdleSamples">Resource samples taken while the freshly started server sat idle.</param>
public sealed record StartupBenchmarkResult(IReadOnlyList<TimeSpan> StartupTimes, IReadOnlyList<ResourceSample> IdleSamples);

/// <summary>Runs the Phase 1 startup/idle-resource benchmark.</summary>
public static class StartupBenchmark
{
	/// <summary>Runs <paramref name="settings" />.Iterations start/settle/stop cycles against <paramref name="host" />.</summary>
	public static async Task<StartupBenchmarkResult> RunAsync(IServerHost host, StartupSettings settings,
		TimeSpan idleSettle, TimeSpan sampleInterval, CancellationToken cancellationToken = default)
	{
		var startupTimes = new List<TimeSpan>();
		var idleSamples = new List<ResourceSample>();

		for (var i = 0; i < settings.Iterations; i++)
		{
			await host.StartAsync(cancellationToken);
			var elapsed = await host.WaitUntilHealthyAsync(cancellationToken);
			if (elapsed.HasValue) startupTimes.Add(elapsed.Value);

			var settleUntil = DateTimeOffset.UtcNow + idleSettle;
			while (DateTimeOffset.UtcNow < settleUntil)
			{
				idleSamples.Add(await host.CollectMetricsAsync(cancellationToken));
				await Task.Delay(sampleInterval, cancellationToken);
			}

			await host.StopAsync(cancellationToken);
		}

		return new StartupBenchmarkResult(startupTimes, idleSamples);
	}
}
