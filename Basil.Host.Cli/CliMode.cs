namespace Basil.Host.Cli;

public enum ConsoleLevel : byte
{
	Log = 0,

	Full = 1
}

public sealed record CliOptions(ConsoleLevel Console, bool NoColor = false, bool Ascii = false)
{
	public bool HasDashboard => Console != ConsoleLevel.Log;
}

public sealed class CliOptionException(string message) : Exception(message);

public static class CliModes
{
	private const string ConsoleFlag = "--console";

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
					throw new CliOptionException(
						$"{list[i]} was removed; use --console 0 (log only) or --console 1 (dashboard).");
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

	public static bool TermIsDumb()
	{
		return string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);
	}

	public static bool NoColorIsSet()
	{
		return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
	}

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
			_ => throw new CliOptionException($"--console takes 0 or 1, not '{value}'.")
		};
	}

	private static CliOptionException Missing()
	{
		return new CliOptionException("--console needs a value: 0 or 1.");
	}
}