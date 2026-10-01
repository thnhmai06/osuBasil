namespace Basil.Application.Multiplayer.Events;

/// <summary>Something happened to a room's countdown.</summary>
public abstract record CountdownEvent(Room Room) : RoomEvent(Room);

/// <summary>A countdown started, replacing any running one.</summary>
/// <param name="Room">The room.</param>
/// <param name="Length">The countdown length.</param>
/// <param name="StartsRound">Whether the round starts when the countdown ends.</param>
/// <param name="EndsAt">When the countdown ends.</param>
public sealed record CountdownStarted(Room Room, TimeSpan Length, bool StartsRound, DateTimeOffset EndsAt)
	: CountdownEvent(Room);

/// <summary>A countdown reached one of its announced marks.</summary>
public sealed record CountdownTick(Room Room, TimeSpan Remaining) : CountdownEvent(Room);

/// <summary>A countdown was cancelled.</summary>
public sealed record CountdownCancelled(Room Room) : CountdownEvent(Room);

/// <summary>A countdown ended.</summary>
/// <param name="Room">The room.</param>
/// <param name="StartsRound">Whether the round starts now.</param>
public sealed record CountdownElapsed(Room Room, bool StartsRound) : CountdownEvent(Room);