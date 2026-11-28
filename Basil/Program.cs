using Basil.Configuration;
using Basil.Host.Cli;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Shutdown;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Lifetime;

namespace Basil;

public static class Program
{
	public static async Task<int> Main(string[] args)
	{
		if (args.Any(arg => arg is "--help" or "-h")) return await ShellRunner.RunAsync(["help"]);

		if (CommandLineArguments.IsCommand(args)) return await ShellRunner.RunAsync(args);

		// Before anything starts: a mistyped command must not start a second server on the live database.
		if (CommandLineArguments.Unknown(args) is { } unknown)
		{
			await Console.Error.WriteLineAsync(
				$"error: Unknown {(unknown.StartsWith('-') ? "option" : "command")}: {unknown}. Run Basil --help for the list.");
			return ExitCodes.Invalid;
		}

		WebApplication app;
		try
		{
			app = HostApplication.Create(args);
		}
		catch (CliOptionException exception)
		{
			await Console.Error.WriteLineAsync($"error: {exception.Message}");
			return ExitCodes.Invalid;
		}
		catch (DataLayoutException exception)
		{
			await Console.Error.WriteLineAsync($"error: {exception.Message}");
			return ExitCodes.Invalid;
		}
		catch (MissingConfigurationException exception)
		{
			await Console.Error.WriteLineAsync($"error: {exception.Message}");
			return ExitCodes.Invalid;
		}

		// Resolved before the run: the services are disposed once the host has stopped.
		var control = app.Services.GetRequiredService<ShutdownController>();
		await app.RunAsync();
		return control.ExitCode;
	}
}