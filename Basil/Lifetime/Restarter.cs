using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Basil.Lifetime;

public static class Restarter
{
	public const string RestartedVariable = "BASIL_RESTARTED";

	public static bool UnderManager()
	{
		return UnderManager(Environment.GetEnvironmentVariable, ParentProcessName);
	}

	public static bool UnderManager(Func<string, string?> environment, Func<string?> parentProcessName)
	{
		return string.Equals(environment("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(parentProcessName(), "systemd", StringComparison.Ordinal);
	}

	public static void Start(string[] args)
	{
		if (Environment.ProcessPath is not { } path) return;

		var (file, arguments) = RelaunchCommand(path, Environment.GetCommandLineArgs(), args);
		var start = new ProcessStartInfo(file) { UseShellExecute = false };
		foreach (var arg in arguments) start.ArgumentList.Add(arg);
		start.Environment[RestartedVariable] = "1";
		Process.Start(start);
	}

	public static (string File, string[] Arguments) RelaunchCommand(string processPath, string[] commandLine,
		string[] args)
	{
		var host = Path.GetFileNameWithoutExtension(processPath);
		return string.Equals(host, "dotnet", StringComparison.OrdinalIgnoreCase) && commandLine.Length > 0
			? (processPath, [commandLine[0], .. args])
			: (processPath, args);
	}

	private static string? ParentProcessName()
	{
		try
		{
			if (!OperatingSystem.IsLinux()) return null;

			// The fields of /proc/self/stat after the parenthesised name: state, then the parent id.
			var stat = File.ReadAllText("/proc/self/stat");
			var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
			return File.ReadAllText($"/proc/{fields[1]}/comm").Trim();
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException
			                          or IndexOutOfRangeException)
		{
			return null;
		}
	}

	public static bool WasRestarted()
	{
		return Environment.GetEnvironmentVariable(RestartedVariable) is not null;
	}

	public static async Task<bool> WaitForPortsAsync(Func<bool> isFree, TimeProvider clock, TimeSpan timeout,
		CancellationToken ct = default)
	{
		var started = clock.GetTimestamp();
		while (!isFree())
		{
			if (clock.GetElapsedTime(started) >= timeout) return false;

			await Task.Delay(TimeSpan.FromMilliseconds(250), clock, ct);
		}

		return true;
	}

	public static bool CanBind(IPAddress address, int port)
	{
		if (port == 0) return true;

		try
		{
			using var probe = new TcpListener(address, port);
			probe.Start();
			return true;
		}
		catch (SocketException)
		{
			return false;
		}
	}
}