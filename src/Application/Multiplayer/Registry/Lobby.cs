using System.Collections.Concurrent;
using Basil.Application.Common.Common;
using Basil.Application.Multiplayer.Capabilities;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Scores.Objects;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Registry;

/// <summary>The open multiplayer rooms and the osu! clients watching the multiplayer lobby.</summary>
public sealed class Lobby : IRoomLobbyPort, IScoreBeatmapAcceptance
{
	private const int MaxRoomId = ushort.MaxValue;

	/// <summary>The most tournament rooms one creator can have open.</summary>
	internal const int MaxRoomsPerCreator = 4;

	/// <summary>How long an empty tournament room stays open.</summary>
	internal static readonly TimeSpan EmptyTournamentRoomTimeout = TimeSpan.FromMinutes(15);

	/// <summary>How long before an empty tournament room closes the lobby announces its closing a second time.</summary>
	internal static readonly TimeSpan EmptyRoomWarningBefore = TimeSpan.FromMinutes(5);

	private readonly Dictionary<BanchoConnection, Room> _claims = [];

	private readonly HashSet<int> _reserved = [];
	private readonly ConcurrentDictionary<int, Room> _rooms = new();
	private readonly Lock _sync = new();
	private readonly ConcurrentSet<BanchoConnection> _watchers = [];

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
	public IReadOnlySet<BanchoConnection> Watchers => _watchers;

	/// <summary>Gets a value that indicates whether every room id is taken or reserved, so no room can open.</summary>
	internal bool IsFull
	{
		get
		{
			using var scope = _sync.EnterScope();
			return _rooms.Count + _reserved.Count >= MaxRoomId;
		}
	}

	/// <summary>Gets the osu! clients watching the multiplayer lobby, for changing them.</summary>
	internal ISet<BanchoConnection> MutableWatchers => _watchers;

	Room? IRoomLobbyPort.RoomOf(BanchoConnection player)
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

	void IRoomLobbyPort.Close(Room room, UserRecord? by)
	{
		Close(room, by);
	}

	bool IRoomLobbyPort.TryClaim(BanchoConnection player, Room room)
	{
		return TryClaim(player, room);
	}

	void IRoomLobbyPort.Release(BanchoConnection player, Room room)
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
	public Room? RoomOf(BanchoConnection player)
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
	///     seating. The caller holds no room's scope, and ends the claim with <see cref="Release(BanchoConnection, Room)" />.
	/// </remarks>
	internal bool TryClaim(BanchoConnection player, Room room)
	{
		using var scope = _sync.EnterScope();
		return _claims.TryAdd(player, room) || ReferenceEquals(_claims[player], room);
	}

	/// <summary>Ends the claim a room made on a player.</summary>
	/// <param name="player">The player's game connection.</param>
	/// <param name="room">The room that made the claim.</param>
	internal void Release(BanchoConnection player, Room room)
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
	/// <param name="by">The connection acting on the lobby.</param>
	/// <returns>A short-lived scope for the lobby and connection.</returns>
	public ScopedLobby ScopeTo(Connection by)
	{
		return new ScopedLobby(this, by);
	}

	/// <summary>Stops a closed connection watching the lobby; no rights are needed because the server itself calls it.</summary>
	/// <param name="connection">The closed connection that stopped watching.</param>
	/// <remarks>User-initiated unwatching goes through <see cref="ScopedLobby.Unwatch" />.</remarks>
	public void Unwatch(BanchoConnection connection)
	{
		if (MutableWatchers.Remove(connection)) Context.Events.Publish(new LobbyWatcherLeft(connection));
	}

	/// <summary>Stops the countdown that would close an empty tournament room.</summary>
	/// <param name="room">The room that has a player again.</param>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal static void Occupied(Room room)
	{
		RoomOccupied(room);
	}

	/// <summary>
	///     Closes an empty room now; a tournament room is announced as closing at once and again
	///     <see cref="EmptyRoomWarningBefore" /> before it closes, and closes after
	///     <see cref="EmptyTournamentRoomTimeout" /> if still empty.
	/// </summary>
	/// <param name="room">The room that has no player left.</param>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal void Emptied(Room room)
	{
		if (!room.IsTournament)
		{
			Close(room, null);
			return;
		}

		Context.Events.Publish(new LobbyRoomClosingAnnounced(room, ScheduleClosing(room)));
	}

	/// <summary>Closes a room, ending its match and vacating every slot.</summary>
	/// <param name="room">The room to close.</param>
	/// <param name="by">The user who closed the room, or <see langword="null" /> when the room closed itself.</param>
	/// <remarks>
	///     The caller holds the room's scope. A round in progress ends as aborted. Closing a closed room does nothing.
	///     The room's whole consequence is one <see cref="LobbyRoomClosed" /> event.
	/// </remarks>
	internal void Close(Room room, UserRecord? by)
	{
		RoomOccupied(room);
		if (room.IsClosed) return;

		var aborted = room.EndCurrentRound(true);
		room.DecideForClosing();
		room.CancelCountdown(null);

		var evicted = room.Slots.Where(s => s.Player is not null).Select(s => s.Player!).ToList();
		foreach (var player in evicted) room.Vacate(player);

		room.Match.Value.EndedAt = Context.Clock.GetUtcNow();
		room.Members.MutableObservers.Clear();
		room.IsClosed = true;
		Remove(room);
		Context.Events.Publish(new LobbyRoomClosed(room, by, evicted, aborted));
	}

	private static void RoomOccupied(Room room)
	{
		var timer = room.ClosingTimer;
		room.ClosingTimer = null;
		timer?.Dispose();
	}

	/// <summary>Schedules the closing of a tournament room that stays empty.</summary>
	/// <param name="room">The empty room.</param>
	/// <returns>When the room closes if it is still empty.</returns>
	internal DateTimeOffset ScheduleClosing(Room room)
	{
		var closesAt = Context.Clock.GetUtcNow() + EmptyTournamentRoomTimeout;
		Schedule(room, closesAt - EmptyRoomWarningBefore, () => WarnIfStillEmpty(room, closesAt));
		return closesAt;
	}

	private void Schedule(Room room, DateTimeOffset at, Action action)
	{
		var delay = at - Context.Clock.GetUtcNow();
		var timer = Context.Clock.CreateTimer(_ => Room.Fire(action), null,
			delay > TimeSpan.Zero ? delay : TimeSpan.Zero,
			Timeout.InfiniteTimeSpan);
		var previous = room.ClosingTimer;
		room.ClosingTimer = timer;
		previous?.Dispose();
	}

	private void WarnIfStillEmpty(Room room, DateTimeOffset closesAt)
	{
		using var scope = room.Enter();
		if (room.IsClosed || room.Slots.Any(slot => slot.Player is not null)) return;

		Context.Events.Publish(new LobbyRoomClosingAnnounced(room, closesAt));
		Schedule(room, closesAt, () => CloseIfStillEmpty(room));
	}

	private void CloseIfStillEmpty(Room room)
	{
		using var scope = room.Enter();
		if (!room.IsClosed && !room.Slots.Any(slot => slot.Player is not null)) Close(room, null);
	}

	/// <summary>Gets whether the creator has reached the limit for open tournament rooms.</summary>
	/// <param name="creator">The user who created the rooms.</param>
	/// <returns><see langword="true" /> when the creator has reached the limit.</returns>
	internal bool TooManyRooms(UserRecord creator)
	{
		return _rooms.Values.Count(room => room.IsTournament && creator.Equals(room.Authority.Creator)) >=
		       MaxRoomsPerCreator;
	}

	/// <summary>Seats an online creator in a room and makes them its host.</summary>
	/// <param name="room">The room to join.</param>
	/// <param name="creator">The creator's osu! connection.</param>
	internal static void SeatCreator(Room room, BanchoConnection creator)
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
			case BanchoConnection player:
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
		var by = await Context.UserRepository.GetAsync(BasilBot.Id, cancellationToken) ?? user;
		foreach (var room in Rooms.Values.ToArray())
		{
			if (!room.Authority.Referees.Contains(user)) continue;

			room.RemoveReferee(by, user);
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