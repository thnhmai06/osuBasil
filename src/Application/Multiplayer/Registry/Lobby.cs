using System.Collections.Concurrent;
using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Capabilities;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Scores.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Registry;

/// <summary>The open multiplayer rooms and the osu! clients watching the multiplayer lobby.</summary>
public sealed class Lobby : IRoomLobbyPort, IScoreBeatmapAcceptance
{
	private const int MaxRoomId = ushort.MaxValue;

	/// <summary>The most rooms one creator can have open.</summary>
	internal const int MaxRoomsPerCreator = 4;

	/// <summary>How long an empty room stays open.</summary>
	public static readonly TimeSpan EmptyRoomTimeout = TimeSpan.FromMinutes(15);

	/// <summary>How long before an empty room closes the lobby announces its closing a second time.</summary>
	public static readonly TimeSpan EmptyRoomWarningBefore = TimeSpan.FromMinutes(5);

	private readonly Dictionary<StableConnection, Room> _claims = [];

	private readonly HashSet<int> _reserved = [];
	private readonly ConcurrentDictionary<int, Room> _rooms = new();
	private readonly Lock _sync = new();
	private readonly ConcurrentSet<StableConnection> _watchers = [];

	/// <summary>Creates the lobby and its runtime dependencies.</summary>
	/// <param name="context">The ports through which the lobby performs its behaviour.</param>
	internal Lobby(LobbyContext context)
	{
		Context = context;
	}

	/// <summary>The runtime context for lobby behaviour.</summary>
	internal LobbyContext Context { get; }

	/// <summary>Gets every open room by its id.</summary>
	public IReadOnlyDictionary<int, Room> Rooms => _rooms;

	/// <summary>Gets the osu! clients watching the multiplayer lobby.</summary>
	public IReadOnlySet<StableConnection> Watchers => _watchers;

	/// <summary>Gets the osu! clients watching the multiplayer lobby, for changing them.</summary>
	internal ISet<StableConnection> MutableWatchers => _watchers;

	Room? IRoomLobbyPort.RoomOf(StableConnection player)
	{
		return RoomOf(player);
	}

	void IRoomLobbyPort.Occupied(Room room)
	{
		Occupied(room);
	}

	void IRoomLobbyPort.Emptied(Room room)
	{
		Emptied(room);
	}

	void IRoomLobbyPort.Close(Room room, UserRecord? actor)
	{
		Close(room, actor);
	}

	bool IRoomLobbyPort.TryClaim(StableConnection player, Room room)
	{
		return TryClaim(player, room);
	}

	void IRoomLobbyPort.Release(StableConnection player, Room room)
	{
		Release(player, room);
	}

	bool IScoreBeatmapAcceptance.AcceptsUnknown(UserRecord player, Md5 beatmap)
	{
		return _rooms.Values.Any(room => room.AwaitsScore(player, beatmap));
	}

	/// <summary>Finds an open room by its id.</summary>
	/// <param name="id">The room's id.</param>
	/// <returns>
	///     The room; <see langword="null" /> when there is no open room with the id. A private room is found like any other,
	///     because private hides a match from the lobby and its history from the public, not the live room.
	/// </returns>
	public Room? Find(int id)
	{
		return _rooms.TryGetValue(id, out var room) && !room.IsClosed ? room : null;
	}

	/// <summary>Finds the room a player is seated in.</summary>
	/// <param name="player">The player's game connection.</param>
	/// <returns>The room containing the player, or <see langword="null" />.</returns>
	public Room? RoomOf(StableConnection player)
	{
		// ponytail: scans every room; add a player index if room counts grow large.
		return _rooms.Values.FirstOrDefault(room => room.Slots.Find(player) is not null);
	}

	/// <summary>Reserves the lowest free room id so no other room takes it.</summary>
	/// <returns>The reserved id, or <see langword="null" /> when every room id is taken or reserved.</returns>
	internal int? Reserve()
	{
		using var scope = _sync.EnterScope();
		if (_rooms.Count + _reserved.Count >= MaxRoomId) return null;

		for (var id = 1; id <= MaxRoomId; id++)
		{
			if (_rooms.ContainsKey(id) || !_reserved.Add(id)) continue;
			return id;
		}

		return null;
	}

	/// <summary>Marks that a room is seating a player, so that no other room seats the same player at the same time.</summary>
	/// <param name="player">The player's game connection.</param>
	/// <param name="room">The room about to seat the player.</param>
	/// <returns><see langword="false" /> when another room is seating the same player right now.</returns>
	/// <remarks>
	///     Seated players are found by <see cref="RoomOf" />; this covers only the moment between a room's check and its
	///     seating. The caller holds no room's scope, and ends the claim with
	///     <see cref="Release(StableConnection, Room)" />.
	/// </remarks>
	internal bool TryClaim(StableConnection player, Room room)
	{
		using var scope = _sync.EnterScope();
		return _claims.TryAdd(player, room) || ReferenceEquals(_claims[player], room);
	}

	/// <summary>Ends the claim a room made on a player.</summary>
	/// <param name="player">The player's game connection.</param>
	/// <param name="room">The room that made the claim.</param>
	internal void Release(StableConnection player, Room room)
	{
		using var scope = _sync.EnterScope();
		if (_claims.TryGetValue(player, out var claimed) && ReferenceEquals(claimed, room)) _claims.Remove(player);
	}

	/// <summary>Frees a reserved room id that no room took.</summary>
	/// <param name="id">The reserved id.</param>
	internal void Release(int id)
	{
		using var scope = _sync.EnterScope();
		_reserved.Remove(id);
	}

	/// <summary>Lists a room as open under the id reserved for it.</summary>
	/// <param name="room">The room, whose <see cref="Room.Id" /> was reserved with <see cref="Reserve" />.</param>
	internal void Add(Room room)
	{
		using var scope = _sync.EnterScope();
		_rooms[room.Id] = room;
		_reserved.Remove(room.Id);
	}

	/// <summary>Removes a room from the open rooms.</summary>
	/// <param name="room">The room to remove.</param>
	/// <returns><see langword="true" /> if that room was open; <see langword="false" /> if it was not listed under its id.</returns>
	internal bool Remove(Room room)
	{
		return _rooms.TryRemove(new KeyValuePair<int, Room>(room.Id, room));
	}

	/// <summary>Enters the short scope in which a room id is assigned and a creator's open rooms are counted.</summary>
	/// <returns>The scope, to dispose when done.</returns>
	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}

	/// <summary>Creates a lobby scope for a connection.</summary>
	/// <param name="actor">The connection acting on the lobby.</param>
	/// <returns>A short-lived scope for the lobby and connection.</returns>
	public ScopedLobby ScopeTo(Connection actor)
	{
		return new ScopedLobby(this, actor);
	}

	/// <summary>Stops a closed connection watching the lobby; no rights are needed because the server itself calls it.</summary>
	/// <param name="connection">The closed connection that stopped watching.</param>
	/// <remarks>User-initiated unwatching goes through <see cref="ScopedLobby.Unwatch" />.</remarks>
	public void Unwatch(StableConnection connection)
	{
		if (MutableWatchers.Remove(connection)) Context.Events.Publish(new LobbyWatcherLeft(connection));
	}

	/// <summary>Stops the clock that would close an empty room.</summary>
	/// <param name="room">The room that has a player again.</param>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal static void Occupied(Room room)
	{
		RoomOccupied(room);
	}

	/// <summary>
	///     Starts the clock that closes an empty room and announces the closing; the room closes after
	///     <see cref="EmptyRoomTimeout" /> if still empty, see <see cref="CloseEmptyRooms" />.
	/// </summary>
	/// <param name="room">The room that has no player left.</param>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal void Emptied(Room room)
	{
		Context.Events.Publish(new LobbyRoomClosingAnnounced(room, StartEmptyClock(room)));
	}

	/// <summary>Closes a room, ending its match and vacating every slot.</summary>
	/// <param name="room">The room to close.</param>
	/// <param name="actor">The user who closed the room, or <see langword="null" /> when the room closed itself.</param>
	/// <remarks>
	///     The caller holds the room's scope. A round in progress ends as aborted. Closing a closed room does nothing.
	///     The room's whole consequence is one <see cref="LobbyRoomClosed" /> event.
	/// </remarks>
	internal void Close(Room room, UserRecord? actor)
	{
		RoomOccupied(room);
		if (room.IsClosed) return;

		var aborted = room.EndCurrentRound(true);
		room.DecideForClosing();

		var evicted = room.Slots.Where(s => s.Player is not null).Select(s => s.Player!).ToList();
		foreach (var player in evicted) room.Vacate(player);

		room.Match.Value.EndedAt = Context.Clock.GetUtcNow();
		room.Members.MutableObservers.Clear();
		room.IsClosed = true;
		Remove(room);
		Context.Events.Publish(new LobbyRoomClosed(room, actor, evicted, aborted));
	}

	private static void RoomOccupied(Room room)
	{
		room.EmptySince = null;
		room.ClosingWarned = false;
	}

	/// <summary>Starts the clock of a room that has no player.</summary>
	/// <param name="room">The empty room.</param>
	/// <returns>When the room closes if it is still empty.</returns>
	internal DateTimeOffset StartEmptyClock(Room room)
	{
		var now = Context.Clock.GetUtcNow();
		room.EmptySince = now;
		room.ClosingWarned = false;
		return now + EmptyRoomTimeout;
	}

	/// <summary>
	///     Closes the rooms that have stayed empty for <see cref="EmptyRoomTimeout" /> and announces the closing of those that
	///     are <see cref="EmptyRoomWarningBefore" /> from it, when the server itself calls it; it needs no rights.
	/// </summary>
	/// <returns>How many rooms were closed.</returns>
	public int CloseEmptyRooms()
	{
		var closed = 0;
		foreach (var room in _rooms.Values.ToArray())
		{
			using var scope = room.Enter();
			if (room.IsClosed || room.EmptySince is not { } since || room.Slots.Any(slot => slot.Player is not null))
				continue;

			var closesAt = since + EmptyRoomTimeout;
			var now = Context.Clock.GetUtcNow();
			if (now >= closesAt)
			{
				Close(room, null);
				closed++;
			}
			else if (!room.ClosingWarned && now >= closesAt - EmptyRoomWarningBefore)
			{
				room.ClosingWarned = true;
				Context.Events.Publish(new LobbyRoomClosingAnnounced(room, closesAt));
			}
		}

		return closed;
	}

	/// <summary>Gets whether the creator has reached the limit for open rooms.</summary>
	/// <param name="creator">The user who created the rooms.</param>
	/// <returns><see langword="true" /> when the creator has reached the limit.</returns>
	internal bool TooManyRooms(UserRecord creator)
	{
		return _rooms.Values.Count(room => creator.Equals(room.Authority.Creator)) >= MaxRoomsPerCreator;
	}

	/// <summary>Gets whether opening rooms is locked.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> while room creation is locked.</returns>
	internal async Task<bool> IsRoomCreationLockedAsync(CancellationToken cancellationToken)
	{
		return (await Context.Settings.GetAsync(cancellationToken)).Locks.HasFlag(Locks.Rooms);
	}

	/// <summary>Opens a room for a new match and registers it.</summary>
	/// <param name="creator">The user who creates the room.</param>
	/// <param name="seat">The creator's osu! client to seat as host, or <see langword="null" /> to seat nobody.</param>
	/// <param name="name">The room's name.</param>
	/// <param name="password">The room's password, or an empty string for none.</param>
	/// <param name="isPrivate">Whether the room is private.</param>
	/// <param name="settings">The room's initial settings, or <see langword="null" /> for the defaults.</param>
	/// <param name="seatsFromGame">Whether the room is opened in game, which fails when the client already plays in a room.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The opened room and Ok; otherwise no room and AlreadyInRoom, InvalidSettings or NoRoomId.</returns>
	internal async Task<(Room? Room, RoomOutcome Outcome)> OpenRoomAsync(UserRecord creator, StableConnection? seat,
		bool seatsFromGame, string name, string password, bool isPrivate, MatchSettings? settings,
		CancellationToken cancellationToken)
	{
		var now = Context.Clock.GetUtcNow();
		if (seatsFromGame && RoomOf(seat!) is not null) return (null, RoomOutcome.AlreadyInRoom);

		if (!MatchData.IsValidName(name)) return (null, RoomOutcome.InvalidSettings);

		// ponytail: two concurrent opens by one creator can both pass the room limit; count reservations per creator if that matters.
		if (Reserve() is not { } id) return (null, RoomOutcome.NoRoomId);

		var match = await CreateMatchAsync(id, name, creator, isPrivate, now, cancellationToken);
		var room = Establish(id, match, settings, password, seat);

		var closesAt = !room.Slots.Any(slot => slot.Player is not null) ? StartEmptyClock(room) : (DateTimeOffset?)null;
		Context.Events.Publish(new LobbyRoomOpened(room, room.Authority.Host, closesAt));
		return (room, RoomOutcome.Ok);
	}

	private async Task<MatchRecord> CreateMatchAsync(int id, string name, UserRecord creator, bool isPrivate,
		DateTimeOffset now, CancellationToken cancellationToken)
	{
		try
		{
			return await Context.Matches.CreateAsync(new MatchData
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
			Release(id);
			throw;
		}
	}

	private Room Establish(int id, MatchRecord match, MatchSettings? settings, string password,
		StableConnection? seat)
	{
		Room room;
		using (Enter())
		{
			var context = new RoomContext(Context.Clock, Context.RoomEvents, this, Context.Users, Context.Settings);
			room = new Room(id, match, settings ?? new MatchSettings(), context);
			if (!string.IsNullOrEmpty(password)) room.Settings.Password = password;
			if (seat is not null && RoomOf(seat) is null && TryClaim(seat, room))
				try
				{
					SeatCreator(room, seat);
				}
				finally
				{
					Release(seat, room);
				}

			Add(room);
		}

		return room;
	}

	/// <summary>Finds the open room that runs a match: a room's id is not its match's id.</summary>
	/// <param name="match">The match.</param>
	/// <returns>The open room, or <see langword="null" /> when the match has no open room.</returns>
	internal Room? OpenRoomOf(MatchRecord match)
	{
		return Rooms.Values.FirstOrDefault(room => !room.IsClosed && room.Match.Id == match.Id);
	}

	/// <summary>Starts watching the lobby for an osu! client.</summary>
	/// <param name="connection">The client that watches.</param>
	internal void Watch(StableConnection connection)
	{
		if (MutableWatchers.Add(connection)) Context.Events.Publish(new LobbyWatcherJoined(connection));
	}

	/// <summary>Seats an online creator in a room and makes them its host.</summary>
	/// <param name="room">The room to join.</param>
	/// <param name="creator">The creator's osu! connection.</param>
	internal static void SeatCreator(Room room, StableConnection creator)
	{
		if (room.Seat(creator) is null) return;
		room.Authority.Host = creator;
	}

	/// <summary>Releases a connection from room memberships when the server itself calls it; it needs no rights.</summary>
	/// <param name="connection">The connection to release.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public Task ReleaseAsync(Connection connection, CancellationToken cancellationToken = default)
	{
		switch (connection)
		{
			case StableConnection player:
			{
				RoomOf(player)?.Leave(player);
				break;
			}
			case TourneyConnection observer:
			{
				foreach (var room in Rooms.Values.Where(room => room.Members.Observers.Contains(observer)).ToArray())
					room.ObserverLeave(observer);

				break;
			}
		}

		Context.Events.Publish(new LobbyConnectionReleased(connection));
		return Task.CompletedTask;
	}

	/// <summary>Removes a user from rooms when the server itself calls it; it needs no rights.</summary>
	/// <param name="user">The user whose multiplayer access is suspended.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public async Task RemoveSuspendedAsync(User user, CancellationToken cancellationToken = default)
	{
		if (user.EffectivePermissions(Context.Clock.GetUtcNow()).Allows(UserPermissions.RoomJoin)) return;

		foreach (var connection in user.Connections)
			await ReleaseAsync(connection, cancellationToken);

		await RemoveRefereeAsync(user.Record, cancellationToken);
	}

	/// <summary>Removes a referee from open rooms when the server itself calls it; it needs no rights.</summary>
	/// <param name="user">The user to remove from referee lists.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public async Task RemoveRefereeAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		if (await Context.UserRepository.GetAsync(BasilBot.Id, cancellationToken) is not { } actor) return;

		foreach (var room in Rooms.Values.ToArray())
		{
			if (!room.Authority.Referees.Contains(user)) continue;

			room.RemoveReferee(actor, user);
		}
	}

	/// <summary>Closes every open room when the server itself calls it at shutdown; it needs no rights.</summary>
	/// <returns>How many rooms were closed.</returns>
	/// <remarks>Each room ends its match and its round in progress, and publishes one <see cref="LobbyRoomClosed" />.</remarks>
	public int CloseAll()
	{
		var closed = 0;
		foreach (var room in Rooms.Values.ToArray())
		{
			if (room.IsClosed) continue;

			room.Close(null);
			closed++;
		}

		return closed;
	}

	/// <summary>Closes unfinished matches when the server itself calls it at startup; it needs no rights.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many matches were ended.</returns>
	public async Task<int> CloseUnfinishedAsync(CancellationToken cancellationToken = default)
	{
		var allMatches = await Paging.ListAllAsync(
			page => Context.Matches.ListAsync(new MatchQuery(false), page, cancellationToken),
			cancellationToken);

		var now = Context.Clock.GetUtcNow();

		foreach (var match in allMatches)
		{
			var aborted = (await Context.Rounds.ListAsync(match, cancellationToken)).Where(r => r.EndedAt is null)
				.ToList();
			foreach (var round in aborted)
			{
				round.EndedAt = now;
				round.Aborted = true;
			}

			match.Value.EndedAt = now;
			Context.Events.Publish(new LobbyMatchRecovered(match, aborted));
		}

		return allMatches.Count;
	}
}