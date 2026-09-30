using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer.Events;

/// <summary>A player joined, left, or was removed from a room.</summary>
public abstract record RoomMembershipEvent(Room Room) : RoomEvent(Room);

/// <summary>A player joined the room and was assigned a slot.</summary>
public sealed record PlayerJoined(Room Room, BanchoConnection Player, RoomSlot Slot) : RoomMembershipEvent(Room);

/// <summary>A player left the room, clearing their slot.</summary>
/// <param name="Room">The room the player left.</param>
/// <param name="Player">The player who left.</param>
/// <param name="Slot">The slot that was vacated.</param>
/// <param name="Host">The room's host after the player left.</param>
public sealed record PlayerLeft(Room Room, BanchoConnection Player, RoomSlot Slot, BanchoConnection? Host)
	: RoomMembershipEvent(Room);

/// <summary>A player was removed from the room.</summary>
/// <param name="Room">The room the player was removed from.</param>
/// <param name="Player">The player who was removed.</param>
/// <param name="Slot">The slot that was vacated.</param>
/// <param name="Host">The room's host after the player left.</param>
public sealed record PlayerKicked(Room Room, BanchoConnection Player, RoomSlot Slot, BanchoConnection? Host)
	: RoomMembershipEvent(Room);

/// <summary>A player moved from one slot to another.</summary>
public sealed record PlayerMoved(Room Room, BanchoConnection Player, RoomSlot From, RoomSlot To)
	: RoomMembershipEvent(Room);

/// <summary>An osu!tourney client started observing the room.</summary>
public sealed record ObserverJoined(Room Room, TourneyConnection Observer) : RoomMembershipEvent(Room);

/// <summary>An osu!tourney client stopped observing the room.</summary>
public sealed record ObserverLeft(Room Room, TourneyConnection Observer) : RoomMembershipEvent(Room);