using System.Globalization;
using Basil.Application.Users.Messages;
using Basil.Host.Shared.Errors;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Basil.Domain;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Cli.Commands;

public sealed class CommandRunner(
	CommandCatalog catalog,
	IServiceProvider services,
	ViewFormatter formatter,
	ILogger<CommandRunner> logger)
{
	public CommandCatalog Catalog => catalog;

	// "users alice sessions list" runs as "users 7 sessions list": an account can be named instead of numbered.
	private async Task<IReadOnlyList<string>?> NamedUserAsync(IReadOnlyList<string> args, CancellationToken ct)
	{
		if (args.Count < 2 || !args[0].Equals("users", StringComparison.OrdinalIgnoreCase) ||
		    CommandCatalog.IsOption(args[1]) ||
		    int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
		    catalog.All.Any(command => command.Words.Length > 1 && !CommandDef.IsParameter(command.Words[1]) &&
		                               command.Words[0].Equals("users", StringComparison.OrdinalIgnoreCase) &&
		                               command.Words[1].Equals(args[1], StringComparison.OrdinalIgnoreCase)))
			return null;

		try
		{
			return await services.GetRequiredService<ISender>().Send(new FindUserByName(args[1]), ct) is { } record
				? [args[0], record.Id.ToString(CultureInfo.InvariantCulture), .. args.Skip(2)]
				: throw CommandException.NotFound($"No such user: {args[1]}.");
		}
		catch (Exception exception) when (exception is not (OperationCanceledException or CommandException))
		{
			// The name could not be looked up; the command then fails to parse as it would have.
			logger.LogDebug(exception, "The user {Name} could not be looked up", args[1]);
			return null;
		}
	}

	public async Task<int> RunAsync(IReadOnlyList<string> args, ICommandOutput output, bool serverRunning,
		CommandSession? session, CancellationToken ct)
	{
		try
		{
			if (await NamedUserAsync(args, ct) is { } named) args = named;
		}
		catch (CommandException exception)
		{
			output.Error(exception.Code, exception.Message);
			return exception.ExitCode;
		}

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
		catch (BasilException error)
		{
			output.Error(BusinessErrors.Describe(error).Code, error.Message);
			return ExitCodes.Invalid;
		}
		catch (ArgumentException exception)
		{
			// The message names the rejected value for the operator; the parameter name the runtime appends is not theirs.
			var message = exception.ParamName is null
				? exception.Message
				: exception.Message.Replace($" (Parameter '{exception.ParamName}')", string.Empty,
					StringComparison.Ordinal);
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