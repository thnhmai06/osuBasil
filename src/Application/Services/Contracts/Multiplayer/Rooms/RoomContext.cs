using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Multiplayer.Rooms;

/// <summary>The ports through which a room interacts with its runtime environment.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The event sink for emitting room events.</param>
/// <param name="Lobby">The lobby that holds the room.</param>
/// <param name="Channel">The chat service for the room's channel.</param>
internal sealed record RoomContext(
	TimeProvider Clock,
	IRoomEventSink Events,
	IRoomLobbyPort Lobby,
	IRoomChannelPort Channel);

/// <summary>Emits room events to the event stream.</summary>
internal interface IRoomEventSink
{
	/// <summary>Emits a room event.</summary>
	/// <param name="event">The event to emit.</param>
	void Emit(RoomEvent @event);
}

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
}

/// <summary>What a room asks of the chat to keep its own channel in step.</summary>
internal interface IRoomChannelPort
{
	/// <summary>Adds a connection to the room's channel.</summary>
	/// <param name="room">The room whose channel to join.</param>
	/// <param name="connection">The connection that joins.</param>
	void Join(Room room, Connection connection);

	/// <summary>Removes a connection from the room's channel.</summary>
	/// <param name="room">The room whose channel to leave.</param>
	/// <param name="connection">The connection that leaves.</param>
	void Part(Room room, Connection connection);
}