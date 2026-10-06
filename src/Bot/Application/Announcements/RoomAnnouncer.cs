using System.Collections.Concurrent;
using Basil.Bot.Application.Basil;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Announcements;

/// <summary>Reacts to room events and posts announcements in the room's channel.</summary>
internal sealed class RoomAnnouncer(IBasilChat chat, IBasilRooms rooms, TimeProvider timeProvider)
{
	private readonly ConcurrentDictionary<int, bool> _countdownStartsRound = new();
	private readonly ConcurrentDictionary<int, DateTimeOffset> _closingRooms = new();

	/// <summary>Handles a room event and posts appropriate announcements.</summary>
	public async Task HandleAsync(BotEvent botEvent, CancellationToken cancellationToken)
	{
		switch (botEvent)
		{
			case RoomCountdownStarted e:
				await HandleCountdownStartedAsync(e, cancellationToken);
				break;
			case RoomCountdownTicked e:
				await HandleCountdownTickedAsync(e, cancellationToken);
				break;
			case RoomCountdownCancelled e:
				await HandleCountdownCancelledAsync(e, cancellationToken);
				break;
			case RoomCountdownElapsed e:
				await HandleCountdownElapsedAsync(e, cancellationToken);
				break;
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

	private async Task HandleCountdownStartedAsync(RoomCountdownStarted e, CancellationToken cancellationToken)
	{
		_countdownStartsRound[e.RoomId] = e.StartsRound;
		var seconds = (int)e.Length.TotalSeconds;
		var text = e.StartsRound
			? string.Format(AnnouncementReplies.CountdownStartedForRound, seconds)
			: string.Format(AnnouncementReplies.CountdownStarted, seconds);
		await chat.PostAsync($"#mp_{e.RoomId}", text, cancellationToken);
	}

	private async Task HandleCountdownTickedAsync(RoomCountdownTicked e, CancellationToken cancellationToken)
	{
		if (!_countdownStartsRound.TryGetValue(e.RoomId, out var startsRound))
			return;

		var seconds = (int)e.Remaining.TotalSeconds;
		var text = startsRound
			? string.Format(AnnouncementReplies.CountdownTickedForRound, seconds)
			: string.Format(AnnouncementReplies.CountdownTicked, seconds);
		await chat.PostAsync($"#mp_{e.RoomId}", text, cancellationToken);
	}

	private async Task HandleCountdownCancelledAsync(RoomCountdownCancelled e, CancellationToken cancellationToken)
	{
		_countdownStartsRound.TryRemove(e.RoomId, out _);
		await chat.PostAsync($"#mp_{e.RoomId}", AnnouncementReplies.CountdownCancelled, cancellationToken);
	}

	private async Task HandleCountdownElapsedAsync(RoomCountdownElapsed e, CancellationToken cancellationToken)
	{
		_countdownStartsRound.TryRemove(e.RoomId, out _);
		var text = e.StartsRound
			? AnnouncementReplies.CountdownElapsedForRound
			: AnnouncementReplies.CountdownElapsed;
		await chat.PostAsync($"#mp_{e.RoomId}", text, cancellationToken);
	}

	private async Task HandleRoundStartedAsync(RoomRoundStarted e, CancellationToken cancellationToken)
	{
		_countdownStartsRound.TryRemove(e.RoomId, out _);

		string? text = null;
		if (e.ByCountdown)
			text = AnnouncementReplies.RoundStartedByCountdown;
		else if (e.PlayerCount == 0)
			text = AnnouncementReplies.RoundStartedNoPlayers;

		if (text is not null)
			await chat.PostAsync($"#mp_{e.RoomId}", text, cancellationToken);
	}

	private async Task HandleRoundAbortedAsync(RoomRoundAborted e, CancellationToken cancellationToken)
	{
		await chat.PostAsync($"#mp_{e.RoomId}", AnnouncementReplies.RoundAborted, cancellationToken);
	}

	private async Task HandleSettingsChangedAsync(RoomSettingsChanged e, CancellationToken cancellationToken)
	{
		if (e.CountdownCancelled)
		{
			_countdownStartsRound.TryRemove(e.RoomId, out _);
			await chat.PostAsync($"#mp_{e.RoomId}", AnnouncementReplies.SettingsChangedCountdownCancelled, cancellationToken);
		}
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

		var recipients = new List<User>();
		if (room.Creator is not null)
			recipients.Add(room.Creator);
		recipients.AddRange(room.Referees);

		foreach (var recipient in recipients)
			await chat.SendAsync(recipient, text, cancellationToken);
	}

	private async Task HandlePlayerJoinedAsync(RoomPlayerJoined e, CancellationToken cancellationToken)
	{
		if (_closingRooms.TryRemove(e.RoomId, out _))
		{
			await chat.PostAsync($"#mp_{e.RoomId}", AnnouncementReplies.RoomPlayerJoinedClosing, cancellationToken);
		}
	}

	private void HandleRoomClosed(RoomClosed e)
	{
		_countdownStartsRound.TryRemove(e.RoomId, out _);
		_closingRooms.TryRemove(e.RoomId, out _);
	}
}
