using Basil.Application.Common.Events;
using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer.Events;

/// <summary>Something happened to the set of open rooms or to who is watching the lobby.</summary>
public abstract record LobbyEvent : Event;

/// <summary>A room was opened.</summary>
/// <param name="Room">The room that was opened.</param>
/// <param name="Host">The room's first host, or <see langword="null" /> when nobody was seated.</param>
public sealed record RoomOpened(Room Room, BanchoConnection? Host) : LobbyEvent;

/// <summary>A room was closed; it emits nothing afterwards.</summary>
/// <param name="Room">The room that was closed.</param>
/// <param name="Evicted">The players who were still seated.</param>
public sealed record RoomClosed(Room Room, IReadOnlyList<BanchoConnection> Evicted) : LobbyEvent;

/// <summary>An empty tournament room closes soon unless a player joins.</summary>
/// <param name="Room">The empty room.</param>
/// <param name="ClosesAt">When the room closes if it is still empty.</param>
public sealed record EmptyRoomClosingSoon(Room Room, DateTimeOffset ClosesAt) : LobbyEvent;

/// <summary>An osu! client started watching the multiplayer lobby.</summary>
public sealed record LobbyWatcherJoined(BanchoConnection Watcher) : LobbyEvent;

/// <summary>An osu! client stopped watching the multiplayer lobby.</summary>
public sealed record LobbyWatcherLeft(BanchoConnection Watcher) : LobbyEvent;