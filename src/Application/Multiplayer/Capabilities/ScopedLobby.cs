using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Capabilities;

/// <summary>A lobby scoped to one connection.</summary>
/// <remarks>Use a scope for one request or command so privilege checks use current authority.</remarks>
public readonly struct ScopedLobby
{
	/// <summary>Gets the actor this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the privileges the actor held when the scope was created.</summary>
	public UserPrivileges Rights { get; }

	/// <summary>Checks whether the actor holds the right an action needs.</summary>
	/// <param name="required">The right the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal UserPrivileges? Require(UserPrivileges required)
	{
		return ScopedRights.Require(Rights, required);
	}

	/// <summary>Creates a lobby scope for a connection.</summary>
	/// <param name="lobby">The lobby to scope.</param>
	/// <param name="actor">The connection acting on the lobby.</param>
	internal ScopedLobby(Lobby lobby, Connection actor)
	{
		Actor = actor;
		Rights = actor.EffectivePrivileges(lobby.Context.Clock.GetUtcNow());
		Lobby = lobby;
	}

	/// <summary>The lobby this scope is over.</summary>
	public Lobby Lobby { get; }

	/// <summary>Opens a room for a new match, created by the scoped connection.</summary>
	/// <param name="name">The room's name.</param>
	/// <param name="password">The room's password, or an empty string for none.</param>
	/// <param name="source">Where the request comes from: the creator's game client or the API.</param>
	/// <param name="isPrivate">
	///     Whether the room is private: hidden from the lobby, with a match history that only its
	///     participants, its managers and users with <see cref="UserPrivileges.Tournament" /> can view. Joining is
	///     not
	///     affected.
	/// </param>
	/// <param name="settings">The room's initial settings, or <see langword="null" /> for the defaults.</param>
	/// <returns>
	///     The opened room and Ok; otherwise Silenced when a needed privilege is suspended, NotAuthorized when it is not
	///     granted, TooManyRooms, AlreadyInRoom or NoRoomId.
	/// </returns>
	/// <remarks>
	///     A room opened in <see cref="RoomSource.Game" /> seats the scoped connection's osu! client as the first host, and
	///     the call fails with AlreadyInRoom when that client already plays in a room. A room opened through
	///     <see cref="RoomSource.Api" /> seats the creator's osu! client as host if it is online, may join rooms and is not
	///     in any room. A creator can have at most 4 rooms open unless they hold
	///     <see cref="UserPrivileges.Tournament" />.
	///     While room creation is locked (<see cref="Locks.Rooms" />), only a user with
	///     <see cref="UserPrivileges.Tournament" /> may open a room; anyone else gets Locked.
	///     A room opened with nobody seated closes after 15 minutes unless someone joins. Seating the creator
	///     is reported by <see cref="LobbyRoomOpened" /> through its host.
	/// </remarks>
	/// <exception cref="ArgumentException">The scoped connection is not an osu! client for a room opened in game.</exception>
	public async Task<(Room? Room, Result<RoomOutcome> Result)> OpenRoomAsync(
		string name,
		string password,
		RoomSource source,
		bool isPrivate,
		MatchSettings? settings = null)
	{
		if (source is RoomSource.Game && Actor is not StableConnection)
			throw new ArgumentException("A room opened in game needs the creator's osu! client.");

		if (await OpenRefusalAsync(source) is { } refusal) return (null, refusal);

		var seat = SeatFor(source);
		return await Lobby.OpenRoomAsync(Actor.Record, seat, source is RoomSource.Game, name, password, isPrivate,
			settings);
	}

	private async Task<Result<RoomOutcome>?> OpenRefusalAsync(RoomSource source, CancellationToken cancellationToken = default)
	{
		var required = source is RoomSource.Api
			? UserPrivileges.Player
			: UserPrivileges.Player | UserPrivileges.Player;
		if (Require(required) is { } missingPrivileges) return Result<RoomOutcome>.Denied(missingPrivileges);

		if (await Lobby.IsRoomCreationLockedAsync(cancellationToken) &&
		    Require(UserPrivileges.Tournament) is not null)
			return RoomOutcome.Locked;

		var limited = Require(UserPrivileges.Tournament) is not null;
		if (limited && Lobby.TooManyRooms(Actor.Record)) return RoomOutcome.TooManyRooms;

		return null;
	}

	private StableConnection? SeatFor(RoomSource source)
	{
		// A room opened in game seats its creator; a room opened through the API seats the creator's osu! client when it may join.
		return source is RoomSource.Api
			? Actor.User.Stable is { IsOpen: true } stable &&
			  stable.User.EffectivePrivileges(Lobby.Context.Clock.GetUtcNow()).Allows(UserPrivileges.Player)
				? stable
				: null
			: (StableConnection)Actor;
	}

	/// <summary>Lists the matches that were or are being played.</summary>
	/// <param name="query">The filter to apply; it is narrowed to what the caller may view.</param>
	/// <param name="page">Which part of the matches to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, one part of the matches, newest first.</returns>
	/// <remarks>Requires <see cref="UserPrivileges.Tournament" />.</remarks>
	public async Task<(Result<RoomOutcome> Result, Page<MatchRecord>? Matches)> ListMatchesAsync(
		MatchQuery query, PageRequest page, CancellationToken cancellationToken = default)
	{
		if (Require(UserPrivileges.Tournament) is { } missing)
			return (Result<RoomOutcome>.Denied(missing), null);

		return (RoomOutcome.Ok,
			await Lobby.Context.Matches.ListAsync(query.LimitTo(Rights, Actor.Record), page, cancellationToken));
	}

	/// <summary>Tells whether the scoped connection's user may view the data that belongs to a match.</summary>
	/// <param name="match">The match.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> for a public match, and for a private one when the user holds
	///     <see cref="UserPrivileges.Tournament" />, took part in the match, or manages it (its creator, or a
	///     referee
	///     that is still one), or sits in or manages the match's open room. A suspended privilege does not count.
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

		// An open room is judged by its live privacy: the copy of the flag in a caller's record or in the store can lag
		// behind a change. Participants who left an open room see it again once it closes.
		if (Lobby.OpenRoomOf(match) is { } room)
			return !room.Settings.IsPrivate
			       || Require(UserPrivileges.Tournament) is null
			       || room.Involves(Actor.Record);

		return !match.Value.IsPrivate
		       || Require(UserPrivileges.Tournament) is null
		       || await Lobby.Context.Matches.IsVisibleToAsync(match, Actor.Record, cancellationToken);
	}

	/// <summary>Starts watching the multiplayer lobby.</summary>
	/// <exception cref="ArgumentException">The scoped connection is not an osu! client.</exception>
	public void Watch()
	{
		if (Actor is not StableConnection stable)
			throw new ArgumentException("Only an osu! client can watch the lobby.", nameof(Actor));

		Lobby.Watch(stable);
	}

	/// <summary>Stops watching the multiplayer lobby; doing it again does nothing.</summary>
	/// <exception cref="ArgumentException">The scoped connection is not an osu! client.</exception>
	public void Unwatch()
	{
		if (Actor is not StableConnection stable)
			throw new ArgumentException("Only an osu! client can watch the lobby.", nameof(Actor));

		Lobby.Unwatch(stable);
	}
}