using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Events;
using Basil.Host.Cli.Shutdown;
using Basil.Host.Irc.Session;
using Basil.Host.Irc.Transport;
using Basil.Host.Rest.Sse;
using Basil.Host.Stable.Support;
using Basil.Logging;

namespace Basil.Lifetime;

public sealed class ShutdownTimings
{
	public static readonly TimeSpan Host = TimeSpan.FromSeconds(75);

	public TimeSpan StopAccepting { get; init; } = TimeSpan.FromSeconds(2);

	public TimeSpan OsuPollWindow { get; init; } = TimeSpan.FromSeconds(5);

	public TimeSpan AnnounceOsu { get; init; } = TimeSpan.FromSeconds(8);

	public TimeSpan AnnouncePush { get; init; } = TimeSpan.FromSeconds(3);

	public TimeSpan CloseRooms { get; init; } = TimeSpan.FromSeconds(5);

	public TimeSpan DrainEvents { get; init; } = TimeSpan.FromSeconds(10);

	public TimeSpan FlushLogs { get; init; } = TimeSpan.FromSeconds(3);
}

internal sealed class GracefulShutdown(
	ShutdownSequence sequence,
	ShutdownController control,
	StableShutdown stable,
	IrcListener irc,
	IrcState ircState,
	SseHub sse,
	Lobby lobby,
	EventQueue events,
	LogDrops logs,
	ILogger<GracefulShutdown> logger,
	ShutdownTimings? timings = null) : IHostedLifecycleService
{
	private readonly ShutdownTimings _timings = timings ?? new ShutdownTimings();

	public IReadOnlyList<ShutdownStepResult> Results { get; private set; } = [];

	public async Task StoppingAsync(CancellationToken ct)
	{
		var restarting = control.RestartRequested;
		logger.LogInformation("{Msg}", restarting ? "Restarting the server" : "Shutting down the server");
		Results = await sequence.RunAsync(Steps(restarting), ct);
	}

	public Task StartingAsync(CancellationToken ct)
	{
		return Task.CompletedTask;
	}

	public Task StartAsync(CancellationToken ct)
	{
		return Task.CompletedTask;
	}

	public Task StartedAsync(CancellationToken ct)
	{
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken ct)
	{
		return Task.CompletedTask;
	}

	public Task StoppedAsync(CancellationToken ct)
	{
		return Task.CompletedTask;
	}

	internal IReadOnlyList<ShutdownStep> Steps(bool restarting)
	{
		return
		[
			new ShutdownStep("stop accepting new work", _timings.StopAccepting, _ =>
			{
				ircState.AcceptingLogins = false;
				sse.StopAccepting();
				stable.StopAccepting();
				return Task.CompletedTask;
			}),
			new ShutdownStep("tell the osu! clients", _timings.AnnounceOsu,
				async token => await stable.AnnounceAsync(TimeSpan.Zero, _timings.OsuPollWindow, token)),
			new ShutdownStep("tell the IRC and HTTP clients", _timings.AnnouncePush, _ =>
			{
				irc.Announce(restarting);
				sse.Announce(restarting);
				return Task.CompletedTask;
			}),
			new ShutdownStep("close the rooms", _timings.CloseRooms, _ =>
			{
				var closed = lobby.CloseAll();
				logger.LogInformation("Closed {RoomCount} rooms", closed);
				return Task.CompletedTask;
			}),
			new ShutdownStep("deliver the queued events", _timings.DrainEvents, async token =>
			{
				while (events.Outstanding > 0) await Task.Delay(10, token);
			}),
			new ShutdownStep("write the queued log events", _timings.FlushLogs, async token =>
			{
				while (logs.Pending > 0) await Task.Delay(10, token);
			})
		];
	}
}