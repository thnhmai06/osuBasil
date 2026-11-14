using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Basil.Infrastructure.Diagnostics.Services;

/// <summary>Reads the memory and the network traffic of the whole machine, which the process measurements cannot show.</summary>
internal sealed class MachineSampler
{
	private (long Sent, long Received) _lastTraffic = Traffic();

	/// <summary>Measures the machine.</summary>
	/// <param name="seconds">The seconds since the last measurement; the traffic is reported per second over them.</param>
	/// <returns>
	///     The memory used and in total in bytes, and the bytes sent and received per second by every network interface
	///     that is up, loopback excluded; a value the system does not publish is zero.
	/// </returns>
	public object Sample(double seconds)
	{
		var (total, available) = Memory();
		var traffic = Traffic();
		var result = new
		{
			totalMemoryBytes = total, usedMemoryBytes = Math.Max(0, total - available),
			networkSentBytesPerSecond = (long)Math.Max(0, (traffic.Sent - _lastTraffic.Sent) / seconds),
			networkReceivedBytesPerSecond = (long)Math.Max(0, (traffic.Received - _lastTraffic.Received) / seconds)
		};
		_lastTraffic = traffic;
		return result;
	}

	private static (long Total, long Available) Memory()
	{
		try
		{
			if (OperatingSystem.IsLinux())
			{
				long total = 0, available = 0;
				foreach (var line in File.ReadLines("/proc/meminfo"))
				{
					// Lines look like "MemTotal:       16318412 kB".
					var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
					if (parts[0] == "MemTotal:") total = long.Parse(parts[1]) * 1024;
					else if (parts[0] == "MemAvailable:") available = long.Parse(parts[1]) * 1024;
				}

				return (total, available);
			}

			if (OperatingSystem.IsWindows())
			{
				var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
				if (GlobalMemoryStatusEx(ref status)) return ((long)status.TotalPhys, (long)status.AvailPhys);
			}
		}
		catch (Exception exception) when (exception is IOException or FormatException or UnauthorizedAccessException
			                                  or IndexOutOfRangeException)
		{
		}

		return (0, 0);
	}

	private static (long Sent, long Received) Traffic()
	{
		long sent = 0, received = 0;
		try
		{
			foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (network.OperationalStatus != OperationalStatus.Up ||
				    network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

				var statistics = network.GetIPStatistics();
				sent += statistics.BytesSent;
				received += statistics.BytesReceived;
			}
		}
		catch (Exception exception) when (exception is NetworkInformationException or PlatformNotSupportedException)
		{
		}

		return (sent, received);
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

	[StructLayout(LayoutKind.Sequential)]
	private struct MemoryStatus
	{
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhys;
		public ulong AvailPhys;
		public ulong TotalPageFile;
		public ulong AvailPageFile;
		public ulong TotalVirtual;
		public ulong AvailVirtual;
		public ulong AvailExtendedVirtual;
	}
}
