using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer;

/// <summary>Creates and closes multiplayer rooms.</summary>
public sealed class Lobby(
	IRoomRegistry rooms,
	IMatchRepository matches)
{
	/// <summary>The largest room id; the client protocol carries room ids as unsigned 16-bit values.</summary>
	private const int MaxRoomId = ushort.MaxValue;

	/// <summary>Gets every currently open room, for a lobby listing.</summary>
	public IEnumerable<Room> Observers => rooms.AllById.Values;

	/// <summary>Creates a match, its room, and its chat channel, and registers all three.</summary>
	/// <param name="creator">The player creating the room, or <see langword="null" /> for an unattended room.</param>
	/// <param name="name">The room's name.</param>
	/// <param name="password">The room's password, or an empty string for none.</param>
	/// <param name="settings">The room's initial settings, or <see langword="null" /> for the defaults.</param>
	/// <param name="cancellationToken">A token that cancels the creation.</param>
	/// <returns>The newly created room.</returns>
	/// <exception cref="InvalidOperationException">Every room id is in use.</exception>
	public async Task<Room> CreateRoomAsync(User? creator, string name, string password,
		MatchSettings? settings = null, CancellationToken cancellationToken = default)
	{
		var match = await matches.AddAsync(new MatchData
		{
			Name = name,
			StartedAt = DateTimeOffset.UtcNow,
			EndedAt = null,
			Creator = creator
		}, cancellationToken);

		Room room;
		do
		{
			room = new Room(FreeRoomId(), match, settings ?? new MatchSettings());
			if (!string.IsNullOrEmpty(password))
				room.Password = password;
		} while (!rooms.TryAdd(room));

		return room;
	}

	/// <summary>Closes a room: removes every player, ends its match, and unregisters the room and its channel.</summary>
	/// <param name="roomId">The id of the room to close.</param>
	/// <param name="cancellationToken">A token that cancels the closure.</param>
	public async Task CloseRoomAsync(int roomId, CancellationToken cancellationToken = default)
	{
		if (await rooms.EnterAsync(roomId, cancellationToken) is not { } scope) return;

		Room room;
		await using (scope)
		{
			room = scope.Room;
			room.Close();
			rooms.Remove(roomId);
		}
	}

	/// <summary>Finds the room a player is seated in.</summary>
	/// <param name="player">The player's game client connection.</param>
	/// <returns>The room, or <see langword="null" /> when the player is not in a room.</returns>
	public Room? RoomOf(BanchoConnection player)
	{
		// ponytail: scans every room; replaced by a player index when the lobby owns membership.
		return rooms.AllById.Values.FirstOrDefault(room => room.Slots.Find(player) is not null);
	}

	private int FreeRoomId()
	{
		var taken = rooms.AllById;
		for (var id = 1; id <= MaxRoomId; id++)
			if (!taken.ContainsKey(id))
				return id;
		throw new InvalidOperationException("Every room id is in use.");
	}
}