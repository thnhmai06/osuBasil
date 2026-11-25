using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Socket;
using Basil.Host.Cli.Shutdown;
using Basil.Infrastructure.Shared.Storage;
using Basil.Infrastructure.Shared.Workers;

namespace Basil.Lifetime;

public static class ShellRunner
{
	private static readonly string[] OfflineServices = [nameof(EventDispatcher), nameof(StorageStartup)];

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
		if (ShutdownConfirmation.Needed(parsed))
		{
			if (!await ShutdownConfirmation.AskAsync(parsed, output, input))
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
}