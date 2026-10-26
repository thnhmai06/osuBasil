using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Servers;
using Basil.Application.Runtime.Users;
using Basil.Bot.Replies;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Bot.Announcements;

/// <summary>Reacts to room and lobby events by posting announcements in the affected room.</summary>
internal sealed class RoomAnnouncer(Server server, UserRegistry users, TimeProvider timeProvider, IReplies replies)
	: INotificationHandler<LobbyRoomClosingAnnounced>,
		INotificationHandler<LobbyRoomClosed>,
		INotificationHandler<RoomRoundStarted>,
		INotificationHandler<RoomRoundAborted>,
		INotificationHandler<RoomRoundDecided>,
		INotificationHandler<RoomCountdownStarted>,
		INotificationHandler<RoomCountdownTicked>,
		INotificationHandler<RoomCountdownElapsed>,
		INotificationHandler<RoomCountdownCancelled>,
		INotificationHandler<RoomPlayerJoined>
{
	private readonly ConcurrentSet<Room> _closingRooms = [];

	/// <inheritdoc />
	public ValueTask Handle(LobbyRoomClosed @event, CancellationToken cancellationToken)
	{
		_closingRooms.Remove(@event.Room);
		return ValueTask.CompletedTask;
	}

	/// <inheritdoc />
	public async ValueTask Handle(LobbyRoomClosingAnnounced @event, CancellationToken cancellationToken)
	{
		await AnnounceClosingAsync(@event, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomCountdownCancelled @event, CancellationToken cancellationToken)
	{
		await server.SayAsync(@event.Room.Channel, replies.Text(@event.Reason switch
		{
			RoomCountdownCancelReason.SettingsChanged => AnnouncementReplies.SettingsChangedCountdownCancelled,
			RoomCountdownCancelReason.RoundStarted => AnnouncementReplies.CountdownCancelledByRoundStart,
			RoomCountdownCancelReason.RoundAborted => AnnouncementReplies.CountdownCancelledByRoundAbort,
			_ => AnnouncementReplies.CountdownCancelled
		}), cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomCountdownElapsed @event, CancellationToken cancellationToken)
	{
		var elapsed = @event.StartResult switch
		{
			RoomOutcome.Ok when @event.Countdown.StartsRound => AnnouncementReplies.RoundStartedByCountdown,
			RoomOutcome.Ok when !@event.Countdown.StartsRound => AnnouncementReplies.CountdownElapsed,
			RoomOutcome.NoBeatmap => AnnouncementReplies.CountdownElapsedForRound,
			_ => null
		};
		if (elapsed is not null)
			await server.SayAsync(@event.Room.Channel, replies.Text(elapsed), cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomCountdownStarted @event, CancellationToken cancellationToken)
	{
		var seconds = (int)@event.Countdown.Length.TotalSeconds;
		var text = @event.Countdown.StartsRound
			? replies.Text(AnnouncementReplies.CountdownStartedForRound, new { seconds })
			: replies.Text(AnnouncementReplies.CountdownStarted, new { seconds });
		await server.SayAsync(@event.Room.Channel, text, cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomCountdownTicked @event, CancellationToken cancellationToken)
	{
		var remainingSeconds = (int)Math.Round(@event.Remaining.TotalSeconds);
		var tick = @event.Countdown.StartsRound
			? replies.Text(AnnouncementReplies.CountdownTickedForRound, new { seconds = remainingSeconds })
			: replies.Text(AnnouncementReplies.CountdownTicked, new { seconds = remainingSeconds });
		await server.SayAsync(@event.Room.Channel, tick, cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomPlayerJoined @event, CancellationToken cancellationToken)
	{
		if (_closingRooms.Remove(@event.Room))
			await server.SayAsync(@event.Room.Channel, replies.Text(AnnouncementReplies.RoomPlayerJoinedClosing),
				cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundAborted @event, CancellationToken cancellationToken)
	{
		await server.SayAsync(@event.Room.Channel, replies.Text(AnnouncementReplies.RoundAborted),
			cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundDecided @event, CancellationToken cancellationToken)
	{
		var seconds = (int)RoomRounds.ScoreWait.TotalSeconds;
		foreach (var player in @event.Missing)
			await server.SayAsync(@event.Room.Channel,
				replies.Text(AnnouncementReplies.ScoreMissing, new { player = player.Value.Name, seconds }),
				cancellationToken: cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundStarted @event, CancellationToken cancellationToken)
	{
		if (@event.Players.Count == 0)
			await server.SayAsync(@event.Room.Channel, replies.Text(AnnouncementReplies.RoundStartedNoPlayers),
				cancellationToken: cancellationToken);
	}

	private async Task AnnounceClosingAsync(LobbyRoomClosingAnnounced e, CancellationToken cancellationToken)
	{
		var minutesUntilClose = (int)Math.Ceiling((e.ClosesAt - timeProvider.GetUtcNow()).TotalMinutes);
		var text = replies.Text(AnnouncementReplies.RoomClosingAnnounced, new { minutes = minutesUntilClose });

		_closingRooms.Add(e.Room);
		await server.SayAsync(e.Room.Channel, text, cancellationToken: cancellationToken);
		await WhisperStaffAsync(e.Room, text, cancellationToken);
	}

	private async Task WhisperStaffAsync(Room room, string text, CancellationToken cancellationToken)
	{
		await WhisperIfOnlineAsync(room.Authority.Creator);

		foreach (var referee in room.Authority.Referees)
			await WhisperIfOnlineAsync(referee);
		return;

		async Task WhisperIfOnlineAsync(UserRecord user)
		{
			if (users.Online.GetValueOrDefault(user) is { } online)
				await server.WhisperAsync(online, text, cancellationToken: cancellationToken);
		}
	}
}