using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Multiplayer;

/// <summary>The open multiplayer rooms and the osu! clients watching the multiplayer lobby.</summary>
public interface ILobby
{
	/// <summary>Gets every open room by its id.</summary>
	IReadOnlyDictionary<int, Room> Rooms { get; }

	/// <summary>Gets the osu! clients watching the multiplayer lobby.</summary>
	IReadOnlySet<BanchoConnection> Watchers { get; }

	/// <summary>Gets a value that indicates whether every room id is taken or reserved, so no room can open.</summary>
	internal bool IsFull { get; }

	/// <summary>Gets the osu! clients watching the multiplayer lobby, for changing them.</summary>
	internal ISet<BanchoConnection> MutableWatchers { get; }

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

	/// <summary>Enters the short scope in which a room id is assigned and a creator's open rooms are counted.</summary>
	/// <returns>The scope, to dispose when done.</returns>
	internal Lock.Scope Enter();
}
