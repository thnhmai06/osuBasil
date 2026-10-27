using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Multiplayer.Runtime;

/// <summary>Records what happens in rooms and the lobby as matches, rounds and match events.</summary>
internal sealed class MatchRecorder(
	IMatchRepository matches,
	IRoundRepository rounds,
	IMatchEventRepository matchEvents,
	IRoundScoreRepository roundScores) : INotificationHandler<LobbyRoomOpened>,
	INotificationHandler<LobbyRoomClosed>,
	INotificationHandler<LobbyMatchRecovered>,
	INotificationHandler<RoomSettingsChanged>,
	INotificationHandler<RoomRoundStarted>,
	INotificationHandler<RoomRoundAborted>,
	INotificationHandler<RoomRoundCompleted>,
	INotificationHandler<RoomRoundScoreSubmitted>,
	INotificationHandler<RoomRoundDecided>,
	INotificationHandler<RoomPlayerJoined>,
	INotificationHandler<RoomPlayerLeft>,
	INotificationHandler<RoomPlayerKicked>,
	INotificationHandler<RoomPlayerBanned>,
	INotificationHandler<RoomSlotLockChanged>,
	INotificationHandler<RoomHostChanged>,
	INotificationHandler<RoomRefereeAdded>,
	INotificationHandler<RoomRefereeRemoved>
{
	/// <inheritdoc />
	public async ValueTask Handle(LobbyMatchRecovered @event, CancellationToken cancellationToken)
	{
		foreach (var round in @event.AbortedRounds)
			await rounds.CreateOrUpdateAsync(round, cancellationToken);
		await matches.UpdateAsync(@event.Match, cancellationToken);
		await RecordMatchEventAsync(@event.Match, MatchEventType.Closed, @event.Timestamp,
			detail: "Server shutdown recovery", cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(LobbyRoomClosed @event, CancellationToken cancellationToken)
	{
		if (@event.AbortedRound is { } abortedRound)
			await rounds.CreateOrUpdateAsync(abortedRound, cancellationToken);
		await matches.UpdateAsync(@event.Room.Match, cancellationToken);
		await RecordMatchEventAsync(@event.Room.Match, MatchEventType.Closed, @event.Timestamp, @event.By,
			detail: @event.By is null ? "Empty" : null, cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(LobbyRoomOpened @event, CancellationToken cancellationToken)
	{
		await RecordMatchEventAsync(@event.Room.Match, MatchEventType.Created, @event.Timestamp,
			@event.Room.Authority.Creator, cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomHostChanged @event, CancellationToken cancellationToken)
	{
		if (@event.Host is { } host)
			await RecordMatchEventAsync(@event.Room.Match, MatchEventType.HostGranted, @event.Timestamp, @event.By,
				host.Record, cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomPlayerBanned @event, CancellationToken cancellationToken)
	{
		await RecordProgressAsync(@event.RoundProgress, cancellationToken);
		await RecordMatchEventAsync(@event.Room.Match, MatchEventType.Kicked, @event.Timestamp, @event.By,
			@event.Player, "Banned", cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomPlayerJoined @event, CancellationToken cancellationToken)
	{
		await RecordProgressAsync(@event.RoundProgress, cancellationToken);
		await RecordMatchEventAsync(@event.Room.Match, MatchEventType.PlayerJoined, @event.Timestamp,
			target: @event.Player.Record, cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomPlayerKicked @event, CancellationToken cancellationToken)
	{
		await RecordProgressAsync(@event.RoundProgress, cancellationToken);
		await RecordMatchEventAsync(@event.Room.Match, MatchEventType.Kicked, @event.Timestamp, @event.By,
			@event.Player.Record, "Kicked", cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomPlayerLeft @event, CancellationToken cancellationToken)
	{
		await RecordProgressAsync(@event.RoundProgress, cancellationToken);
		await RecordMatchEventAsync(@event.Room.Match, MatchEventType.PlayerLeft, @event.Timestamp,
			target: @event.Player.Record, cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRefereeAdded @event, CancellationToken cancellationToken)
	{
		await RecordMatchEventAsync(@event.Room.Match, MatchEventType.RefAdded, @event.Timestamp, @event.By,
			@event.Referee, cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRefereeRemoved @event, CancellationToken cancellationToken)
	{
		await RecordMatchEventAsync(@event.Room.Match, MatchEventType.RefRemoved, @event.Timestamp, @event.By,
			@event.Referee, cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundAborted @event, CancellationToken cancellationToken)
	{
		await rounds.CreateOrUpdateAsync(@event.Round, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundCompleted @event, CancellationToken cancellationToken)
	{
		await rounds.CreateOrUpdateAsync(@event.Round, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundDecided @event, CancellationToken cancellationToken)
	{
		await rounds.CreateOrUpdateAsync(@event.Round, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundScoreSubmitted @event, CancellationToken cancellationToken)
	{
		await roundScores.RecordAsync(@event.Score, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundStarted @event, CancellationToken cancellationToken)
	{
		await rounds.CreateOrUpdateAsync(@event.Round, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomSettingsChanged @event, CancellationToken cancellationToken)
	{
		if (@event.Change.Name is not null || @event.Change.IsPrivate is not null)
			await matches.UpdateAsync(@event.Room.Match, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomSlotLockChanged @event, CancellationToken cancellationToken)
	{
		await RecordProgressAsync(@event.RoundProgress, cancellationToken);
	}

	/// <summary>Records the round a departure or arrival ended, when the event reports it.</summary>
	private async ValueTask RecordProgressAsync(RoomRoundProgress? progress, CancellationToken cancellationToken)
	{
		if (progress is { Completed: true })
			await rounds.CreateOrUpdateAsync(progress.Round, cancellationToken);
	}

	private async ValueTask RecordMatchEventAsync(
		MatchRecord match,
		MatchEventType type,
		DateTimeOffset timestamp,
		UserRecord? actor = null,
		UserRecord? target = null,
		string? detail = null,
		CancellationToken cancellationToken = default)
	{
		var matchEvent = new MatchEvent(match, type, timestamp, actor, target, detail);
		await matchEvents.RecordAsync(matchEvent, cancellationToken);
	}
}