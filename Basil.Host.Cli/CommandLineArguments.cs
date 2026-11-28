using Basil.Host.Cli.Commands;

namespace Basil.Host.Cli;

/// <summary>Reads the command line of the program: whether it names a terminal command, and whether it holds an argument nothing understands.</summary>
public static class CommandLineArguments
{
	/// <summary>Tells whether the first argument is a terminal command rather than a server option.</summary>
	public static bool IsCommand(string[] args)
	{
		if (args.Length == 0 || args[0].StartsWith('-')) return false;

		return CommandCatalogFactory.Create().All
			.Any(command => command.Words[0].Equals(args[0], StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	///     Gives the first argument the server would not understand, or <see langword="null"/> when every one is a known
	///     option, an option's value or a setting such as <c>--Basil:Server:Domain=example.org</c>.
	/// </summary>
	public static string? Unknown(string[] args)
	{
		string[] options = ["--console", "--nocolor", "--ascii", "--urls", "--environment", "--contentRoot", "--applicationName"];
		var expectsValue = false;
		foreach (var arg in args)
		{
			if (!arg.StartsWith('-'))
			{
				if (expectsValue)
				{
					expectsValue = false;
					continue;
				}

				return arg;
			}

			string[] switches = ["--nocolor", "--ascii", "--nogui", "--noconsole"];
			var isSwitch = switches.Contains(arg, StringComparer.OrdinalIgnoreCase);
			expectsValue = !arg.Contains('=') && !isSwitch;
			if (!isSwitch && !options.Contains(arg, StringComparer.OrdinalIgnoreCase) && !arg.Contains(':') &&
			    !arg.Contains('=') && !arg.Contains("__"))
				return arg;
		}

		return null;
	}
}