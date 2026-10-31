using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Commands;

/// <summary>
///     Runs console commands: matches the line, runs the command as the operator and turns problems into an exit
///     code.
/// </summary>
/// <param name="catalog">The commands.</param>
/// <param name="services">The services of the server the commands act on.</param>
/// <param name="formatter">The formatter of record views.</param>
/// <param name="logger">Where each command is recorded, with its secrets hidden.</param>
public sealed class CommandRunner(
	CommandCatalog catalog,
	IServiceProvider services,
	ViewFormatter formatter,
	ILogger<CommandRunner> logger)
{
	/// <summary>Gets the commands this runner knows.</summary>
	public CommandCatalog Catalog => catalog;

	/// <summary>Runs a command line.</summary>
	/// <param name="args">The words of the command line.</param>
	/// <param name="output">Where the command prints.</param>
	/// <param name="serverRunning">Whether the server is running; commands that need it are refused when it is not.</param>
	/// <param name="session">Where the command was typed; <see langword="null" /> for a shell or a test.</param>
	/// <param name="cancellationToken">A token that cancels the command.</param>
	/// <returns>The exit code of the command.</returns>
	public async Task<int> RunAsync(IReadOnlyList<string> args, ICommandOutput output, bool serverRunning,
		CommandSession? session, CancellationToken cancellationToken)
	{
		var (parsed, failure) = catalog.Parse(args);
		if (parsed is null)
		{
			output.Error("invalid", failure!.Message);
			return ExitCodes.Invalid;
		}

		var command = parsed.Command;
		if (command.DashboardOnly && session?.Dashboard != true)
		{
			output.Error("invalid", $"{command.Words[0]} is only available in the dashboard.");
			return ExitCodes.Invalid;
		}

		if (!serverRunning && !command.Offline)
		{
			output.Error("serverNotRunning", "The server is not running; this command needs it.");
			return ExitCodes.NotRunning;
		}

		logger.LogInformation("Console command: {Command}", Redactor.Redact(catalog, args));
		var call = new CommandCall(services, output, formatter, parsed.Words, parsed.Options, cancellationToken,
			session);
		try
		{
			return await command.Run(call);
		}
		catch (CommandException exception)
		{
			output.Error(exception.Code, exception.Message);
			return exception.ExitCode;
		}
		catch (ArgumentException exception)
		{
			output.Error("invalid", exception.Message);
			return ExitCodes.Invalid;
		}
		catch (IOException exception)
		{
			output.Error("invalid", exception.Message);
			return ExitCodes.Invalid;
		}
	}
}