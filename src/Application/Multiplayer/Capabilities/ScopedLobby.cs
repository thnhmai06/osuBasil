using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Shared.Privileges;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Application.Multiplayer.Capabilities;

public readonly struct ScopedLobby
{
	public Connection Actor { get; }

	public UserPrivileges Privileges { get; }

	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	internal ScopedLobby(Lobby lobby, Connection actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(lobby.Context.Clock.GetUtcNow());
		Lobby = lobby;
	}

	public Lobby Lobby { get; }

	public async Task<(Room? Room, RoomOutcome Result)> OpenRoomAsync(
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

	private async Task<RoomOutcome?> OpenRefusalAsync(RoomSource source, CancellationToken ct = default)
	{
		var required = source is RoomSource.Api
			? UserPrivileges.Player
			: UserPrivileges.Player | UserPrivileges.Player;
		if (Lacks(required)) return RoomOutcome.Denied;

		if (await Lobby.IsRoomCreationLockedAsync(ct) &&
		    Lacks(UserPrivileges.Tournament))
			return RoomOutcome.Locked;

		var limited = Lacks(UserPrivileges.Tournament);
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

	public async Task<(RoomOutcome Result, Page<MatchRecord>? Matches)> ListMatchesAsync(
		MatchQuery query, PageRequest page, CancellationToken ct = default)
	{
		if (Lacks(UserPrivileges.Tournament))
			return (RoomOutcome.Denied, null);

		return (RoomOutcome.Ok,
			await Lobby.Context.Matches.ListAsync(query.LimitTo(Privileges, Actor.Record), page, ct));
	}

	public async Task<bool> CanViewAsync(MatchRecord match, CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(match);

		// An open room is judged by its live privacy: the copy of the flag in a caller's record or in the store can lag
		// behind a change. Participants who left an open room see it again once it closes.
		if (Lobby.OpenRoomOf(match) is { } room) return CanView(room);

		return !match.Value.IsPrivate
		       || !Lacks(UserPrivileges.Tournament)
		       || await Lobby.Context.Matches.IsVisibleToAsync(match, Actor.Record, ct);
	}

	public Page<Room> SearchRooms(RoomQuery query, PageRequest page)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(page);

		var scope = this;
		IReadOnlyList<Room> found = [.. Lobby.Rooms.Values.Where(room => !room.IsClosed && scope.CanView(room) && query.Includes(room))
			.OrderBy(room => room.Id).Skip(page.Offset).Take(page.Fetch)];
		var (items, hasNext) = page.Slice(found);
		return new Page<Room>(items, hasNext);
	}

	private bool CanView(Room room)
	{
		return !room.Settings.IsPrivate
		       || !Lacks(UserPrivileges.Tournament)
		       || room.Involves(Actor.Record);
	}

	public void Watch()
	{
		if (Actor is not StableConnection stable)
			throw new ArgumentException("Only an osu! client can watch the lobby.", nameof(Actor));

		Lobby.Watch(stable);
	}

	public void Unwatch()
	{
		if (Actor is not StableConnection stable)
			throw new ArgumentException("Only an osu! client can watch the lobby.", nameof(Actor));

		Lobby.Unwatch(stable);
	}
}