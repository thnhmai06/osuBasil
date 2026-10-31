using Basil.Application.Common.Events;
using Basil.Application.Multiplayer.Capabilities;
using Basil.Application.Multiplayer.Registry;
using Basil.Host.Bancho.Support;
using Basil.Host.Cli.Shutdown;
using Basil.Host.Irc;
using Basil.Host.Logging;
using Basil.Host.Rest.Sse;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.Extensions.Hosting;

namespace Basil.Host.Lifetime;

/// <summary>The time limits of the steps of the graceful shutdown.</summary>
public sealed class ShutdownTimings
{
	/// <summary>Gets how long the whole host may take to stop, which is longer than all the steps together.</summary>
	public static readonly TimeSpan Host = TimeSpan.FromSeconds(75);

	/// <summary>Gets the time limit of refusing new work.</summary>
	public TimeSpan StopAccepting { get; init; } = TimeSpan.FromSeconds(2);

	/// <summary>Gets how long the polls of the osu! clients are waited for so that they take the restart packet.</summary>
	public TimeSpan OsuPollWindow { get; init; } = TimeSpan.FromSeconds(5);

	/// <summary>Gets the time limit of telling the osu! clients, which includes <see cref="OsuPollWindow" />.</summary>
	public TimeSpan AnnounceOsu { get; init; } = TimeSpan.FromSeconds(8);

	/// <summary>Gets the time limit of telling the IRC and HTTP clients.</summary>
	public TimeSpan AnnouncePush { get; init; } = TimeSpan.FromSeconds(3);

	/// <summary>Gets the time limit of closing the rooms.</summary>
	public TimeSpan CloseRooms { get; init; } = TimeSpan.FromSeconds(5);

	/// <summary>Gets the time limit of delivering the events that are queued.</summary>
	public TimeSpan DrainEvents { get; init; } = TimeSpan.FromSeconds(10);

	/// <summary>Gets the time limit of storing the writes that are queued.</summary>
	public TimeSpan DrainWrites { get; init; } = TimeSpan.FromSeconds(20);

	/// <summary>Gets the time limit of writing the log events that are queued.</summary>
	public TimeSpan FlushLogs { get; init; } = TimeSpan.FromSeconds(3);
}

/// <summary>
///     Stops the server in order while it still serves: refuses new work, tells every client, closes the rooms, and waits
///     until the events and the writes they caused are stored, then lets the host close its listeners and services.
/// </summary>
/// <remarks>
///     It runs in <see cref="IHostedLifecycleService.StoppingAsync" />, which the host calls for every service before it
///     stops any of them, so the web server still answers the polls of the osu! clients while they are told.
/// </remarks>
internal sealed class GracefulShutdown(
	ShutdownSequence sequence,
	ShutdownController control,
	BanchoShutdown bancho,
	IrcListener irc,
	IrcState ircState,
	SseHub sse,
	Lobby lobby,
	EventQueue events,
	DatabaseWriter writes,
	LogDrops logs,
	ILogger<GracefulShutdown> logger,
	ShutdownTimings? timings = null) : IHostedLifecycleService
{
	private readonly ShutdownTimings _timings = timings ?? new ShutdownTimings();

	/// <summary>Gets what each step did, once the shutdown ran.</summary>
	public IReadOnlyList<ShutdownStepResult> Results { get; private set; } = [];

	/// <summary>Creates the steps for a shutdown or a restart.</summary>
	/// <param name="restarting">Whether the server comes back.</param>
	internal IReadOnlyList<ShutdownStep> Steps(bool restarting)
	{
		return
		[
			new ShutdownStep("stop accepting new work", _timings.StopAccepting, _ =>
			{
				ircState.AcceptingLogins = false;
				sse.StopAccepting();
				bancho.StopAccepting();
				return Task.CompletedTask;
			}),
			new ShutdownStep("tell the osu! clients", _timings.AnnounceOsu,
				async token => await bancho.AnnounceAsync(TimeSpan.Zero, _timings.OsuPollWindow, token)),
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
			new ShutdownStep("store the queued writes", _timings.DrainWrites, token => writes.DrainAsync(token)),
			new ShutdownStep("write the queued log events", _timings.FlushLogs, async token =>
			{
				while (logs.Pending > 0) await Task.Delay(10, token);
			})
		];
	}

	/// <inheritdoc />
	public async Task StoppingAsync(CancellationToken cancellationToken)
	{
		var restarting = control.RestartRequested;
		logger.LogInformation(restarting ? "Restarting the server" : "Shutting down the server");
		Results = await sequence.RunAsync(Steps(restarting), cancellationToken);
	}

	/// <inheritdoc />
	public Task StartingAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StartedAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StoppedAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
