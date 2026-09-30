using Basil.Domain.Users;
using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer.Events;

/// <summary>A player's access to a room — ban, unban, or invitation — changed.</summary>
public abstract record RoomAccessEvent(Room Room) : RoomEvent(Room);

/// <summary>A player was banned from the room, evicting them if they were seated.</summary>
public sealed record PlayerBanned(Room Room, User Player, RoomSlot? Vacated, BanchoConnection? Evicted)
	: RoomAccessEvent(Room);

/// <summary>A player's ban from the room was lifted.</summary>
public sealed record PlayerUnbanned(Room Room, User Player) : RoomAccessEvent(Room);

/// <summary>A player was invited to the room.</summary>
public sealed record PlayerInvited(Room Room, User Player) : RoomAccessEvent(Room);