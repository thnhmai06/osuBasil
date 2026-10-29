using System.Diagnostics;

namespace Basil.Host;

/// <summary>Starts the server again after a <c>restart</c> when no manager does it.</summary>
public static class Restarter
{
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
		Process.Start(start);
	}
}
