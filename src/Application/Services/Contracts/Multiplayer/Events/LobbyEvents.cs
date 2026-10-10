using Basil.Domain.Users;
using Basil.Application.Services.Contracts.Events;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Multiplayer.Room;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Multiplayer;
using Basil.Domain.Multiplayer.Round;

namespace Basil.Application.Services.Contracts.Multiplayer.Events;

/// <summary>Something happened to the set of open rooms or to who is watching the lobby.</summary>
public abstract record LobbyEvent : Event
{
	/// <summary>Gets the moment the event happened.</summary>
	public DateTimeOffset Timestamp { get; init; }
}

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
	User? By,
	IReadOnlyList<BanchoConnection> Evicted,
	Round? AbortedRound)
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