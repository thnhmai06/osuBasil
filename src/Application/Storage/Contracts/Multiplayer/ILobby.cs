using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Storage.Contracts.Multiplayer;

/// <summary>The open multiplayer rooms and the osu! clients watching the multiplayer lobby.</summary>
public interface ILobby
{
	/// <summary>Gets every open room.</summary>
	IEnumerable<Room> Rooms { get; }

	/// <summary>Gets the osu! clients watching the multiplayer lobby.</summary>
	IReadOnlySet<BanchoConnection> Watchers { get; }

	/// <summary>Gets a value that indicates whether every room id is taken or reserved, so no room can open.</summary>
	internal bool IsFull { get; }

	/// <summary>Finds an open room by id.</summary>
	Room? Find(int id);

	/// <summary>Finds the room a player is seated in.</summary>
	Room? RoomOf(BanchoConnection player);

	/// <summary>Reserves the lowest free room id so no other room takes it.</summary>
	/// <returns>The reserved id, or <see langword="null" /> when every room id is taken or reserved.</returns>
	internal int? Reserve();

	/// <summary>Frees a reserved room id that no room took.</summary>
	/// <param name="id">The reserved id.</param>
	internal void Release(int id);

	/// <summary>Lists a room as open under the id reserved for it.</summary>
	/// <param name="room">The room, whose <see cref="Room.Id" /> was reserved with <see cref="Reserve" />.</param>
	internal void Add(Room room);

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