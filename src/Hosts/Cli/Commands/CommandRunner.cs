using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Commands;

public sealed class CommandRunner(
	CommandCatalog catalog,
	IServiceProvider services,
	ViewFormatter formatter,
	ILogger<CommandRunner> logger)
{
	public CommandCatalog Catalog => catalog;

	public async Task<int> RunAsync(IReadOnlyList<string> args, ICommandOutput output, bool serverRunning,
		CommandSession? session, CancellationToken ct)
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
		var call = new CommandCall(services, output, formatter, parsed.Words, parsed.Options, ct,
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
			// The message names the rejected value for the operator; the parameter name the runtime appends is not theirs.
			var message = exception.ParamName is null
				? exception.Message
				: exception.Message.Replace($" (Parameter '{exception.ParamName}')", string.Empty, StringComparison.Ordinal);
			output.Error("invalid", message);
			return ExitCodes.Invalid;
		}
		catch (IOException exception)
		{
			output.Error("invalid", exception.Message);
			return ExitCodes.Invalid;
		}
	}
}