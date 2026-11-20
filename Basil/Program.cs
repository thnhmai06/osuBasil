using System.Net;
using Basil.Configuration;
using Basil.Host.Cli;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Shutdown;
using Basil.Lifetime;
using Microsoft.Extensions.Options;
using Velopack;

namespace Basil;

public static class Program
{
	public static async Task<int> Main(string[] args)
	{
		// Lets the updater run its install and uninstall hooks; it returns at once when Basil was started normally.
		VelopackApp.Build().Run();
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

		if (Restarter.WasRestarted())
		{
			var server = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;
			var bindings = KestrelSetup.Bindings(app.Configuration);
			await Restarter.WaitForPortsAsync(
				() => bindings.All(binding => Restarter.CanBind(binding.Address, binding.Port)) &&
				      Restarter.CanBind(IPAddress.Any, server.Irc.Port),
				TimeProvider.System, TimeSpan.FromSeconds(10));
		}

		// Resolved before the run: the services are disposed once the host has stopped.
		var control = app.Services.GetRequiredService<ShutdownController>();
		await app.RunAsync();
		if (control is { RestartRequested: true } && !Restarter.UnderManager())
			Restarter.Start(args);

		return control.ExitCode;
	}
}