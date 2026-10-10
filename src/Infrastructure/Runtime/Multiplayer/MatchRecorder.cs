using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Domain.Multiplayer.Match;
using Basil.Domain.Multiplayer.Round;
using Basil.Domain.Users;
using Basil.Infrastructure.Runtime.Events;

namespace Basil.Infrastructure.Runtime.Multiplayer;

/// <summary>Records what happens in rooms and the lobby as matches, rounds and match events.</summary>
internal sealed class MatchRecorder(
	IMatchRepository matches,
	IRoundRepository rounds,
	IMatchEventRepository matchEvents) : IEventHandler<RoomEvent>, IEventHandler<LobbyEvent>
{
	/// <inheritdoc />
	public async ValueTask HandleAsync(LobbyEvent @event, CancellationToken cancellationToken)
	{
		switch (@event)
		{
			case LobbyRoomOpened e:
				await RecordMatchEventAsync(e.Room.Match, MatchEventType.Created, @event.Timestamp,
					e.Room.Authority.Creator, cancellationToken: cancellationToken);
				break;
			case LobbyRoomClosed e:
				if (e.AbortedRound is { } abortedRound)
					await rounds.CreateOrUpdateAsync(abortedRound, cancellationToken);
				await matches.CreateOrUpdateAsync(e.Room.Match, cancellationToken);
				await RecordMatchEventAsync(e.Room.Match, MatchEventType.Closed, @event.Timestamp, e.By,
					detail: e.By is null ? "Empty" : null, cancellationToken: cancellationToken);
				break;
		}
	}

	/// <inheritdoc />
	public async ValueTask HandleAsync(RoomEvent @event, CancellationToken cancellationToken)
	{
		if (ProgressOf(@event) is { Completed: true } progress)
			await rounds.CreateOrUpdateAsync(progress.Round, cancellationToken);

		switch (@event)
		{
			case RoomSettingsChanged e when e.Change.Name is not null || e.Change.IsPrivate is not null:
				await matches.CreateOrUpdateAsync(e.Room.Match, cancellationToken);
				break;
			case RoomRoundStarted or RoomRoundAborted or RoomRoundCompleted:
				await rounds.CreateOrUpdateAsync(((RoomRoundEvent)@event).Round, cancellationToken);
				break;
			case RoomPlayerJoined e:
				await RecordMatchEventAsync(e.Room.Match, MatchEventType.PlayerJoined, @event.Timestamp,
					target: e.Player.User, cancellationToken: cancellationToken);
				break;
			case RoomPlayerLeft e:
				await RecordMatchEventAsync(e.Room.Match, MatchEventType.PlayerLeft, @event.Timestamp,
					target: e.Player.User, cancellationToken: cancellationToken);
				break;
			case RoomPlayerKicked e:
				await RecordMatchEventAsync(e.Room.Match, MatchEventType.Kicked, @event.Timestamp, e.By, e.Player.User,
					"Kicked", cancellationToken);
				break;
			case RoomPlayerBanned e:
				await RecordMatchEventAsync(e.Room.Match, MatchEventType.Kicked, @event.Timestamp, e.By, e.Player,
					"Banned", cancellationToken);
				break;
			case RoomHostChanged { Host: { } host } e:
				await RecordMatchEventAsync(e.Room.Match, MatchEventType.HostGranted, @event.Timestamp, e.By, host.User,
					cancellationToken: cancellationToken);
				break;
			case RoomRefereeAdded e:
				await RecordMatchEventAsync(e.Room.Match, MatchEventType.RefAdded, @event.Timestamp, e.By, e.Referee,
					cancellationToken: cancellationToken);
				break;
			case RoomRefereeRemoved e:
				await RecordMatchEventAsync(e.Room.Match, MatchEventType.RefRemoved, @event.Timestamp, e.By, e.Referee,
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

	private async ValueTask RecordMatchEventAsync(
		Match match,
		MatchEventType type,
		DateTimeOffset timestamp,
		User? actor = null,
		User? target = null,
		string? detail = null,
		CancellationToken cancellationToken = default)
	{
		var matchEvent = new MatchEvent(match, type, timestamp, actor, target, detail);
		await matchEvents.CreateAsync(matchEvent, cancellationToken);
	}
}