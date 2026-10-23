using System.Collections.Concurrent;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Sessions;
using Basil.Application.Runtime.Users;
using Basil.Application.Persistence.Common;
using Basil.Application.Persistence.Multiplayer;
using Basil.Domain.Common;
using Basil.Domain.Multiplayer;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Multiplayer;

/// <summary>The open multiplayer rooms and the osu! clients watching the multiplayer lobby.</summary>
public sealed class Lobby : IRoomLobbyPort, IConnectionLobbyPort
{
	private const int MaxRoomId = ushort.MaxValue;
	private readonly HashSet<int> _reserved = [];
	private readonly ConcurrentDictionary<int, Room> _rooms = new();
	private readonly Lock _sync = new();
	private readonly ConcurrentSet<BanchoConnection> _watchers = [];

	/// <summary>The most tournament rooms one creator can have open.</summary>
	internal const int MaxRoomsPerCreator = 4;

	/// <summary>How long an empty tournament room stays open.</summary>
	internal static readonly TimeSpan EmptyTournamentRoomTimeout = TimeSpan.FromMinutes(15);

	/// <summary>How long before an empty tournament room closes the lobby announces its closing a second time.</summary>
	internal static readonly TimeSpan EmptyRoomWarningBefore = TimeSpan.FromMinutes(5);

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

	/// <summary>Finds an open room by its id.</summary>
	/// <param name="id">The room's id.</param>
	/// <returns>
	///     The room; <see langword="null" /> when there is no open room with the id. A private room is found like any other,
	///     because private only keeps a room out of listings.
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

	/// <summary>Gets the osu! clients watching the multiplayer lobby, for changing them.</summary>
	internal ISet<BanchoConnection> MutableWatchers => _watchers;

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
		room.CancelCountdown(null);

		var evicted = room.Slots.Where(s => s.Player is not null).Select(s => s.Player!).ToList();
		foreach (var player in evicted) room.Vacate(player);

		room.Match.Value.EndedAt = Context.Clock.GetUtcNow();
		room.Members.MutableObservers.Clear();
		room.IsClosed = true;
		room.Channel.Close();
		Remove(room);
		Context.Events.Publish(new LobbyRoomClosed(room, by, evicted, aborted));
	}

	Room? IRoomLobbyPort.RoomOf(BanchoConnection player)
	{
		return RoomOf(player);
	}

	Room? IConnectionLobbyPort.RoomOf(BanchoConnection player)
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
		Schedule(room, closesAt - EmptyRoomWarningBefore, () => WarnIfStillEmptyAsync(room, closesAt));
		return closesAt;
	}

	private void Schedule(Room room, DateTimeOffset at, Func<Task> action)
	{
		var delay = at - Context.Clock.GetUtcNow();
		var timer = Context.Clock.CreateTimer(_ => action(), null, delay > TimeSpan.Zero ? delay : TimeSpan.Zero,
			Timeout.InfiniteTimeSpan);
		var previous = room.ClosingTimer;
		room.ClosingTimer = timer;
		previous?.Dispose();
	}

	private async Task WarnIfStillEmptyAsync(Room room, DateTimeOffset closesAt)
	{
		await using var scope = await room.EnterAsync();
		if (scope is null || room.Slots.Any(slot => slot.Player is not null)) return;

		Context.Events.Publish(new LobbyRoomClosingAnnounced(room, closesAt));
		Schedule(room, closesAt, () => CloseIfStillEmptyAsync(room));
	}

	private async Task CloseIfStillEmptyAsync(Room room)
	{
		await using var scope = await room.EnterAsync();
		if (scope is not null && !room.Slots.Any(slot => slot.Player is not null)) Close(room, null);
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
		room.JoinChannel(creator);
	}

	/// <summary>Releases a connection from room memberships when the server itself calls it; it needs no rights.</summary>
	/// <param name="connection">The connection to release.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public async Task ReleaseAsync(Connection connection, CancellationToken cancellationToken = default)
	{
		switch (connection)
		{
			case BanchoConnection player:
			{
				if (RoomOf(player) is { } room)
				{
					await using var scope = await room.EnterAsync(cancellationToken);
					if (scope is not null) room.Leave(player);
				}

				break;
			}
			case TourneyConnection observer:
			{
				foreach (var room in Rooms.Values.Where(room => room.Members.Observers.Contains(observer)).ToArray())
				{
					await using var scope = await room.EnterAsync(cancellationToken);
					if (scope is not null) room.ObserverLeave(observer);
				}

				break;
			}
		}

		// Managers stay in a room's channel after leaving their seat, and IRC referees join it directly.
		foreach (var room in Rooms.Values)
			room.Channel.ScopeTo(connection).Part();
	}

	/// <summary>Removes a user from rooms when the server itself calls it; it needs no rights.</summary>
	/// <param name="user">The user whose multiplayer access is suspended.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public async Task RemoveSuspendedAsync(User user, CancellationToken cancellationToken = default)
	{
		if (user.EffectivePermissions(Context.Clock.GetUtcNow()).Allows(Permissions.RoomJoin)) return;

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

			await using var scope = await room.EnterAsync(cancellationToken);
			if (scope is null || !room.Authority.MutableReferees.Remove(user)) continue;

			room.Context.Events.Publish(new RoomRefereeRemoved(room, by, user));
		}
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
			foreach (var round in (await Context.Rounds.ListAsync(match, cancellationToken)).Where(r =>
				         r.EndedAt is null))
			{
				round.EndedAt = now;
				round.Aborted = true;
				await Context.Rounds.CreateOrUpdateAsync(round, cancellationToken);
			}

			match.Value.EndedAt = now;
			await Context.Matches.UpdateAsync(match, cancellationToken);
			await Context.MatchEvents.RecordAsync(
				new MatchEvent(match, MatchEventType.Closed, now, Detail: "Server shutdown recovery"),
				cancellationToken);
		}

		return allMatches.Count;
	}
}