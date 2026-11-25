using Basil.Application.Servers.Objects;
using Basil.Infrastructure.Shared.Events;
using Basil.Host.Irc.Clients;
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
	StableShutdown stable,
	IrcListener irc,
	IrcState ircState,
	SseHub sse,
	Server server,
	EventQueue events,
	LogDrops logs,
	ILogger<GracefulShutdown> logger,
	ShutdownTimings? timings = null) : IHostedLifecycleService
{
	private readonly ShutdownTimings _timings = timings ?? new ShutdownTimings();

	public IReadOnlyList<ShutdownStepResult> Results { get; private set; } = [];

	public async Task StoppingAsync(CancellationToken ct)
	{
		logger.LogInformation("Shutting down the server");
		Results = await sequence.RunAsync(Steps(), ct);
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

	internal IReadOnlyList<ShutdownStep> Steps()
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
				irc.Announce();
				sse.Announce();
				return Task.CompletedTask;
			}),
			new ShutdownStep("close the rooms", _timings.CloseRooms, async token =>
			{
				var closed = await server.ShutdownAsync(token);
				logger.LogInformation("Closed {RoomCount} rooms", closed);
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