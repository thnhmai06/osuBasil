using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Basil.Lifetime;

/// <summary>Starts the server again after a <c>restart</c> when no manager does it.</summary>
public static class Restarter
{
	/// <summary>
	///     The environment variable that tells the new process that it replaces a server that is still letting go of its
	///     ports.
	/// </summary>
	public const string RestartedVariable = "BASIL_RESTARTED";

	/// <summary>Tells whether systemd or a container runtime manages the process and restarts it from its exit code.</summary>
	public static bool UnderManager()
	{
		return UnderManager(Environment.GetEnvironmentVariable, ParentProcessName);
	}

	/// <summary>Tells whether a container runtime or systemd (as the parent process) manages the process.</summary>
	/// <param name="environment">Reads an environment variable.</param>
	/// <param name="parentProcessName">The name of the parent process, or <see langword="null" /> when unknown.</param>
	public static bool UnderManager(Func<string, string?> environment, Func<string?> parentProcessName)
	{
		return string.Equals(environment("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(parentProcessName(), "systemd", StringComparison.Ordinal);
	}

	/// <summary>Starts the same program with the same arguments.</summary>
	/// <param name="args">The arguments of this process.</param>
	public static void Start(string[] args)
	{
		if (Environment.ProcessPath is not { } path) return;

		var (file, arguments) = RelaunchCommand(path, Environment.GetCommandLineArgs(), args);
		var start = new ProcessStartInfo(file) { UseShellExecute = false };
		foreach (var arg in arguments) start.ArgumentList.Add(arg);
		start.Environment[RestartedVariable] = "1";
		Process.Start(start);
	}

	/// <summary>Builds the command that runs this program again.</summary>
	/// <param name="processPath">The executable of this process.</param>
	/// <param name="commandLine">The command line of this process, starting with the program being run.</param>
	/// <param name="args">The arguments of this process.</param>
	/// <returns>
	///     The file to start and its arguments; when the executable is the <c>dotnet</c> host, the program (the
	///     <c>.dll</c>) stays the first argument.
	/// </returns>
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

	/// <summary>Tells whether this process was started by <see cref="Start" />.</summary>
	public static bool WasRestarted()
	{
		return Environment.GetEnvironmentVariable(RestartedVariable) is not null;
	}

	/// <summary>Waits until the ports are free, because the process that is replaced may still hold them for a moment.</summary>
	/// <param name="isFree">Tells whether the ports can be bound.</param>
	/// <param name="clock">The clock the waiting uses.</param>
	/// <param name="timeout">The longest time to wait.</param>
	/// <param name="ct">A token that stops the wait.</param>
	/// <returns>
	///     <see langword="true" /> when the ports are free; <see langword="false" /> when the time ran out, and the
	///     server then starts and reports the failure itself.
	/// </returns>
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

	/// <summary>Tells whether a TCP port can be bound on an address.</summary>
	/// <param name="address">The address.</param>
	/// <param name="port">The port; <c>0</c> is always free.</param>
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