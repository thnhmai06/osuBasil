using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Content;
using Basil.Domain.Multiplayer.Matches;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Multiplayer;

/// <summary>A lobby scoped to one connection.</summary>
/// <remarks>Use a scope for one request or command so permission checks use current authority.</remarks>
public sealed class ScopedLobby
{
	/// <summary>The lobby this scope is over.</summary>
	public Lobby Lobby { get; }

	/// <summary>The connection this scope is for.</summary>
	public Connection By { get; }

	/// <summary>Creates a lobby scope for a connection.</summary>
	/// <param name="lobby">The lobby to scope.</param>
	/// <param name="by">The connection acting on the lobby.</param>
	internal ScopedLobby(Lobby lobby, Connection by)
	{
		Lobby = lobby;
		By = by;
	}

	/// <summary>Opens a room for a new match, created by the scoped connection.</summary>
	/// <param name="name">The room's name.</param>
	/// <param name="password">The room's password, or an empty string for none.</param>
	/// <param name="isTournament">Whether the room is a tournament room.</param>
	/// <param name="isPrivate">Whether the room's history is private.</param>
	/// <param name="settings">The room's initial settings, or <see langword="null" /> for the defaults.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The opened room and Ok; otherwise Silenced when a needed permission is suspended, NotAuthorized when it is not
	///     granted, TooManyRooms, AlreadyInRoom or NoRoomId.
	/// </returns>
	/// <remarks>
	///     A room that is not a tournament room is opened in game: the scoped connection's osu! client is seated as the first
	///     host, and the call fails with AlreadyInRoom when that client already plays in a room. When a tournament room opens,
	///     the creator's osu! client is seated as host if it is online, may join rooms and is not in any room. A creator
	///     can have at most 4 tournament rooms open unless they hold <see cref="Permissions.RoomCreateUnlimited" />.
	///     While room creation is locked (<see cref="CreationLocks.Rooms" />), only a user with
	///     <see cref="Permissions.RoomManageAny" /> may open a room; anyone else gets NotAuthorized.
	///     A tournament room opened with nobody seated closes after 15 minutes unless someone joins. Seating the creator
	///     is reported by <see cref="LobbyRoomOpened" /> through its host.
	/// </remarks>
	/// <exception cref="ArgumentException">The scoped connection is not an osu! client for a non-tournament room.</exception>
	public async Task<(Room? Room, RoomResult Result)> OpenRoomAsync(
		string name,
		string password,
		bool isTournament,
		bool isPrivate,
		MatchSettings? settings = null,
		CancellationToken cancellationToken = default)
	{
		if (!isTournament && By is not BanchoConnection)
			throw new ArgumentException("A room opened in game needs the creator's osu! client.");

		var now = Lobby.Context.Clock.GetUtcNow();
		var required = isTournament
			? Permissions.RoomCreate
			: Permissions.RoomCreate | Permissions.RoomJoin;
		var access = By.Session.Check(required, now);
		if (access is Access.NotGranted) return (null, RoomResult.NotAuthorized);
		if (access is Access.Suspended) return (null, RoomResult.Silenced);

		if ((await Lobby.Context.Settings.GetAsync(cancellationToken)).LockedCreation.HasFlag(CreationLocks.Rooms) &&
		    By.Session.Check(Permissions.RoomManageAny, now) is not Access.Allowed)
			return (null, RoomResult.NotAuthorized);

		var creator = By.User;
		var limited = isTournament && By.Session.Check(Permissions.RoomCreateUnlimited, now) is not Access.Allowed;
		if (limited && Lobby.TooManyRooms(creator)) return (null, RoomResult.TooManyRooms);

		// A room opened in game seats its creator; a tournament room seats the creator's osu! client when it may join.
		var seat = isTournament
			? By.Session.Bancho is { IsOpen: true } bancho &&
			  bancho.Session.Check(Permissions.RoomJoin, now) is Access.Allowed
				? bancho
				: null
			: (BanchoConnection)By;
		if (!isTournament && Lobby.RoomOf(seat!) is not null) return (null, RoomResult.AlreadyInRoom);

		if (!MatchData.IsValidName(name)) return (null, RoomResult.InvalidSettings);

		// ponytail: two concurrent opens by one creator can both pass the room limit; count reservations per creator if that matters.
		if (Lobby.Reserve() is not { } id) return (null, RoomResult.NoRoomId);

		MatchRecord match;
		try
		{
			match = await Lobby.Context.Matches.CreateAsync(new MatchData
			{
				Name = name,
				StartedAt = now,
				EndedAt = null,
				Creator = creator,
				IsPrivate = isPrivate
			}, cancellationToken);
		}
		catch
		{
			Lobby.Release(id);
			throw;
		}

		Room room;
		using (Lobby.Enter())
		{
			var context = new RoomContext(Lobby.Context.Clock, Lobby.Context.RoomEvents, Lobby, Lobby.Context.Chat,
				Lobby.Context.Users);
			room = new Room(id, match, settings ?? new MatchSettings(), isTournament, context);
			if (!string.IsNullOrEmpty(password)) room.Settings.Password = password;
			if (seat is not null && Lobby.RoomOf(seat) is null) Lobby.SeatCreator(room, seat);
			Lobby.Add(room);
		}

		DateTimeOffset? closesAt = room.IsTournament && !room.Slots.Any(slot => slot.Player is not null)
			? Lobby.ScheduleClosing(room)
			: null;
		Lobby.Context.Events.Emit(new LobbyRoomOpened(room, room.Authority.Host, closesAt));
		return (room, RoomResult.Ok);
	}

	/// <summary>Starts watching the multiplayer lobby.</summary>
	/// <exception cref="ArgumentException">The scoped connection is not an osu! client.</exception>
	public void Watch()
	{
		if (By is not BanchoConnection bancho)
			throw new ArgumentException("Only an osu! client can watch the lobby.", nameof(By));

		if (Lobby.MutableWatchers.Add(bancho)) Lobby.Context.Events.Emit(new LobbyWatcherJoined(bancho));
	}

	/// <summary>Stops watching the multiplayer lobby; doing it again does nothing.</summary>
	/// <exception cref="ArgumentException">The scoped connection is not an osu! client.</exception>
	public void Unwatch()
	{
		if (By is not BanchoConnection bancho)
			throw new ArgumentException("Only an osu! client can watch the lobby.", nameof(By));

		Lobby.Unwatch(bancho);
	}
}
