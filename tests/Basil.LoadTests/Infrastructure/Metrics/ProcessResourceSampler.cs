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
/// <param name="logWarning">Sink for the one-time PID-reuse warning; defaults to a no-op.</param>
/// <remarks>
///     Caches the target process's start time at construction and re-checks it on every sample.
///     Windows (and Linux) recycle process ids once a process exits, so a bare <c>Process.GetProcessById</c>
///     can silently start reporting an unrelated process's resource usage after the real target has
///     died — see the 2026-08-25/26 Phase 0 baseline run, where the server crashed mid-run and this
///     sampler kept reporting a climbing thread/handle count for the next three hours against whatever
///     process later reused its PID, misread as a severe leak until traced back to this gap.
/// </remarks>
public sealed class ProcessResourceSampler(int processId, int serverPort, Action<string>? logWarning = null)
	: IResourceSampler
{
	private readonly DateTime _expectedStartTime = TryGetStartTime(processId);
	private bool _warnedOnce;
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
			if (_expectedStartTime != DateTime.MinValue && process.StartTime != _expectedStartTime)
			{
				// Same PID, different process: the OS recycled it after the original target died.
				// Reporting this process's numbers would silently attribute someone else's resource
				// usage to the server under test.
				if (!_warnedOnce)
				{
					_warnedOnce = true;
					logWarning?.Invoke(
						$"Process id {processId} no longer belongs to the sampled server (its start time " +
						"changed) — the id was recycled by another process. Reporting an empty sample instead " +
						"of misattributed data.");
				}

				return Task.FromResult(new ResourceSample(now));
			}

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

	private static DateTime TryGetStartTime(int processId)
	{
		try
		{
			using var process = Process.GetProcessById(processId);
			return process.StartTime;
		}
		catch (ArgumentException)
		{
			return DateTime.MinValue;
		}
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