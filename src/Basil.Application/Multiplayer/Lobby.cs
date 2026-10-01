using System.Collections.Concurrent;
using System.Threading.Channels;
using Basil.Domain.Client;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Domain.Utilities;
using Basil.Application.Common.Events;
using Basil.Application.Multiplayer.Events;
using Basil.Application.Sessions;

namespace Basil.Application.Multiplayer;

/// <summary>The open multiplayer rooms and the osu! clients watching the multiplayer lobby.</summary>
public sealed class Lobby(IMatchRepository matches, Presence presence, TimeProvider time) : IEventPublisher<LobbyEvent>
{
	/// <summary>The most tournament rooms one creator can have open.</summary>
	public const int MaxRoomsPerCreator = 4;

	/// <summary>How long an empty tournament room stays open.</summary>
	public static readonly TimeSpan EmptyTournamentRoomTimeout = TimeSpan.FromMinutes(15);

	/// <summary>How long before an empty tournament room closes the lobby announces its closing a second time.</summary>
	public static readonly TimeSpan EmptyRoomWarningBefore = TimeSpan.FromMinutes(5);

	private const int MaxRoomId = ushort.MaxValue;

	private readonly Channel<LobbyEvent> _events = Channel.CreateUnbounded<LobbyEvent>();
	private readonly ConcurrentDictionary<int, Room> _rooms = new();
	private readonly ConcurrentDictionary<Room, ITimer> _emptyTimers = new();
	private readonly ConcurrentSet<BanchoConnection> _watchers = [];
	private readonly Lock _openSync = new();

	/// <inheritdoc />
	public ChannelReader<LobbyEvent> Events => _events.Reader;

	/// <summary>Gets every open room.</summary>
	public IEnumerable<Room> Rooms => _rooms.Values;

	/// <summary>Gets the osu! clients watching the multiplayer lobby.</summary>
	public IReadOnlySet<BanchoConnection> Watchers => _watchers;

	/// <summary>Finds an open room by id.</summary>
	public Room? Find(int id) => _rooms.GetValueOrDefault(id);

	/// <summary>Finds the room a player is seated in.</summary>
	public Room? RoomOf(BanchoConnection player) =>
		// ponytail: scans every room; add a player index if room counts grow large.
		_rooms.Values.FirstOrDefault(room => room.Slots.Find(player) is not null);

	/// <summary>Opens a room for a new match.</summary>
	/// <param name="creator">The user creating the room, or <see langword="null" /> for an unattended room.</param>
	/// <param name="creatorConnection">The creator's game client, or <see langword="null" /> when they should not be seated.</param>
	/// <param name="name">The room's name.</param>
	/// <param name="password">The room's password, or an empty string for none.</param>
	/// <param name="isTournament">Whether the room is a tournament room.</param>
	/// <param name="isPrivate">Whether the room's history is private.</param>
	/// <param name="settings">The room's initial settings, or <see langword="null" /> for the defaults.</param>
	/// <param name="cancellationToken">A token that cancels the creation.</param>
	/// <returns>The opened room and the result of the operation.</returns>
	/// <remarks>
	///     A creator who is silenced or restricted cannot open a room, and a creator can have at most
	///     <see cref="MaxRoomsPerCreator" /> tournament rooms open. When <paramref name="creatorConnection" />
	///     is given and not seated in another room, it is seated as the first host. A room that is not a
	///     tournament room is opened in game: it needs the creator's game client and fails with AlreadyInRoom
	///     when that client already plays in a room. A tournament room opened with nobody seated closes after
	///     <see cref="EmptyTournamentRoomTimeout" /> unless someone joins. The server's bot joins the room's
	///     chat channel.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="creatorConnection" /> is <see langword="null" /> for a room that is not a tournament room.
	/// </exception>
	public async Task<(Room? Room, RoomResult Result)> OpenAsync(
		User? creator,
		BanchoConnection? creatorConnection,
		string name,
		string password,
		bool isTournament,
		bool isPrivate,
		MatchSettings? settings = null,
		CancellationToken cancellationToken = default)
	{
		if (!isTournament && creatorConnection is null)
			throw new ArgumentNullException(nameof(creatorConnection), "A room opened in game needs the creator's game client.");

		if (creator is not null)
		{
			if (creator.Value.SilenceEndsAt > time.GetUtcNow()) return (null, RoomResult.Silenced);
			if (!creator.Value.Privilege.Has(ClientPrivileges.Player)) return (null, RoomResult.NotAuthorized);
			if (isTournament && TooManyRooms(creator)) return (null, RoomResult.TooManyRooms);
		}

		// A room opened in game seats its creator; one who already plays in a room cannot open another.
		if (!isTournament && RoomOf(creatorConnection!) is not null) return (null, RoomResult.AlreadyInRoom);

		if (FreeRoomId() is null) return (null, RoomResult.NoRoomId);

		var match = await matches.AddAsync(new MatchData
		{
			Name = name,
			StartedAt = time.GetUtcNow(),
			EndedAt = null,
			Creator = creator,
			IsPrivate = isPrivate
		}, cancellationToken);

		lock (_openSync)
		{
			if (FreeRoomId() is not { } id) return (null, RoomResult.NoRoomId);
			if (creator is not null && isTournament && TooManyRooms(creator)) return (null, RoomResult.TooManyRooms);

			var room = new Room(this, time, id, match, settings ?? new MatchSettings(), isTournament);
			if (!string.IsNullOrEmpty(password))
				room.SetInitialPassword(password);

			_rooms[id] = room;

			if (creatorConnection is not null && RoomOf(creatorConnection) is null)
				room.SeatCreator(creatorConnection);

			if (presence.Sessions.Select(session => session.Bot).FirstOrDefault(b => b is not null) is { } bot)
				room.Channel.Join(bot);

			_events.Writer.TryWrite(new RoomOpened(room, room.Host));

			// A tournament room opened without a seated player starts its empty-room countdown now.
			if (!room.Slots.Any(slot => slot.Player is not null)) RoomEmptied(room);
			return (room, RoomResult.Ok);
		}
	}

	/// <summary>Closes a room.</summary>
	/// <param name="by">A connection whose user is the creator or a referee.</param>
	/// <param name="room">The room to close.</param>
	/// <param name="cancellationToken">A token that cancels the wait for the room's scope.</param>
	/// <returns>Ok, or NotAuthorized when the caller may not close the room.</returns>
	/// <remarks>Only the creator or a referee can close a room. Closing a closed room returns Ok and does nothing.</remarks>
	public async Task<RoomResult> CloseAsync(Connection by, Room room, CancellationToken cancellationToken = default)
	{
		if (!room.IsManager(by.User)) return RoomResult.NotAuthorized;

		await using var scope = await room.EnterAsync(cancellationToken);
		if (scope is null) return RoomResult.Ok;

		Close(room);
		return RoomResult.Ok;
	}

	/// <summary>Starts watching the multiplayer lobby.</summary>
	/// <param name="by">The osu! client that started watching.</param>
	public void Watch(BanchoConnection by)
	{
		if (_watchers.Add(by))
			_events.Writer.TryWrite(new LobbyWatcherJoined(by));
	}

	/// <summary>Stops watching the multiplayer lobby; doing it again does nothing.</summary>
	/// <param name="by">The osu! client that stopped watching.</param>
	public void Unwatch(BanchoConnection by)
	{
		if (_watchers.Remove(by))
			_events.Writer.TryWrite(new LobbyWatcherLeft(by));
	}

	/// <summary>Removes a connection from every room it plays in or observes and from the lobby watchers.</summary>
	/// <param name="connection">The connection to release.</param>
	/// <param name="cancellationToken">A token that cancels waits for room scopes.</param>
	/// <remarks>Called when the connection closes or its user is silenced; calling it again does nothing.</remarks>
	public async Task ReleaseAsync(Connection connection, CancellationToken cancellationToken = default)
	{
		if (connection is BanchoConnection player)
		{
			Unwatch(player);

			if (RoomOf(player) is { } room)
			{
				await using var scope = await room.EnterAsync(cancellationToken);
				if (scope is not null) room.Leave(player);
			}
		}
		else if (connection is TourneyConnection observer)
		{
			foreach (var room in _rooms.Values.Where(room => room.Observers.Contains(observer)).ToArray())
			{
				await using var scope = await room.EnterAsync(cancellationToken);
				if (scope is not null) room.ObserverLeave(observer);
			}
		}

		// Managers stay in a room's channel after leaving their seat, and IRC referees join it directly.
		foreach (var room in _rooms.Values)
			room.Channel.Part(connection);
	}

	/// <summary>
	///     Closes an empty room now; a tournament room is announced as closing at once and again
	///     <see cref="EmptyRoomWarningBefore" /> before it closes, and closes after
	///     <see cref="EmptyTournamentRoomTimeout" /> if still empty.
	/// </summary>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal void RoomEmptied(Room room)
	{
		if (!room.IsTournament)
		{
			Close(room);
			return;
		}

		var closesAt = time.GetUtcNow() + EmptyTournamentRoomTimeout;
		_events.Writer.TryWrite(new EmptyRoomClosingSoon(room, closesAt));
		Schedule(room, closesAt - EmptyRoomWarningBefore, () => WarnIfStillEmptyAsync(room, closesAt));
	}

	/// <summary>Stops the countdown that would close an empty tournament room.</summary>
	/// <remarks>The caller holds the room's scope.</remarks>
	internal void RoomOccupied(Room room)
	{
		if (_emptyTimers.TryRemove(room, out var timer)) timer.Dispose();
	}

	private void Schedule(Room room, DateTimeOffset at, Func<Task> action)
	{
		var delay = at - time.GetUtcNow();
		var timer = time.CreateTimer(_ => _ = action(), null, delay > TimeSpan.Zero ? delay : TimeSpan.Zero,
			Timeout.InfiniteTimeSpan);
		if (_emptyTimers.TryRemove(room, out var previous)) previous.Dispose();
		_emptyTimers[room] = timer;
	}

	private async Task WarnIfStillEmptyAsync(Room room, DateTimeOffset closesAt)
	{
		await using var scope = await room.EnterAsync();
		if (scope is null || room.Slots.Any(slot => slot.Player is not null)) return;

		_events.Writer.TryWrite(new EmptyRoomClosingSoon(room, closesAt));
		Schedule(room, closesAt, () => CloseIfStillEmptyAsync(room));
	}

	private async Task CloseIfStillEmptyAsync(Room room)
	{
		await using var scope = await room.EnterAsync();
		if (scope is not null && !room.Slots.Any(slot => slot.Player is not null)) Close(room);
	}

	private void Close(Room room)
	{
		RoomOccupied(room);
		var evicted = room.Close();
		_rooms.TryRemove(new KeyValuePair<int, Room>(room.Id, room));
		_events.Writer.TryWrite(new RoomClosed(room, evicted));
	}

	private bool TooManyRooms(User creator) =>
		_rooms.Values.Count(room => room.IsTournament && creator.Equals(room.Creator)) >= MaxRoomsPerCreator;

	private int? FreeRoomId()
	{
		for (var id = 1; id <= MaxRoomId; id++)
			if (!_rooms.ContainsKey(id))
				return id;
		return null;
	}
}