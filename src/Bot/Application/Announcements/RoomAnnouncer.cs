using System.Collections.Concurrent;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Application.Objects.Servers;
using Basil.Application.Objects.Users;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Announcements;

/// <summary>Reacts to room and lobby events by posting announcements in the affected room.</summary>
internal sealed class RoomAnnouncer(Server server, UserRegistry users, TimeProvider timeProvider)
	: IEventHandler<RoomEvent>, IEventHandler<LobbyEvent>
{
	private readonly ConcurrentDictionary<int, Room> _closingRooms = new();

	/// <inheritdoc />
	public async ValueTask HandleAsync(RoomEvent @event, CancellationToken cancellationToken)
	{
		switch (@event)
		{
			case RoomRoundStarted { Players.Count: 0 } e:
				await server.SayAsync(e.Room.Channel, AnnouncementReplies.RoundStartedNoPlayers, ct: cancellationToken);
				break;
			case RoomRoundAborted e:
				await server.SayAsync(e.Room.Channel, AnnouncementReplies.RoundAborted, ct: cancellationToken);
				break;
			case RoomCountdownStarted e:
				var seconds = (int)e.Countdown.Length.TotalSeconds;
				var text = e.Countdown.StartsRound
					? string.Format(AnnouncementReplies.CountdownStartedForRound, seconds)
					: string.Format(AnnouncementReplies.CountdownStarted, seconds);
				await server.SayAsync(e.Room.Channel, text, ct: cancellationToken);
				break;
			case RoomCountdownTicked e:
				var remainingSeconds = (int)Math.Round(e.Remaining.TotalSeconds);
				var tick = e.Countdown.StartsRound
					? string.Format(AnnouncementReplies.CountdownTickedForRound, remainingSeconds)
					: string.Format(AnnouncementReplies.CountdownTicked, remainingSeconds);
				await server.SayAsync(e.Room.Channel, tick, ct: cancellationToken);
				break;
			case RoomCountdownElapsed e:
				var elapsed = e.StartResult switch
				{
					RoomResult.Ok when e.Countdown.StartsRound =>
						AnnouncementReplies.RoundStartedByCountdown,
					RoomResult.Ok when !e.Countdown.StartsRound => AnnouncementReplies.CountdownElapsed,
					RoomResult.NoBeatmap => AnnouncementReplies.CountdownElapsedForRound,
					_ => null
				};
				if (elapsed is not null)
					await server.SayAsync(e.Room.Channel, elapsed, ct: cancellationToken);
				break;
			case RoomCountdownCancelled e:
				await server.SayAsync(e.Room.Channel, AnnouncementReplies.CountdownCancelled, ct: cancellationToken);
				break;
			case RoomPlayerJoined e when _closingRooms.TryRemove(new KeyValuePair<int, Room>(e.Room.Id, e.Room)):
				await server.SayAsync(e.Room.Channel, AnnouncementReplies.RoomPlayerJoinedClosing,
					ct: cancellationToken);
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
				_closingRooms.TryRemove(new KeyValuePair<int, Room>(e.Room.Id, e.Room));
				break;
		}
	}

	private async Task AnnounceClosingAsync(LobbyRoomClosingAnnounced e, CancellationToken cancellationToken)
	{
		var minutesUntilClose = (int)Math.Ceiling((e.ClosesAt - timeProvider.GetUtcNow()).TotalMinutes);
		var text = string.Format(AnnouncementReplies.RoomClosingAnnounced, minutesUntilClose);

		await server.SayAsync(e.Room.Channel, text, ct: cancellationToken);
		_closingRooms[e.Room.Id] = e.Room;
		await WhisperStaffAsync(e.Room, text, cancellationToken);
	}

	private async Task WhisperStaffAsync(Room room, string text, CancellationToken cancellationToken)
	{
		if (room.Authority.Creator is { } creator)
			await WhisperIfOnlineAsync(creator);

		foreach (var referee in room.Authority.Referees)
			await WhisperIfOnlineAsync(referee);
		return;

		async Task WhisperIfOnlineAsync(UserRecord user)
		{
			if (users.Online.GetValueOrDefault(user) is { } online)
				await server.WhisperAsync(online, text, ct: cancellationToken);
		}
	}
}