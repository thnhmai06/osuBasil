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
		if (ShellRunner.IsCommand(args)) return await ShellRunner.RunAsync(args);

		WebApplication app;
		try
		{
			app = HostApplication.Create(args);
		}
		catch (CliOptionException exception)
		{
			await Console.Error.WriteLineAsync($"error: invalid: {exception.Message}");
			return ExitCodes.Invalid;
		}
		catch (DataLayoutException exception)
		{
			await Console.Error.WriteLineAsync($"error: invalid: {exception.Message}");
			return ExitCodes.Invalid;
		}
		catch (MissingConfigurationException exception)
		{
			await Console.Error.WriteLineAsync($"error: invalid: {exception.Message}");
			return ExitCodes.Invalid;
		}

		// Resolved before the run: the services are disposed once the host has stopped.
		var control = app.Services.GetRequiredService<ShutdownController>();
		await app.RunAsync();
		return control.ExitCode;
	}
}