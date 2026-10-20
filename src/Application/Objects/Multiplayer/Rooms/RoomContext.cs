using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Multiplayer.Rooms;

/// <summary>The ports through which a room interacts with its runtime environment.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The event sink for emitting room events.</param>
/// <param name="Lobby">The lobby that holds the room.</param>
/// <param name="Chat">The runtime dependencies for the room's channel.</param>
/// <param name="Users">The user service for checking blocks.</param>
internal sealed record RoomContext(
	TimeProvider Clock,
	IRoomEventSink Events,
	IRoomLobbyPort Lobby,
	ChannelContext Chat,
	IRoomUsersPort Users);

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

	/// <summary>Closes the room: the lobby evicts its players, ends its round, closes its channel and forgets it.</summary>
	/// <param name="room">The room to close.</param>
	/// <param name="by">The user who closed the room, or <see langword="null" /> when the room closed itself.</param>
	/// <remarks>The caller holds the room's scope.</remarks>
	void Close(Room room, UserRecord? by);
}

/// <summary>What a room asks of the users: whether one blocks another.</summary>
internal interface IRoomUsersPort
{
	/// <summary>Gets whether one user blocks another.</summary>
	/// <param name="blocker">The user whose blocks are checked.</param>
	/// <param name="who">The user who may be blocked.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	ValueTask<bool> BlocksAsync(UserRecord blocker, UserRecord who, CancellationToken cancellationToken);
}
