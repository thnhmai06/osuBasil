using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Storage.Contracts.Multiplayer;

/// <summary>The open multiplayer rooms and the osu! clients watching the multiplayer lobby.</summary>
public interface ILobby
{
	/// <summary>Gets every open room.</summary>
	IEnumerable<Room> Rooms { get; }

	/// <summary>Gets the osu! clients watching the multiplayer lobby.</summary>
	IReadOnlySet<BanchoConnection> Watchers { get; }

	/// <summary>Gets a value that indicates whether every room id is taken, so no room can open.</summary>
	internal bool IsFull { get; }

	/// <summary>Finds an open room by id.</summary>
	Room? Find(int id);

	/// <summary>Finds the room a player is seated in.</summary>
	Room? RoomOf(BanchoConnection player);

	/// <summary>Creates a room under the lowest free room id and lists it as open.</summary>
	/// <param name="create">Creates the room for the assigned id.</param>
	/// <returns>The new room, or <see langword="null" /> when every room id is taken.</returns>
	/// <remarks>The caller holds the scope from <see cref="Enter" />.</remarks>
	internal Room? Add(Func<int, Room> create);

	/// <summary>Removes a room from the open rooms.</summary>
	/// <param name="room">The room to remove.</param>
	/// <returns><see langword="true" /> if that room was open; <see langword="false" /> if it was not listed under its id.</returns>
	internal bool Remove(Room room);

	/// <summary>Adds an osu! client to the lobby watchers.</summary>
	/// <param name="watcher">The client that started watching.</param>
	/// <returns><see langword="true" /> if the client was added; <see langword="false" /> if it already was watching.</returns>
	internal bool AddWatcher(BanchoConnection watcher);

	/// <summary>Removes an osu! client from the lobby watchers.</summary>
	/// <param name="watcher">The client that stopped watching.</param>
	/// <returns><see langword="true" /> if the client was removed; <see langword="false" /> if it was not watching.</returns>
	internal bool RemoveWatcher(BanchoConnection watcher);

	/// <summary>Enters the short scope in which a room id is assigned and a creator's open rooms are counted.</summary>
	/// <returns>The scope, to dispose when done.</returns>
	internal Lock.Scope Enter();
}
