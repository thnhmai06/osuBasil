using Basil.Application.Shared.Events;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Multiplayer.Report.Matches;
using Basil.Domain.Multiplayer.Report.Rounds;
using Basil.Domain.Users;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Application.Multiplayer.Objects.Events;

public abstract record LobbyEvent : Event;

public sealed record LobbyMatchRecovered(MatchRecord Match, IReadOnlyList<Round> AbortedRounds) : LobbyEvent;

public sealed record LobbyRoomOpened(Room Room, StableSession? Host, DateTimeOffset? ClosesAt) : LobbyEvent;

public sealed record LobbyRoomClosed(
	Room Room,
	UserRecord? Actor,
	IReadOnlyList<StableSession> Evicted,
	Round? AbortedRound)
	: LobbyEvent;

public sealed record LobbyConnectionReleased(Session Session) : LobbyEvent;

public sealed record LobbyRoomClosingAnnounced(Room Room, DateTimeOffset ClosesAt) : LobbyEvent;

public sealed record LobbyWatcherJoined(StableSession Watcher) : LobbyEvent;

public sealed record LobbyWatcherLeft(StableSession Watcher) : LobbyEvent;