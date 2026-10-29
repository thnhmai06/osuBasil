using Basil.Host.Cli;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Socket;
using Basil.Infrastructure.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Host;

/// <summary>
///     Runs a console command given on the command line of the executable: forwarded to the server when it runs, directly
///     against the storage when it does not and the command allows it.
/// </summary>
public static class ShellRunner
{
	/// <summary>The hosted services an offline command needs: the write queue, the event dispatch and the storage startup.</summary>
	private static readonly string[] OfflineServices = ["DatabaseWriter", "EventDispatcher", "StorageStartup"];

	/// <summary>Tells whether the command line starts with the name of a console command.</summary>
	/// <param name="args">The command-line arguments.</param>
	public static bool IsCommand(string[] args)
	{
		if (args.Length == 0 || args[0].StartsWith('-')) return false;

		return CommandCatalogFactory.Create().All.Any(command => command.Words[0].Equals(args[0], StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Runs the command.</summary>
	/// <param name="args">The command-line arguments.</param>
	/// <param name="configure">Changes the builder before it is built; the tests use it to point at their storage.</param>
	/// <param name="output">Where the output goes (default: the console).</param>
	/// <param name="input">Where the confirmation is read from (default: the console).</param>
	/// <param name="cancellationToken">A token that cancels the command.</param>
	/// <returns>The exit code of the command.</returns>
	public static async Task<int> RunAsync(string[] args, Action<WebApplicationBuilder>? configure = null, TextWriter? output = null,
		TextReader? input = null, CancellationToken cancellationToken = default)
	{
		output ??= Console.Out;
		input ??= Console.In;
		var app = HostApplication.Create(["--nogui"], builder =>
		{
			builder.Logging.SetMinimumLevel(LogLevel.Warning);
			configure?.Invoke(builder);
		});
		await using var _ = app;
		var catalog = app.Services.GetRequiredService<CommandCatalog>();
		var (parsed, failure) = catalog.Parse(args);
		if (parsed is null)
		{
			await output.WriteLineAsync($"error: invalid: {failure!.Message}");
			return ExitCodes.Invalid;
		}

		var path = ControlSocket.PathIn(app.Services.GetRequiredService<IOptions<StorageOptions>>().Value.DataDirectory);
		using (var running = await ControlClient.ConnectAsync(path, cancellationToken))
		{
			if (running is not null)
			{
				var forwarded = args;
				if (IsShutdown(parsed) && !parsed.Options.ContainsKey("now"))
				{
					if (!await ConfirmAsync(output, input, $"Really {(parsed.Command.Words[0] == "restart" ? "restart" : "shut down")} the server?"))
					{
						await output.WriteLineAsync("Cancelled.");
						return ExitCodes.Declined;
					}

					forwarded = [.. args, "--now"];
				}

				return await running.RunAsync(forwarded, output, cancellationToken);
			}
		}

		if (!parsed.Command.Offline)
		{
			await output.WriteLineAsync("error: serverNotRunning: The server is not running; this command needs it.");
			return ExitCodes.NotRunning;
		}

		var runner = app.Services.GetRequiredService<CommandRunner>();
		if (!parsed.Command.NeedsStorage)
			return await runner.RunAsync(args, new PlainOutput(output), false, null, cancellationToken);

		var started = app.Services.GetServices<IHostedService>().Where(service => OfflineServices.Contains(service.GetType().Name)).ToList();
		var lifecycle = started.OfType<IHostedLifecycleService>().ToList();
		foreach (var service in lifecycle) await service.StartingAsync(cancellationToken);
		foreach (var service in started) await service.StartAsync(cancellationToken);
		foreach (var service in lifecycle) await service.StartedAsync(cancellationToken);
		try
		{
			return await runner.RunAsync(args, new PlainOutput(output), false, null, cancellationToken);
		}
		finally
		{
			// Stopping drains the write queue and flushes it, so a command that printed its confirmation has stored its change.
			foreach (var service in Enumerable.Reverse(lifecycle)) await service.StoppingAsync(CancellationToken.None);
			foreach (var service in Enumerable.Reverse(started)) await service.StopAsync(CancellationToken.None);
			foreach (var service in Enumerable.Reverse(lifecycle)) await service.StoppedAsync(CancellationToken.None);
		}
	}

	private static bool IsShutdown(ParsedCommand parsed)
	{
		return parsed.Command.Words[0] is "shutdown" or "restart";
	}

	private static async Task<bool> ConfirmAsync(TextWriter output, TextReader input, string question)
	{
		await output.WriteAsync(question + " (y/N) ");
		await output.FlushAsync();
		var answer = await input.ReadLineAsync();
		return answer is not null && (answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
		                              answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));
	}
}
