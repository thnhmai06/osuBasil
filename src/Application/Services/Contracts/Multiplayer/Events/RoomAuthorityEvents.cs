using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Multiplayer.Events;

/// <summary>A room's host or referee authority changed.</summary>
public abstract record RoomAuthorityEvent(Room Room) : RoomEvent(Room);

/// <summary>The room's host changed.</summary>
/// <param name="Room">The room whose host changed.</param>
/// <param name="By">The user who set the host.</param>
/// <param name="Host">The new host, or <see langword="null" /> when the room has none.</param>
public sealed record RoomHostChanged(Room Room, User By, BanchoConnection? Host) : RoomAuthorityEvent(Room);

/// <summary>A player was granted referee authority for the room.</summary>
/// <param name="Room">The room.</param>
/// <param name="By">The user who granted it.</param>
/// <param name="Referee">The new referee.</param>
public sealed record RoomRefereeAdded(Room Room, User By, User Referee) : RoomAuthorityEvent(Room);

/// <summary>A player's referee authority for the room was revoked.</summary>
/// <param name="Room">The room.</param>
/// <param name="By">The user who revoked it.</param>
/// <param name="Referee">The former referee.</param>
public sealed record RoomRefereeRemoved(Room Room, User By, User Referee) : RoomAuthorityEvent(Room);
