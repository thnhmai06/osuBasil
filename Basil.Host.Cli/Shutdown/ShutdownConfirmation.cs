using Basil.Host.Cli.Commands;

namespace Basil.Host.Cli.Shutdown;

/// <summary>Asks the operator to confirm a shutdown or restart typed in the terminal.</summary>
public static class ShutdownConfirmation
{
	/// <summary>Tells whether the command stops the server and has not already been confirmed with <c>--now</c>.</summary>
	public static bool Needed(ParsedCommand parsed)
	{
		return parsed.Command.Words[0] is "shutdown" or "restart" && !parsed.Options.ContainsKey("now");
	}

	/// <summary>Asks the question for the command and reads the answer; only "y" and "yes" confirm.</summary>
	public static async Task<bool> AskAsync(ParsedCommand parsed, TextWriter output, TextReader input)
	{
		await output.WriteAsync($"Really {(parsed.Command.Words[0] == "restart" ? "restart" : "shut down")} the server? (y/N) ");
		await output.FlushAsync();
		var answer = await input.ReadLineAsync();
		return answer is not null && (answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
		                              answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));
	}
}