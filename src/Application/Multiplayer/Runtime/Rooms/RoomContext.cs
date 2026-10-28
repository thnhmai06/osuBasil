using Basil.Application.Content.Persistence;
using Basil.Application.Users.Connections;
using Basil.Application.Common.Events;
using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Runtime.Rooms;

/// <summary>The ports through which a room interacts with its runtime environment.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The publisher for room events.</param>
/// <param name="Lobby">The lobby that holds the room.</param>
/// <param name="Users">The questions a room asks about blocks between users.</param>
internal sealed record RoomContext(
	TimeProvider Clock,
	IEventPublisher<RoomEvent> Events,
	IRoomLobbyPort Lobby,
	RoomBlocks Users,
	ISettingsRepository Settings);

/// <summary>What a room asks of the lobby that holds it.</summary>
internal interface IRoomLobbyPort
{
	/// <summary>Gets the room a connection is seated in, or <see langword="null" />.</summary>
	/// <param name="player">The player's game connection.</param>
	/// <returns>The room containing the player, or <see langword="null" />.</returns>
	Room? RoomOf(BanchoConnection player);

	/// <summary>Tells the lobby the room has a player again, which cancels a pending close.</summary>
	/// <param name="room">The room that is occupied.</param>
	void Occupied(Room room);

	/// <summary>Tells the lobby the room has no player left, so it closes or schedules its closing.</summary>
	/// <param name="room">The room that may be empty.</param>
	void Emptied(Room room);

	/// <summary>Closes the room: the lobby evicts its players, ends its round and forgets it.</summary>
	/// <param name="room">The room to close.</param>
	/// <param name="by">The user who closed the room, or <see langword="null" /> when the room closed itself.</param>
	/// <remarks>The caller holds the room's scope.</remarks>
	void Close(Room room, UserRecord? by);

	/// <summary>Marks that a seating of a player in a room is under way, so no other room seats the same player meanwhile.</summary>
	/// <param name="player">The player's game connection.</param>
	/// <param name="room">The room that is about to seat the player.</param>
	/// <returns><see langword="false" /> when another room is seating the same player.</returns>
	/// <remarks>
	///     The caller does not hold a room's scope, and releases the claim with <see cref="Release" /> when the seating
	///     is over.
	/// </remarks>
	bool TryClaim(BanchoConnection player, Room room);

	/// <summary>Ends the claim a room made on a player, whatever the seating came to.</summary>
	/// <param name="player">The player's game connection.</param>
	/// <param name="room">The room that made the claim.</param>
	void Release(BanchoConnection player, Room room);
}