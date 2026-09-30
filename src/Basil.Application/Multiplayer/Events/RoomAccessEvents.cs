using Basil.Domain.Users;
using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer.Events;

/// <summary>A player's access to a room — ban, unban, or invitation — changed.</summary>
public abstract record RoomAccessEvent(Room Room) : RoomEvent(Room);

/// <summary>A player was banned from the room, evicting them if they were seated.</summary>
/// <param name="Room">The room the player was banned from.</param>
/// <param name="Player">The player who was banned.</param>
/// <param name="Vacated">The slot that was vacated, or <see langword="null" /> when the player was not seated.</param>
/// <param name="Evicted">The connection that was removed, or <see langword="null" /> when the player was not seated.</param>
/// <param name="Host">The room's host after the player left.</param>
public sealed record PlayerBanned(
	Room Room,
	User Player,
	RoomSlot? Vacated,
	BanchoConnection? Evicted,
	BanchoConnection? Host)
	: RoomAccessEvent(Room);

/// <summary>A player's ban from the room was lifted.</summary>
public sealed record PlayerUnbanned(Room Room, User Player) : RoomAccessEvent(Room);

/// <summary>A player was invited to the room.</summary>
/// <param name="Room">The room the player was invited to.</param>
/// <param name="By">The user who sent the invitation.</param>
/// <param name="Player">The invited user.</param>
public sealed record PlayerInvited(Room Room, User By, User Player) : RoomAccessEvent(Room);