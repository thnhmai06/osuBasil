using Basil.Application.Bot.Announcements;
using Basil.Application.Bot.Replies;
using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Multiplayer;
using Basil.Domain.Shared;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

/// <summary>Reacts to room and lobby events by posting announcements in the affected room.</summary>
internal sealed class RoomAnnouncer(
	Server server,
	RoomChannels channels,
	UserRegistry users,
	TimeProvider timeProvider,
	IReplies replies)
	: INotificationHandler<LobbyRoomClosingAnnounced>,
		INotificationHandler<LobbyRoomClosed>,
		INotificationHandler<RoomRoundStarted>,
		INotificationHandler<RoomRoundAborted>,
		INotificationHandler<RoomRoundDecided>,
		INotificationHandler<RoomRoundEnded>,
		INotificationHandler<RoomPlayerJoined>
{
	// Rounds announced as ended; only those are announced as decided, so a round nobody completed stays silent.
	private readonly ConcurrentSet<Round> _awaitingScores = [];
	private readonly ConcurrentSet<Room> _closingRooms = [];

	/// <inheritdoc />
	public ValueTask Handle(LobbyRoomClosed @event, CancellationToken ct)
	{
		_closingRooms.Remove(@event.Room);
		return ValueTask.CompletedTask;
	}

	/// <inheritdoc />
	public async ValueTask Handle(LobbyRoomClosingAnnounced @event, CancellationToken ct)
	{
		await AnnounceClosingAsync(@event, ct);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomPlayerJoined @event, CancellationToken ct)
	{
		if (_closingRooms.Remove(@event.Room))
			await SayInRoomAsync(@event.Room, replies.Text(AnnouncementReplies.RoomPlayerJoinedClosing),
				ct);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundAborted @event, CancellationToken ct)
	{
		await SayInRoomAsync(@event.Room, replies.Text(AnnouncementReplies.RoundAborted), ct);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundDecided @event, CancellationToken ct)
	{
		if (!_awaitingScores.Remove(@event.Round)) return;

		if (@event.Missing.Count == 0)
		{
			await SayInRoomAsync(@event.Room, replies.Text(AnnouncementReplies.ScoresReceived), ct);
			return;
		}

		await SayInRoomAsync(@event.Room, replies.Text(AnnouncementReplies.ScoresReceivedExcept), ct);
		foreach (var player in @event.Missing)
			await SayInRoomAsync(@event.Room,
				replies.Text(AnnouncementReplies.ScoreMissing, new { player = player.Value.Name }),
				ct);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundEnded @event, CancellationToken ct)
	{
		var seconds = (int)Math.Round(@event.ScoreWait.TotalSeconds);
		_awaitingScores.Add(@event.Round);
		await SayInRoomAsync(@event.Room,
			replies.Text(AnnouncementReplies.RoundEnded, new { id = @event.Round.Index, seconds }),
			ct);
	}

	/// <inheritdoc />
	public async ValueTask Handle(RoomRoundStarted @event, CancellationToken ct)
	{
		if (@event.Players.Count == 0)
			await SayInRoomAsync(@event.Room, replies.Text(AnnouncementReplies.RoundStartedNoPlayers),
				ct);
	}

	private async Task AnnounceClosingAsync(LobbyRoomClosingAnnounced e, CancellationToken ct)
	{
		var minutesUntilClose = (int)Math.Ceiling((e.ClosesAt - timeProvider.GetUtcNow()).TotalMinutes);
		var text = replies.Text(AnnouncementReplies.RoomClosingAnnounced, new { minutes = minutesUntilClose });

		_closingRooms.Add(e.Room);
		await SayInRoomAsync(e.Room, text, ct);
		await WhisperStaffAsync(e.Room, text, ct);
	}

	private Task SayInRoomAsync(Room room, string text, CancellationToken ct)
	{
		return room.IsClosed
			? Task.CompletedTask
			: server.SayAsync(channels.Of(room), text, ct: ct);
	}

	private async Task WhisperStaffAsync(Room room, string text, CancellationToken ct)
	{
		await WhisperIfOnlineAsync(room.Authority.Creator);

		foreach (var referee in room.Authority.Referees)
			await WhisperIfOnlineAsync(referee);
		return;

		async Task WhisperIfOnlineAsync(UserRecord user)
		{
			if (users.Online.GetValueOrDefault(user) is { } online)
				await server.WhisperAsync(online, text, ct: ct);
		}
	}
}