using System.Runtime.InteropServices;

namespace Basil.LoadTests.Infrastructure.Metrics;

/// <summary>
///     Samples machine-wide CPU usage (all processes, all cores) — distinct from
///     <see cref="ProcessResourceSampler" />, which only sees the server process. Needed because the
///     load generator runs co-resident with the server in every local run: without this, client-side
///     CPU pressure can't be ruled out as a contributor to inflated latency percentiles.
/// </summary>
/// <remarks>
///     Windows: <c>GetSystemTimes</c> (idle/kernel/user), delta-based, no P/Invoke dependency beyond
///     <c>kernel32</c>. Linux: the aggregate <c>cpu</c> line in <c>/proc/stat</c>, delta-based. Any other
///     platform reports the field as unavailable rather than guessing.
/// </remarks>
public sealed class TotalMachineCpuSampler : IResourceSampler
{
	private ulong _lastIdle;
	private ulong _lastTotal;
	private bool _hasPrevious;

	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public Task<ResourceSample> SampleAsync(CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;
		var reading = OperatingSystem.IsWindows() ? ReadWindows() : OperatingSystem.IsLinux() ? ReadLinux() : null;
		if (reading is not { Idle: var idle, Total: var total })
			return Task.FromResult(new ResourceSample(now));

		double? percent = null;
		if (_hasPrevious && total > _lastTotal)
		{
			var idleDelta = idle - _lastIdle;
			var totalDelta = total - _lastTotal;
			percent = 100.0 * (1.0 - (double)idleDelta / totalDelta);
		}

		_lastIdle = idle;
		_lastTotal = total;
		_hasPrevious = true;

		return Task.FromResult(new ResourceSample(now) { TotalMachineCpuPercent = percent });
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}

	private static (ulong Idle, ulong Total)? ReadWindows()
	{
		if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;

		var idleTicks = ToUInt64(idle);
		// Windows counts idle time as part of kernel time, so summing kernel+user double-counts idle —
		// total must be (kernel - idle) + idle + user, i.e. kernel + user, with idle tracked separately.
		var totalTicks = ToUInt64(kernel) + ToUInt64(user);
		return (idleTicks, totalTicks);
	}

	private static (ulong Idle, ulong Total)? ReadLinux()
	{
		try
		{
			using var reader = new StreamReader("/proc/stat");
			var line = reader.ReadLine();
			if (line is null || !line.StartsWith("cpu ", StringComparison.Ordinal)) return null;

			var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			// user nice system idle iowait irq softirq [steal [guest [guest_nice]]]
			if (parts.Length < 5) return null;

			var fields = parts.Skip(1).Select(p => ulong.TryParse(p, out var v) ? v : 0UL).ToArray();
			var idle = fields[3] + (fields.Length > 4 ? fields[4] : 0); // idle + iowait
			var total = fields.Aggregate(0UL, (sum, f) => sum + f);
			return (idle, total);
		}
		catch (IOException)
		{
			return null;
		}
	}

	private static ulong ToUInt64(Filetime time)
	{
		return ((ulong)time.dwHighDateTime << 32) | time.dwLowDateTime;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetSystemTimes(out Filetime lpIdleTime, out Filetime lpKernelTime,
		out Filetime lpUserTime);

	[StructLayout(LayoutKind.Sequential)]
	private struct Filetime
	{
		public uint dwLowDateTime;
		public uint dwHighDateTime;
	}
}