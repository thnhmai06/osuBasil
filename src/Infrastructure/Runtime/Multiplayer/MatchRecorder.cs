using System.Threading.Channels;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Infrastructure.Runtime.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Multiplayer;

internal sealed class MatchRecorder(
	IMatchRepository matches,
	IRoundRepository rounds,
	IMatchEventRepository matchEvents,
	TimeProvider time,
	ILogger<MatchRecorder> logger) : BackgroundService, IEventHandler<RoomEvent>, IEventHandler<LobbyEvent>
{
	private readonly Channel<Func<CancellationToken, Task>> _writes = Channel.CreateBounded<Func<CancellationToken, Task>>(
		new BoundedChannelOptions(1024)
		{
			SingleReader = true,
			FullMode = BoundedChannelFullMode.Wait
		});

	/// <inheritdoc />
	public async ValueTask HandleAsync(RoomEvent @event, CancellationToken cancellationToken)
	{
		if (ProgressOf(@event) is { Completed: true } progress)
			await EnqueueRoundAsync(progress.Round, cancellationToken);

		switch (@event)
		{
			case RoomSettingsChanged e when e.Change.Name is not null || e.Change.IsPrivate is not null:
				await EnqueueAsync(ct => matches.CreateOrUpdateAsync(e.Room.Match, ct), cancellationToken);
				break;
			case RoomRoundStarted or RoomRoundAborted or RoomRoundCompleted:
				await EnqueueRoundAsync(((RoomRoundEvent)@event).Round, cancellationToken);
				break;
			case RoomPlayerJoined e:
				await EnqueueMatchEventAsync(e.Room.Match, MatchEventType.PlayerJoined, target: e.Player.User,
					cancellationToken: cancellationToken);
				break;
			case RoomPlayerLeft e:
				await EnqueueMatchEventAsync(e.Room.Match, MatchEventType.PlayerLeft, target: e.Player.User,
					cancellationToken: cancellationToken);
				break;
			case RoomPlayerKicked e:
				await EnqueueMatchEventAsync(e.Room.Match, MatchEventType.Kicked, e.By, e.Player.User, "Kicked",
					cancellationToken);
				break;
			case RoomPlayerBanned e:
				await EnqueueMatchEventAsync(e.Room.Match, MatchEventType.Kicked, e.By, e.Player, "Banned",
					cancellationToken);
				break;
			case RoomHostChanged { Host: { } host } e:
				await EnqueueMatchEventAsync(e.Room.Match, MatchEventType.HostGranted, e.By, host.User,
					cancellationToken: cancellationToken);
				break;
			case RoomRefereeAdded e:
				await EnqueueMatchEventAsync(e.Room.Match, MatchEventType.RefAdded, e.By, e.Referee,
					cancellationToken: cancellationToken);
				break;
			case RoomRefereeRemoved e:
				await EnqueueMatchEventAsync(e.Room.Match, MatchEventType.RefRemoved, e.By, e.Referee,
					cancellationToken: cancellationToken);
				break;
		}
	}

	/// <summary>Gets what a departure or arrival did to the round in progress, when the event reports it.</summary>
	private static RoomRoundProgress? ProgressOf(RoomEvent @event)
	{
		return @event switch
		{
			RoomPlayerJoined e => e.RoundProgress,
			RoomPlayerLeft e => e.RoundProgress,
			RoomPlayerKicked e => e.RoundProgress,
			RoomPlayerBanned e => e.RoundProgress,
			RoomSlotLockChanged e => e.RoundProgress,
			_ => null
		};
	}

	/// <inheritdoc />
	public async ValueTask HandleAsync(LobbyEvent @event, CancellationToken cancellationToken)
	{
		switch (@event)
		{
			case LobbyRoomOpened e:
				await EnqueueMatchEventAsync(e.Room.Match, MatchEventType.Created, e.Room.Authority.Creator,
					cancellationToken: cancellationToken);
				break;
			case LobbyRoomClosed e:
				if (e.AbortedRound is { } abortedRound)
					await EnqueueRoundAsync(abortedRound, cancellationToken);
				await EnqueueAsync(ct => matches.CreateOrUpdateAsync(e.Room.Match, ct), cancellationToken);
				await EnqueueMatchEventAsync(e.Room.Match, MatchEventType.Closed, e.By, detail: e.By is null ? "Empty" : null,
					cancellationToken: cancellationToken);
				break;
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await foreach (var write in _writes.Reader.ReadAllAsync(stoppingToken))
		{
			for (var attempt = 1; attempt <= 3; attempt++)
			{
				try
				{
					await write(stoppingToken);
					break;
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					return;
				}
				catch (Exception exception)
				{
					if (attempt == 3)
						logger.LogError(exception, "Could not record a multiplayer change after {AttemptCount} attempts.", attempt);
					else
						await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), time, stoppingToken);
				}
			}
		}
	}

	private ValueTask EnqueueRoundAsync(Round round, CancellationToken cancellationToken)
	{
		return EnqueueAsync(ct => rounds.CreateOrUpdateAsync(round, ct), cancellationToken);
	}

	private ValueTask EnqueueMatchEventAsync(
		Match match,
		MatchEventType type,
		User? actor = null,
		User? target = null,
		string? detail = null,
		CancellationToken cancellationToken = default)
	{
		var matchEvent = new MatchEvent(match, type, time.GetUtcNow(), actor, target, detail);
		return EnqueueAsync(ct => matchEvents.CreateAsync(matchEvent, ct), cancellationToken);
	}

	private ValueTask EnqueueAsync(Func<CancellationToken, Task> write, CancellationToken cancellationToken)
	{
		return _writes.Writer.WriteAsync(write, cancellationToken);
	}
}
