namespace Basil.Host.Cli;

/// <summary>How the server shows itself in its terminal.</summary>
public enum CliMode : byte
{
	/// <summary>The dashboard and the command box.</summary>
	Full,

	/// <summary>The dashboard only (<c>--noconsole</c>).</summary>
	NoConsole,

	/// <summary>Plain log lines without colour (<c>--nogui</c>).</summary>
	NoGui
}

/// <summary>Reads the display mode from the command line and the terminal.</summary>
public static class CliModes
{
	/// <summary>Picks the mode.</summary>
	/// <param name="args">The command-line arguments.</param>
	/// <param name="inputRedirected">Whether stdin is redirected.</param>
	/// <param name="outputRedirected">Whether stdout is redirected.</param>
	/// <param name="supportsColour">Whether the terminal shows colour.</param>
	/// <returns>
	///     <see cref="CliMode.NoGui" /> when it is asked for, or when the terminal is redirected or has no colour;
	///     otherwise <see cref="CliMode.NoConsole" /> when it is asked for; otherwise <see cref="CliMode.Full" />.
	/// </returns>
	public static CliMode Choose(IEnumerable<string> args, bool inputRedirected, bool outputRedirected, bool supportsColour)
	{
		var flags = args.Select(arg => arg.ToLowerInvariant()).ToHashSet();
		if (flags.Contains("--nogui") || inputRedirected || outputRedirected || !supportsColour) return CliMode.NoGui;

		return flags.Contains("--noconsole") ? CliMode.NoConsole : CliMode.Full;
	}

	/// <summary>Tells whether the terminal supports colour: not dumb, and the user did not ask for none.</summary>
	public static bool TerminalSupportsColour()
	{
		var term = Environment.GetEnvironmentVariable("TERM");
		return Environment.GetEnvironmentVariable("NO_COLOR") is null && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Removes the display arguments, which the configuration must not read as settings.</summary>
	public static string[] Strip(IEnumerable<string> args)
	{
		return [.. args.Where(arg => !arg.Equals("--nogui", StringComparison.OrdinalIgnoreCase) &&
		                             !arg.Equals("--noconsole", StringComparison.OrdinalIgnoreCase))];
	}
}
