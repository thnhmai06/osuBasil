using Basil.Application.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Events;

/// <summary>A room's host or referee authority changed.</summary>
public abstract record RoomAuthorityEvent(Room Room) : RoomEvent(Room);

/// <summary>The room's host changed.</summary>
public sealed record HostChanged(Room Room, BanchoConnection? Host) : RoomAuthorityEvent(Room);

/// <summary>A player was granted referee authority for the room.</summary>
public sealed record RefereeAdded(Room Room, User Referee) : RoomAuthorityEvent(Room);

/// <summary>A player's referee authority for the room was revoked.</summary>
public sealed record RefereeRemoved(Room Room, User Referee) : RoomAuthorityEvent(Room);