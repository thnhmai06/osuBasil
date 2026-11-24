using Basil.Application.Chat.Registry;
using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Registry;
using Basil.Domain.Multiplayer.Report.Rounds;
using Basil.Domain.Users;
using Basil.Shared.Structures.Data;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Infrastructure.Bot.Services;

/// <summary>Speaks BasilBot's announcements in a room and remembers which of them are still awaited.</summary>
internal sealed class RoomAnnouncements(Server server, RoomChannels channels, SessionRegistry users)
{
	// Rounds announced as ended; only those are announced as decided, so a round nobody completed stays silent.
	private readonly ConcurrentSet<Round> _awaitingScores = [];
	private readonly ConcurrentSet<Room> _closingRooms = [];

	public void AwaitScores(Round round)
	{
		_awaitingScores.Add(round);
	}

	public bool TakeAwaitingScores(Round round)
	{
		return _awaitingScores.Remove(round);
	}

	public void MarkClosing(Room room)
	{
		_closingRooms.Add(room);
	}

	public bool TakeClosing(Room room)
	{
		return _closingRooms.Remove(room);
	}

	public Task SayInRoomAsync(Room room, string text, CancellationToken ct)
	{
		return room.IsClosed
			? Task.CompletedTask
			: server.SayAsync(channels.Of(room), text, ct: ct);
	}

	public async Task WhisperStaffAsync(Room room, string text, CancellationToken ct)
	{
		await WhisperIfOnlineAsync(room.Creator);

		foreach (var referee in room.Referees)
			await WhisperIfOnlineAsync(referee);
		return;

		async Task WhisperIfOnlineAsync(UserRecord user)
		{
			if (users.Online.GetValueOrDefault(user) is { } online)
				await server.WhisperAsync(online, text, ct: ct);
		}
	}
}
