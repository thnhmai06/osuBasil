using Basil.Application.Contracts.Events;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Contracts.Multiplayer.Events;

/// <summary>Something happened to the set of open rooms or to who is watching the lobby.</summary>
public abstract record LobbyEvent : Event;

/// <summary>A room was opened.</summary>
/// <param name="Room">The room that was opened.</param>
/// <param name="Host">The room's first host, or <see langword="null" /> when nobody was seated.</param>
/// <param name="ClosesAt">When the room closes if no player joins: set for a tournament room opened with nobody seated, otherwise <see langword="null" />.</param>
public sealed record LobbyRoomOpened(Room Room, BanchoConnection? Host, DateTimeOffset? ClosesAt) : LobbyEvent;

/// <summary>A room was closed; it emits nothing afterwards.</summary>
/// <param name="Room">The room that was closed.</param>
/// <param name="Evicted">The players who were still seated.</param>
/// <param name="AbortedRound">The round that was in progress and ended as aborted when the room closed, or <see langword="null" /> when none was.</param>
public sealed record LobbyRoomClosed(Room Room, IReadOnlyList<BanchoConnection> Evicted, Round? AbortedRound)
	: LobbyEvent;

/// <summary>An empty tournament room will close unless a player joins.</summary>
/// <remarks>Announced when the room becomes empty after having players, and again shortly before it closes.</remarks>
/// <param name="Room">The empty room.</param>
/// <param name="ClosesAt">When the room closes if it is still empty.</param>
public sealed record LobbyRoomClosingAnnounced(Room Room, DateTimeOffset ClosesAt) : LobbyEvent;

/// <summary>An osu! client started watching the multiplayer lobby.</summary>
public sealed record LobbyWatcherJoined(BanchoConnection Watcher) : LobbyEvent;

/// <summary>An osu! client stopped watching the multiplayer lobby.</summary>
public sealed record LobbyWatcherLeft(BanchoConnection Watcher) : LobbyEvent;