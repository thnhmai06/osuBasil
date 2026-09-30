using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer.Events;

/// <summary>A player joined, left, or was removed from a room.</summary>
public abstract record RoomMembershipEvent(Room Room) : RoomEvent(Room);

/// <summary>A player joined the room and was assigned a slot.</summary>
public sealed record PlayerJoined(Room Room, BanchoConnection Player, RoomSlot Slot) : RoomMembershipEvent(Room);

/// <summary>A player left the room, clearing their slot.</summary>
public sealed record PlayerLeft(Room Room, BanchoConnection Player, RoomSlot Slot) : RoomMembershipEvent(Room);

/// <summary>A player was removed from the room.</summary>
public sealed record PlayerKicked(Room Room, BanchoConnection Player, RoomSlot Slot) : RoomMembershipEvent(Room);

/// <summary>A player moved from one slot to another.</summary>
public sealed record PlayerMoved(Room Room, BanchoConnection Player, RoomSlot From, RoomSlot To)
	: RoomMembershipEvent(Room);