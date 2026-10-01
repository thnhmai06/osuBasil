namespace Basil.Application.Multiplayer.Events;

/// <summary>Something happened to a room's countdown.</summary>
public abstract record RoomCountdownEvent(Room Room) : RoomEvent(Room);

/// <summary>A countdown started, replacing any running one.</summary>
/// <param name="Room">The room.</param>
/// <param name="Length">The countdown length.</param>
/// <param name="StartsRound">Whether the round starts when the countdown ends.</param>
/// <param name="EndsAt">When the countdown ends.</param>
public sealed record RoomCountdownStarted(Room Room, TimeSpan Length, bool StartsRound, DateTimeOffset EndsAt)
	: RoomCountdownEvent(Room);

/// <summary>A countdown reached one of its announced marks.</summary>
public sealed record RoomCountdownTicked(Room Room, TimeSpan Remaining) : RoomCountdownEvent(Room);

/// <summary>A countdown was cancelled.</summary>
public sealed record RoomCountdownCancelled(Room Room) : RoomCountdownEvent(Room);

/// <summary>A countdown ended.</summary>
/// <param name="Room">The room.</param>
/// <param name="StartsRound">Whether the round starts now.</param>
public sealed record RoomCountdownElapsed(Room Room, bool StartsRound) : RoomCountdownEvent(Room);