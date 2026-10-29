using Basil.Application.Content.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Runtime;

/// <summary>A lobby scoped to one connection.</summary>
/// <remarks>Use a scope for one request or command so permission checks use current authority.</remarks>
public sealed class ScopedLobby : Scoped<UserPermissions, Connection>
{
	/// <summary>Creates a lobby scope for a connection.</summary>
	/// <param name="lobby">The lobby to scope.</param>
	/// <param name="by">The connection acting on the lobby.</param>
	internal ScopedLobby(Lobby lobby, Connection by)
		: base(by, by.EffectivePermissions(lobby.Context.Clock.GetUtcNow()))
	{
		Lobby = lobby;
	}

	/// <summary>The lobby this scope is over.</summary>
	public Lobby Lobby { get; }

	/// <summary>Opens a room for a new match, created by the scoped connection.</summary>
	/// <param name="name">The room's name.</param>
	/// <param name="password">The room's password, or an empty string for none.</param>
	/// <param name="isTournament">Whether the room is a tournament room.</param>
	/// <param name="isPrivate">
	///     Whether the room is private: hidden from the lobby, with a match history that only its
	///     participants, its managers and users with <see cref="UserPermissions.RoomManageViewPrivate" /> can view. Joining is not
	///     affected.
	/// </param>
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
	///     can have at most 4 tournament rooms open unless they hold <see cref="UserPermissions.RoomManageCreateUnlimited" />.
	///     While room creation is locked (<see cref="Locks.Rooms" />), only a user with
	///     <see cref="UserPermissions.RoomManageAny" /> may open a room; anyone else gets Locked.
	///     A tournament room opened with nobody seated closes after 15 minutes unless someone joins. Seating the creator
	///     is reported by <see cref="LobbyRoomOpened" /> through its host.
	/// </remarks>
	/// <exception cref="ArgumentException">The scoped connection is not an osu! client for a non-tournament room.</exception>
	public async Task<(Room? Room, Result<RoomOutcome> Result)> OpenRoomAsync(
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
			? UserPermissions.RoomCreate
			: UserPermissions.RoomCreate | UserPermissions.RoomJoin;
		if (Require(required) is { } missingPermissions)
			return (null, Result<RoomOutcome>.Denied(missingPermissions));

		if ((await Lobby.Context.Settings.GetAsync(cancellationToken)).Locks.HasFlag(Locks.Rooms) &&
		    Require(UserPermissions.RoomManageAny) is not null)
			return (null, RoomOutcome.Locked);

		var creator = By.Record;
		var limited = isTournament && Require(UserPermissions.RoomManageCreateUnlimited) is not null;
		if (limited && Lobby.TooManyRooms(creator)) return (null, RoomOutcome.TooManyRooms);

		// A room opened in game seats its creator; a tournament room seats the creator's osu! client when it may join.
		var seat = isTournament
			? By.User.Bancho is { IsOpen: true } bancho &&
			  bancho.User.EffectivePermissions(now).Allows(UserPermissions.RoomJoin)
				? bancho
				: null
			: (BanchoConnection)By;
		if (!isTournament && Lobby.RoomOf(seat!) is not null) return (null, RoomOutcome.AlreadyInRoom);

		if (!MatchData.IsValidName(name)) return (null, RoomOutcome.InvalidSettings);

		// ponytail: two concurrent opens by one creator can both pass the room limit; count reservations per creator if that matters.
		if (Lobby.Reserve() is not { } id) return (null, RoomOutcome.NoRoomId);

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
			var context = new RoomContext(Lobby.Context.Clock, Lobby.Context.RoomEvents, Lobby, Lobby.Context.Users,
				Lobby.Context.Settings);
			room = new Room(id, match, settings ?? new MatchSettings(), isTournament, context);
			if (!string.IsNullOrEmpty(password)) room.Settings.Password = password;
			if (seat is not null && Lobby.RoomOf(seat) is null && Lobby.TryClaim(seat, room))
				try
				{
					Lobby.SeatCreator(room, seat);
				}
				finally
				{
					Lobby.Release(seat, room);
				}

			Lobby.Add(room);
		}

		DateTimeOffset? closesAt = room.IsTournament && !room.Slots.Any(slot => slot.Player is not null)
			? Lobby.ScheduleClosing(room)
			: null;
		Lobby.Context.Events.Publish(new LobbyRoomOpened(room, room.Authority.Host, closesAt));
		return (room, RoomOutcome.Ok);
	}

	/// <summary>Lists the matches that were or are being played.</summary>
	/// <param name="query">The filter to apply; it is narrowed to what the caller may view.</param>
	/// <param name="page">Which part of the matches to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, one part of the matches, newest first.</returns>
	/// <remarks>Requires <see cref="UserPermissions.RoomManageAny" />.</remarks>
	public async Task<(Result<RoomOutcome> Result, Page<MatchRecord>? Matches)> ListMatchesAsync(
		Persistence.MatchQuery query, PageRequest page, CancellationToken cancellationToken = default)
	{
		if (Require(UserPermissions.RoomManageAny) is { } missing)
			return (Result<RoomOutcome>.Denied(missing), null);

		return (RoomOutcome.Ok,
			await Lobby.Context.Matches.ListAsync(query.LimitTo(Rights, By.Record), page, cancellationToken));
	}

	/// <summary>Tells whether the scoped connection's user may view the data that belongs to a match.</summary>
	/// <param name="match">The match.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> for a public match, and for a private one when the user holds
	///     <see cref="UserPermissions.RoomManageViewPrivate" />, took part in the match, or manages it (its creator, or a referee
	///     that is still one), or sits in or manages the match's open room. A suspended permission does not count.
	/// </returns>
	/// <remarks>
	///     This is the one gate for everything the match owns: the match itself, its rounds, its events, its report and
	///     which scores were played in which round. It does not cover independent resources the match only links to:
	///     scores with their replays and leaderboards, beatmaps and users are read by their own ids as before. The call only
	///     answers the question; it changes nothing and publishes nothing.
	/// </remarks>
	public async Task<bool> CanViewAsync(MatchRecord match, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(match);
		if (!match.Value.IsPrivate || Require(UserPermissions.RoomManageViewPrivate) is null || TakesPartInOpenRoom(match))
			return true;

		// An open room is judged by its live privacy: the stored copy of the flag can lag behind a change, and the
		// stored view would then show a private room to anyone. Participants who left an open room see it once it closes.
		if (Lobby.Find(match.Id) is { IsClosed: false }) return false;
		return await Lobby.Context.Matches.IsVisibleToAsync(match, By.Record, cancellationToken);
	}

	/// <summary>Tells whether the user manages the match's open room or sits in it, which the stored history may not show yet.</summary>
	private bool TakesPartInOpenRoom(MatchRecord match)
	{
		return Lobby.Find(match.Id) is { } room
		       && (room.Authority.IsManagedBy(By.Record) || room.Slots.Find(By.Record) is not null);
	}

	/// <summary>Starts watching the multiplayer lobby.</summary>
	/// <exception cref="ArgumentException">The scoped connection is not an osu! client.</exception>
	public void Watch()
	{
		if (By is not BanchoConnection bancho)
			throw new ArgumentException("Only an osu! client can watch the lobby.", nameof(By));

		if (Lobby.MutableWatchers.Add(bancho)) Lobby.Context.Events.Publish(new LobbyWatcherJoined(bancho));
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
