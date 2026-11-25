using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Socket;
using Basil.Infrastructure.Shared.Storage;

namespace Basil.Lifetime;

public static class ShellRunner
{
	private static readonly string[] OfflineServices = ["EventDispatcher", "StorageStartup"];

	public static bool IsCommand(string[] args)
	{
		if (args.Length == 0 || args[0].StartsWith('-')) return false;

		return CommandCatalogFactory.Create().All
			.Any(command => command.Words[0].Equals(args[0], StringComparison.OrdinalIgnoreCase));
	}

	// The first argument the server would not understand, or null when every one is a known option, an option's value or a
	// setting such as --Basil:Server:Domain=example.org.
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

	public static async Task<int> RunAsync(string[] args, Action<WebApplicationBuilder>? configure = null,
		TextWriter? output = null,
		TextReader? input = null, CancellationToken ct = default)
	{
		output ??= Console.Out;
		input ??= Console.In;
		var app = HostApplication.Create(["--console", "0"], builder =>
		{
			builder.Logging.SetMinimumLevel(LogLevel.Warning);
			configure?.Invoke(builder);
		}, true);
		await using var _ = app;
		var catalog = app.Services.GetRequiredService<CommandCatalog>();
		var (parsed, failure) = catalog.Parse(args);
		if (parsed is null)
		{
			await output.WriteLineAsync($"error: {failure!.Message}");
			return ExitCodes.Invalid;
		}

		var path = ControlSocket.PathIn(app.Services.GetRequiredService<DataPaths>().Temp);
		using (var running = await ControlClient.ConnectAsync(path, ct))
		{
			if (running is not null) return await ForwardAsync(running, parsed, args, output, input, ct);
		}

		if (!parsed.Command.Offline)
		{
			await output.WriteLineAsync(
				$"error: The server could not be reached at {path}. Start it there (run ./Basil, or docker compose up -d) and run the command again.");
			return ExitCodes.NotRunning;
		}

		var runner = app.Services.GetRequiredService<CommandRunner>();
		if (!parsed.Command.NeedsStorage)
			return await runner.RunAsync(args, new PlainOutput(output), false, null, ct);

		var started = app.Services.GetServices<IHostedService>()
			.Where(service => OfflineServices.Contains(service.GetType().Name)).ToList();
		return await RunWithServicesAsync(runner, started, args, output, ct);
	}

	private static async Task<int> ForwardAsync(ControlConnection running, ParsedCommand parsed, string[] args,
		TextWriter output,
		TextReader input, CancellationToken ct)
	{
		var forwarded = args;
		if (IsShutdown(parsed) && !parsed.Options.ContainsKey("now"))
		{
			if (!await ConfirmAsync(output, input,
				    $"Really {(parsed.Command.Words[0] == "restart" ? "restart" : "shut down")} the server?"))
			{
				await output.WriteLineAsync("Cancelled.");
				return ExitCodes.Declined;
			}

			forwarded = [.. args, "--now"];
		}

		return await running.RunAsync(forwarded, output, ct);
	}

	private static async Task<int> RunWithServicesAsync(CommandRunner runner, List<IHostedService> started,
		string[] args,
		TextWriter output, CancellationToken ct)
	{
		var lifecycle = started.OfType<IHostedLifecycleService>().ToList();
		foreach (var service in lifecycle) await service.StartingAsync(ct);
		foreach (var service in started) await service.StartAsync(ct);
		foreach (var service in lifecycle) await service.StartedAsync(ct);
		try
		{
			return await runner.RunAsync(args, new PlainOutput(output), false, null, ct);
		}
		finally
		{
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