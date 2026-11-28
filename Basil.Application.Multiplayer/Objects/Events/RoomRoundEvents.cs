using Basil.Application.Sessions.Objects;
using Basil.Domain.Spectating;
using Basil.Domain.Multiplayer.Report.Rounds;
using Basil.Domain.Users;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomRoundEvent(Room Room, Round Round) : RoomEvent(Room);

public sealed record RoomRoundStarted(
	Room Room,
	Round Round,
	IReadOnlyList<StableSession> Players)
	: RoomRoundEvent(Room, Round);

public sealed record RoomRoundAborted(Room Room, Round Round) : RoomRoundEvent(Room, Round);

public sealed record RoomRoundLoadExpired(Room Room, Round Round, IReadOnlyList<int> Slots) : RoomRoundEvent(Room, Round);

public sealed record RoomRoundCompleted(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

public sealed record RoomRoundPlayerLoaded(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

public sealed record RoomRoundAllLoaded(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

public sealed record RoomRoundPlayerSkipped(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

public sealed record RoomRoundAllSkipped(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

public sealed record RoomRoundPlayerFailed(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

public sealed record RoomRoundPlayerCompleted(Room Room, Round Round, int Slot) : RoomRoundEvent(Room, Round);

public sealed record RoomRoundScoreSubmitted(Room Room, Round Round, UserRecord Player, RoundScore Score)
	: RoomRoundEvent(Room, Round);

public sealed record RoomRoundEnded(Room Room, Round Round, TimeSpan ScoreWait) : RoomRoundEvent(Room, Round);

public sealed record RoomRoundDecided(
	Room Room,
	Round Round,
	RoundResult? Result,
	IReadOnlyList<UserRecord> Missing) : RoomRoundEvent(Room, Round);

/// <summary>A player's score frame during a round, for everyone who watches the room play.</summary>
/// <param name="Slot">The slot of the player whose frame it is.</param>
/// <param name="Frame">The standing of the player.</param>
/// <param name="Audience">The sessions that receive the frame: the players and the observers.</param>
public sealed record RoomScoreRelayed(Room Room, int Slot, ScoreFrame Frame, IReadOnlyList<Session> Audience)
	: RoomEvent(Room);
