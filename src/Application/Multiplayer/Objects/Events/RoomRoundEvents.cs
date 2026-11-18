using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record RoomRoundEvent(Room Room, Round Round) : RoomEvent(Room);

public sealed record RoomRoundProgress(Round Round, bool AllLoaded, bool AllSkipped, bool Completed);

public sealed record RoomRoundStarted(
	Room Room,
	Round Round,
	IReadOnlyList<StableConnection> Players)
	: RoomRoundEvent(Room, Round);

public sealed record RoomRoundAborted(Room Room, Round Round) : RoomRoundEvent(Room, Round);

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