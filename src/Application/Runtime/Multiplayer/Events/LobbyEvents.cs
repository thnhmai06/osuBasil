using Basil.Application.Runtime.Connections;
using Basil.Common.Application.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Multiplayer.Events;

/// <summary>Something happened to the set of open rooms or to who is watching the lobby.</summary>
public abstract record LobbyEvent : Event;

/// <summary>A match found unfinished at start-up was ended.</summary>
/// <param name="Match">The match, with its end set.</param>
/// <param name="AbortedRounds">The rounds of the match that were still open and ended as aborted.</param>
public sealed record LobbyMatchRecovered(MatchRecord Match, IReadOnlyList<Round> AbortedRounds) : LobbyEvent;

/// <summary>A room was opened.</summary>
/// <param name="Room">The room that was opened.</param>
/// <param name="Host">The room's first host, or <see langword="null" /> when nobody was seated.</param>
/// <param name="ClosesAt">
///     When the room closes if no player joins: set for a tournament room opened with nobody seated,
///     otherwise <see langword="null" />.
/// </param>
public sealed record LobbyRoomOpened(Room Room, BanchoConnection? Host, DateTimeOffset? ClosesAt) : LobbyEvent;

/// <summary>A room was closed; it emits nothing afterwards.</summary>
/// <param name="Room">The room that was closed.</param>
/// <param name="By">
///     The user who closed the room, or <see langword="null" /> when the room closed itself because it stayed empty.
/// </param>
/// <param name="Evicted">The players who were still seated.</param>
/// <param name="AbortedRound">
///     The round that was in progress and ended as aborted when the room closed, or
///     <see langword="null" /> when none was.
/// </param>
public sealed record LobbyRoomClosed(
	Room Room,
	UserRecord? By,
	IReadOnlyList<BanchoConnection> Evicted,
	Round? AbortedRound)
	: LobbyEvent;

/// <summary>A connection was released from every open room.</summary>
/// <param name="Connection">The connection that was released.</param>
public sealed record LobbyConnectionReleased(Connection Connection) : LobbyEvent;

/// <summary>An empty tournament room will close unless a player joins.</summary>
/// <remarks>Announced when the room becomes empty after having players, and again shortly before it closes.</remarks>
/// <param name="Room">The empty room.</param>
/// <param name="ClosesAt">When the room closes if it is still empty.</param>
public sealed record LobbyRoomClosingAnnounced(Room Room, DateTimeOffset ClosesAt) : LobbyEvent;

/// <summary>An osu! client started watching the multiplayer lobby.</summary>
public sealed record LobbyWatcherJoined(BanchoConnection Watcher) : LobbyEvent;

/// <summary>An osu! client stopped watching the multiplayer lobby.</summary>
public sealed record LobbyWatcherLeft(BanchoConnection Watcher) : LobbyEvent;