namespace Basil.Host.Cli;

/// <summary>How much of the console the server shows (<c>--console</c>).</summary>
public enum ConsoleLevel : byte
{
	/// <summary>Plain log lines on stdout, no dashboard (<c>--console 0</c>).</summary>
	Log = 0,

	/// <summary>The dashboard and the command box (<c>--console 1</c>, the default).</summary>
	Full = 1,

	/// <summary>The dashboard to look at only: nothing in it changes the server (<c>--console 2</c>).</summary>
	ViewOnly = 2
}

/// <summary>How the server shows itself in its terminal.</summary>
/// <param name="Console">How much of the console is shown.</param>
/// <param name="NoColor">Whether the dashboard is monochrome and its theme cannot change.</param>
/// <param name="Ascii">Whether the dashboard avoids every character outside ASCII.</param>
public sealed record CliOptions(ConsoleLevel Console, bool NoColor = false, bool Ascii = false)
{
	/// <summary>Gets whether the dashboard is shown.</summary>
	public bool HasDashboard => Console != ConsoleLevel.Log;

	/// <summary>Gets whether the dashboard may change the server: the command box, chat and the actions.</summary>
	public bool Editable => Console == ConsoleLevel.Full;
}

/// <summary>Thrown when a display argument is not one the server knows.</summary>
/// <param name="message">What is wrong with the argument.</param>
public sealed class CliOptionException(string message) : Exception(message);

/// <summary>Reads the display options from the command line and the terminal.</summary>
public static class CliModes
{
	private const string ConsoleFlag = "--console";

	/// <summary>Picks the options.</summary>
	/// <param name="args">The command-line arguments.</param>
	/// <param name="inputRedirected">Whether stdin is redirected.</param>
	/// <param name="outputRedirected">Whether stdout is redirected.</param>
	/// <param name="termDumb">Whether <c>TERM</c> is <c>dumb</c>.</param>
	/// <param name="noColorEnvironment">Whether <c>NO_COLOR</c> is set to a value.</param>
	/// <returns>
	///     The options. Redirected stdio or a dumb terminal always gives <see cref="ConsoleLevel.Log" />;
	///     <c>NO_COLOR</c> or <c>--nocolor</c> gives a monochrome dashboard.
	/// </returns>
	/// <exception cref="CliOptionException">An old flag, an unknown value or a missing value.</exception>
	public static CliOptions Choose(IEnumerable<string> args, bool inputRedirected, bool outputRedirected,
		bool termDumb, bool noColorEnvironment)
	{
		var list = args.ToList();
		var level = ConsoleLevel.Full;
		var noColor = noColorEnvironment;
		var ascii = false;
		for (var i = 0; i < list.Count; i++)
		{
			var arg = list[i].ToLowerInvariant();
			switch (arg)
			{
				case "--nogui" or "--noconsole":
					throw new CliOptionException($"{list[i]} was removed; use --console 0 (log only) or --console 2 (view only).");
				case "--nocolor":
					noColor = true;
					break;
				case "--ascii":
					ascii = true;
					break;
				case ConsoleFlag:
					level = i + 1 < list.Count ? ParseLevel(list[++i]) : throw Missing();
					break;
				default:
					if (arg.StartsWith(ConsoleFlag + "=", StringComparison.Ordinal))
						level = ParseLevel(list[i][(ConsoleFlag.Length + 1)..]);
					break;
			}
		}

		if (inputRedirected || outputRedirected || termDumb) level = ConsoleLevel.Log;

		return new CliOptions(level, noColor, ascii);
	}

	/// <summary>Tells whether <c>TERM</c> is <c>dumb</c>.</summary>
	public static bool TermIsDumb()
	{
		return string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Tells whether <c>NO_COLOR</c> is set to a value.</summary>
	public static bool NoColorIsSet()
	{
		return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
	}

	/// <summary>Removes the display arguments, which the configuration must not read as settings.</summary>
	public static string[] Strip(IEnumerable<string> args)
	{
		var list = args.ToList();
		var kept = new List<string>();
		for (var i = 0; i < list.Count; i++)
		{
			var arg = list[i];
			if (arg.Equals(ConsoleFlag, StringComparison.OrdinalIgnoreCase)) i++;
			else if (!arg.Equals("--nocolor", StringComparison.OrdinalIgnoreCase) &&
			         !arg.Equals("--ascii", StringComparison.OrdinalIgnoreCase) &&
			         !arg.StartsWith(ConsoleFlag + "=", StringComparison.OrdinalIgnoreCase))
				kept.Add(arg);
		}

		return [.. kept];
	}

	private static ConsoleLevel ParseLevel(string value)
	{
		return value switch
		{
			"0" => ConsoleLevel.Log,
			"1" => ConsoleLevel.Full,
			"2" => ConsoleLevel.ViewOnly,
			_ => throw new CliOptionException($"--console takes 0, 1 or 2, not '{value}'.")
		};
	}

	private static CliOptionException Missing()
	{
		return new CliOptionException("--console needs a value: 0, 1 or 2.");
	}
}