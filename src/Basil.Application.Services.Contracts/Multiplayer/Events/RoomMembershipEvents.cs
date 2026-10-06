using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Multiplayer.Events;

/// <summary>A player joined, left, or was removed from a room.</summary>
public abstract record RoomMembershipEvent(Room Room) : RoomEvent(Room);

/// <summary>A player joined the room and was assigned a slot.</summary>
/// <param name="Room">The room the player joined.</param>
/// <param name="Player">The player who joined.</param>
/// <param name="Slot">The number of the slot they were assigned.</param>
/// <param name="Replaced">
///     A closed connection of the same user whose seat the player took over, or <see langword="null" />
///     .
/// </param>
/// <param name="RoundProgress">
///     What the departure of the replaced connection did to the round in progress, or
///     <see langword="null" /> when no round was in progress or nothing changed.
/// </param>
public sealed record RoomPlayerJoined(
	Room Room,
	BanchoConnection Player,
	int Slot,
	BanchoConnection? Replaced,
	RoomRoundProgress? RoundProgress)
	: RoomMembershipEvent(Room);

/// <summary>A player left the room, clearing their slot.</summary>
/// <param name="Room">The room the player left.</param>
/// <param name="Player">The player who left.</param>
/// <param name="Slot">The number of the slot that was vacated.</param>
/// <param name="Host">The room's host after the player left.</param>
/// <param name="RoundProgress">
///     What the departure did to the round in progress, or <see langword="null" /> when no round
///     was in progress or nothing changed.
/// </param>
public sealed record RoomPlayerLeft(
	Room Room,
	BanchoConnection Player,
	int Slot,
	BanchoConnection? Host,
	RoomRoundProgress? RoundProgress)
	: RoomMembershipEvent(Room);

/// <summary>A player was removed from the room.</summary>
/// <param name="Room">The room the player was removed from.</param>
/// <param name="Player">The player who was removed.</param>
/// <param name="Slot">The number of the slot that was vacated.</param>
/// <param name="Host">The room's host after the player left.</param>
/// <param name="RoundProgress">
///     What the departure did to the round in progress, or <see langword="null" /> when no round
///     was in progress or nothing changed.
/// </param>
public sealed record RoomPlayerKicked(
	Room Room,
	BanchoConnection Player,
	int Slot,
	BanchoConnection? Host,
	RoomRoundProgress? RoundProgress)
	: RoomMembershipEvent(Room);

/// <summary>A player moved from one slot to another.</summary>
/// <param name="Room">The room the player is in.</param>
/// <param name="Player">The player who moved.</param>
/// <param name="From">The number of the slot they left.</param>
/// <param name="To">The number of the slot they moved to.</param>
public sealed record RoomPlayerMoved(Room Room, BanchoConnection Player, int From, int To)
	: RoomMembershipEvent(Room);

/// <summary>An osu!tourney client started observing the room.</summary>
public sealed record RoomObserverJoined(Room Room, TourneyConnection Observer) : RoomMembershipEvent(Room);

/// <summary>An osu!tourney client stopped observing the room.</summary>
public sealed record RoomObserverLeft(Room Room, TourneyConnection Observer) : RoomMembershipEvent(Room);