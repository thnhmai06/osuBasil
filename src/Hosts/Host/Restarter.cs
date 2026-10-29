using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Basil.Host;

/// <summary>Starts the server again after a <c>restart</c> when no manager does it.</summary>
public static class Restarter
{
	/// <summary>The environment variable that tells the new process that it replaces a server that is still letting go of its ports.</summary>
	public const string RestartedVariable = "BASIL_RESTARTED";

	/// <summary>Tells whether systemd or a container runtime manages the process and restarts it from its exit code.</summary>
	public static bool UnderManager()
	{
		return Environment.GetEnvironmentVariable("INVOCATION_ID") is not null ||
		       string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Starts the same executable with the same arguments.</summary>
	/// <param name="args">The arguments of this process.</param>
	public static void Start(string[] args)
	{
		if (Environment.ProcessPath is not { } path) return;

		var start = new ProcessStartInfo(path) { UseShellExecute = false };
		foreach (var arg in args) start.ArgumentList.Add(arg);
		start.Environment[RestartedVariable] = "1";
		Process.Start(start);
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
	/// <param name="cancellationToken">A token that stops the wait.</param>
	/// <returns><see langword="true" /> when the ports are free; <see langword="false" /> when the time ran out, and the server then starts and reports the failure itself.</returns>
	public static async Task<bool> WaitForPortsAsync(Func<bool> isFree, TimeProvider clock, TimeSpan timeout,
		CancellationToken cancellationToken = default)
	{
		var started = clock.GetTimestamp();
		while (!isFree())
		{
			if (clock.GetElapsedTime(started) >= timeout) return false;

			await Task.Delay(TimeSpan.FromMilliseconds(250), clock, cancellationToken);
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
