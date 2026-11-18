using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

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