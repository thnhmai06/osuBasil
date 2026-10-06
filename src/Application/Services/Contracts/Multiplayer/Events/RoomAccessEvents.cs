using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Multiplayer.Events;

/// <summary>A player's access to a room — ban, unban, or invitation — changed.</summary>
public abstract record RoomAccessEvent(Room Room) : RoomEvent(Room);

/// <summary>A player was banned from the room, evicting them if they were seated.</summary>
/// <param name="Room">The room the player was banned from.</param>
/// <param name="By">The user who banned the player.</param>
/// <param name="Player">The player who was banned.</param>
/// <param name="Vacated">
///     The number of the slot that was vacated, or <see langword="null" /> when the player was not
///     seated.
/// </param>
/// <param name="Evicted">The connection that was removed, or <see langword="null" /> when the player was not seated.</param>
/// <param name="Host">The room's host after the player left.</param>
/// <param name="RoundProgress">
///     What the departure did to the round in progress, or <see langword="null" /> when no round
///     was in progress or nothing changed.
/// </param>
public sealed record RoomPlayerBanned(
	Room Room,
	User By,
	User Player,
	int? Vacated,
	BanchoConnection? Evicted,
	BanchoConnection? Host,
	RoomRoundProgress? RoundProgress)
	: RoomAccessEvent(Room);

/// <summary>A player's ban from the room was lifted.</summary>
public sealed record RoomPlayerUnbanned(Room Room, User Player) : RoomAccessEvent(Room);

/// <summary>A player was invited to the room.</summary>
/// <param name="Room">The room the player was invited to.</param>
/// <param name="By">The user who sent the invitation.</param>
/// <param name="Player">The invited user.</param>
public sealed record RoomPlayerInvited(Room Room, User By, User Player) : RoomAccessEvent(Room);