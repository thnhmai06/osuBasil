using System.Collections.Concurrent;
using Basil.Bot.Application.Basil;
using Basil.Bot.Application.Commands.Mp;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Announcements;

/// <summary>Reacts to room events and posts announcements in the room's channel.</summary>
internal sealed class RoomAnnouncer(
	IBasilChat chat,
	IBasilRooms rooms,
	TimeProvider timeProvider,
	RoomCountdowns countdowns)
{
	private readonly ConcurrentDictionary<int, DateTimeOffset> _closingRooms = new();

	/// <summary>Handles a room event and posts appropriate announcements.</summary>
	public async Task HandleAsync(BotEvent botEvent, CancellationToken cancellationToken)
	{
		switch (botEvent)
		{
			case RoomRoundStarted e:
				await HandleRoundStartedAsync(e, cancellationToken);
				break;
			case RoomRoundAborted e:
				await HandleRoundAbortedAsync(e, cancellationToken);
				break;
			case RoomSettingsChanged e:
				await HandleSettingsChangedAsync(e, cancellationToken);
				break;
			case RoomClosingAnnounced e:
				await HandleClosingAnnouncedAsync(e, cancellationToken);
				break;
			case RoomPlayerJoined e:
				await HandlePlayerJoinedAsync(e, cancellationToken);
				break;
			case RoomClosed e:
				HandleRoomClosed(e);
				break;
		}
	}

	private async Task HandleRoundStartedAsync(RoomRoundStarted e, CancellationToken cancellationToken)
	{
		countdowns.Stop(e.RoomId);

		if (e.PlayerCount == 0)
			await chat.PostAsync($"#mp_{e.RoomId}", AnnouncementReplies.RoundStartedNoPlayers, cancellationToken);
	}

	private async Task HandleRoundAbortedAsync(RoomRoundAborted e, CancellationToken cancellationToken)
	{
		countdowns.Stop(e.RoomId);
		await chat.PostAsync($"#mp_{e.RoomId}", AnnouncementReplies.RoundAborted, cancellationToken);
	}

	private async Task HandleSettingsChangedAsync(RoomSettingsChanged e, CancellationToken cancellationToken)
	{
		if (e.RoundSettingsChanged && countdowns.Stop(e.RoomId, true))
			await chat.PostAsync($"#mp_{e.RoomId}", AnnouncementReplies.SettingsChangedCountdownCancelled,
				cancellationToken);
	}

	private async Task HandleClosingAnnouncedAsync(RoomClosingAnnounced e, CancellationToken cancellationToken)
	{
		var now = timeProvider.GetUtcNow();
		var minutesUntilClose = (int)Math.Ceiling((e.ClosesAt - now).TotalMinutes);
		var text = string.Format(AnnouncementReplies.RoomClosingAnnounced, minutesUntilClose);

		await chat.PostAsync($"#mp_{e.RoomId}", text, cancellationToken);

		_closingRooms[e.RoomId] = e.ClosesAt;

		// Also send PM to creator and referees
		var room = await rooms.GetAsync(e.RoomId, cancellationToken);
		if (room is null)
			return;

		var recipients = new List<UserRecord>();
		if (room.Creator is not null)
			recipients.Add(room.Creator);
		recipients.AddRange(room.Referees);

		foreach (var recipient in recipients)
			await chat.SendAsync(recipient, text, cancellationToken);
	}

	private async Task HandlePlayerJoinedAsync(RoomPlayerJoined e, CancellationToken cancellationToken)
	{
		if (_closingRooms.TryRemove(e.RoomId, out _))
			await chat.PostAsync($"#mp_{e.RoomId}", AnnouncementReplies.RoomPlayerJoinedClosing, cancellationToken);
	}

	private void HandleRoomClosed(RoomClosed e)
	{
		countdowns.Stop(e.RoomId);
		_closingRooms.TryRemove(e.RoomId, out _);
	}
}