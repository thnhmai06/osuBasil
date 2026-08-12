using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Basil.LoadTests.Infrastructure.Metrics;

/// <summary>
///     Samples CPU, memory, thread, handle, and TCP-connection counts for a known local process id.
///     This is the sampler every owned host (<c>dotnet</c>) delegates to for everything except
///     GC/allocation counters, which come from <see cref="DotnetRuntimeMetricsCollector" /> instead.
/// </summary>
/// <param name="processId">The process id to sample.</param>
/// <param name="serverPort">The server's listening port, used to count established TCP connections to it.</param>
public sealed class ProcessResourceSampler(int processId, int serverPort) : IResourceSampler
{
	private TimeSpan _lastCpuTime = TimeSpan.Zero;
	private DateTimeOffset _lastSampleTime = DateTimeOffset.UtcNow;

	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public Task<ResourceSample> SampleAsync(CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;

		Process? process;
		try
		{
			process = Process.GetProcessById(processId);
		}
		catch (ArgumentException)
		{
			// The process has exited; the caller (a host watching for process death) is the
			// authority on that, not this sampler — report an empty sample rather than throwing.
			return Task.FromResult(new ResourceSample(now));
		}

		using (process)
		{
			process.Refresh();

			double? cpuPercent = null;
			var cpuTime = process.TotalProcessorTime;
			var wallDelta = now - _lastSampleTime;
			if (_lastCpuTime != TimeSpan.Zero && wallDelta > TimeSpan.Zero)
			{
				var cpuDelta = cpuTime - _lastCpuTime;
				cpuPercent = cpuDelta.TotalMilliseconds / (wallDelta.TotalMilliseconds * Environment.ProcessorCount) *
				             100.0;
			}

			_lastCpuTime = cpuTime;
			_lastSampleTime = now;

			var sample = new ResourceSample(now)
			{
				CpuPercent = cpuPercent,
				WorkingSetBytes = process.WorkingSet64,
				PrivateMemoryBytes = process.PrivateMemorySize64,
				ThreadCount = process.Threads.Count,
				HandleCount = OperatingSystem.IsWindows() ? process.HandleCount : null,
				TcpConnections = CountTcpConnections()
			};

			return Task.FromResult(sample);
		}
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}

	private int? CountTcpConnections()
	{
		try
		{
			return IPGlobalProperties.GetIPGlobalProperties()
				.GetActiveTcpConnections()
				.Count(c => c.LocalEndPoint.Port == serverPort && c.State == TcpState.Established);
		}
		catch (NetworkInformationException)
		{
			return null;
		}
	}
}