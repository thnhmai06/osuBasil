using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Servers;
using Basil.Application.Runtime.Users;
using Basil.Bot.Replies;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Bot.Announcements;

/// <summary>Reacts to room and lobby events by posting announcements in the affected room.</summary>
internal sealed class RoomAnnouncer(Server server, UserRegistry users, TimeProvider timeProvider, IReplies replies)
	: IEventHandler<RoomEvent>, IEventHandler<LobbyEvent>
{
	private readonly ConcurrentSet<Room> _closingRooms = [];

	/// <inheritdoc />
	public async ValueTask HandleAsync(RoomEvent @event, CancellationToken cancellationToken)
	{
		switch (@event)
		{
			case RoomRoundStarted { Players.Count: 0 } e:
				await server.SayAsync(e.Room.Channel, replies.Text(AnnouncementReplies.RoundStartedNoPlayers),
					cancellationToken: cancellationToken);
				break;
			case RoomRoundAborted e:
				await server.SayAsync(e.Room.Channel, replies.Text(AnnouncementReplies.RoundAborted),
					cancellationToken: cancellationToken);
				break;
			case RoomCountdownStarted e:
				var seconds = (int)e.Countdown.Length.TotalSeconds;
				var text = e.Countdown.StartsRound
					? replies.Text(AnnouncementReplies.CountdownStartedForRound, new { seconds })
					: replies.Text(AnnouncementReplies.CountdownStarted, new { seconds });
				await server.SayAsync(e.Room.Channel, text, cancellationToken: cancellationToken);
				break;
			case RoomCountdownTicked e:
				var remainingSeconds = (int)Math.Round(e.Remaining.TotalSeconds);
				var tick = e.Countdown.StartsRound
					? replies.Text(AnnouncementReplies.CountdownTickedForRound, new { seconds = remainingSeconds })
					: replies.Text(AnnouncementReplies.CountdownTicked, new { seconds = remainingSeconds });
				await server.SayAsync(e.Room.Channel, tick, cancellationToken: cancellationToken);
				break;
			case RoomCountdownElapsed e:
				var elapsed = e.StartResult switch
				{
					RoomOutcome.Ok when e.Countdown.StartsRound =>
						AnnouncementReplies.RoundStartedByCountdown,
					RoomOutcome.Ok when !e.Countdown.StartsRound => AnnouncementReplies.CountdownElapsed,
					RoomOutcome.NoBeatmap => AnnouncementReplies.CountdownElapsedForRound,
					_ => null
				};
				if (elapsed is not null)
					await server.SayAsync(e.Room.Channel, replies.Text(elapsed), cancellationToken: cancellationToken);
				break;
			case RoomCountdownCancelled e:
				await server.SayAsync(e.Room.Channel, replies.Text(e.Reason switch
				{
					RoomCountdownCancelReason.SettingsChanged => AnnouncementReplies.SettingsChangedCountdownCancelled,
					RoomCountdownCancelReason.RoundStarted => AnnouncementReplies.CountdownCancelledByRoundStart,
					RoomCountdownCancelReason.RoundAborted => AnnouncementReplies.CountdownCancelledByRoundAbort,
					_ => AnnouncementReplies.CountdownCancelled
				}), cancellationToken: cancellationToken);
				break;
			case RoomPlayerJoined e when _closingRooms.Remove(e.Room):
				await server.SayAsync(e.Room.Channel, replies.Text(AnnouncementReplies.RoomPlayerJoinedClosing),
					cancellationToken: cancellationToken);
				break;
		}
	}

	/// <inheritdoc />
	public async ValueTask HandleAsync(LobbyEvent @event, CancellationToken cancellationToken)
	{
		switch (@event)
		{
			case LobbyRoomClosingAnnounced e:
				await AnnounceClosingAsync(e, cancellationToken);
				break;
			case LobbyRoomClosed e:
				_closingRooms.Remove(e.Room);
				break;
		}
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