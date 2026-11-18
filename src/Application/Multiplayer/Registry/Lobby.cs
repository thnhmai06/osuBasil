using System.Collections.Concurrent;
using Basil.Application.Multiplayer.Capabilities;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Scores.Objects;
using Basil.Application.Shared.Paging;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Objects.Connections;

using Basil.Domain.Multiplayer;

using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;
using Basil.Shared.Structures.Data;

namespace Basil.Application.Multiplayer.Registry;

public sealed class Lobby : IRoomLobbyPort, IScoreBeatmapAcceptance
{
	private const int MaxRoomId = ushort.MaxValue;

	internal const int MaxRoomsPerCreator = 4;

	public static readonly TimeSpan EmptyRoomTimeout = TimeSpan.FromMinutes(15);

	public static readonly TimeSpan EmptyRoomWarningBefore = TimeSpan.FromMinutes(5);

	private readonly Dictionary<StableConnection, Room> _claims = [];

	private readonly HashSet<int> _reserved = [];
	private readonly ConcurrentDictionary<int, Room> _rooms = new();
	private readonly Lock _sync = new();
	private readonly ConcurrentSet<StableConnection> _watchers = [];

	internal Lobby(LobbyContext context)
	{
		Context = context;
	}

	internal LobbyContext Context { get; }

	public IReadOnlyDictionary<int, Room> Rooms => _rooms;

	public IReadOnlySet<StableConnection> Watchers => _watchers;

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

	bool IScoreBeatmapAcceptance.AcceptsUnknown(UserRecord player, Md5Hash beatmap)
	{
		return _rooms.Any(pair => pair.Value.AwaitsScore(player, beatmap));
	}

	public Room? Find(int id)
	{
		return _rooms.TryGetValue(id, out var room) && !room.IsClosed ? room : null;
	}

	public Room? RoomOf(StableConnection player)
	{
		// ponytail: scans every room; add a player index if room counts grow large.
		return _rooms.Select(pair => pair.Value).FirstOrDefault(room => room.Slots.Find(player) is not null);
	}

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

	internal bool TryClaim(StableConnection player, Room room)
	{
		using var scope = _sync.EnterScope();
		return _claims.TryAdd(player, room) || ReferenceEquals(_claims[player], room);
	}

	internal void Release(StableConnection player, Room room)
	{
		using var scope = _sync.EnterScope();
		if (_claims.TryGetValue(player, out var claimed) && ReferenceEquals(claimed, room)) _claims.Remove(player);
	}

	internal void Release(int id)
	{
		using var scope = _sync.EnterScope();
		_reserved.Remove(id);
	}

	internal void Add(Room room)
	{
		using var scope = _sync.EnterScope();
		_rooms[room.Id] = room;
		_reserved.Remove(room.Id);
	}

	internal bool Remove(Room room)
	{
		return _rooms.TryRemove(new KeyValuePair<int, Room>(room.Id, room));
	}

	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}

	public ScopedLobby ScopeTo(Connection actor)
	{
		return new ScopedLobby(this, actor);
	}

	public void Unwatch(StableConnection connection)
	{
		if (MutableWatchers.Remove(connection)) Context.Events.Publish(new LobbyWatcherLeft(connection));
	}

	internal static void Occupied(Room room)
	{
		RoomOccupied(room);
	}

	internal void Emptied(Room room)
	{
		Context.Events.Publish(new LobbyRoomClosingAnnounced(room, StartEmptyClock(room)));
	}

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

	internal DateTimeOffset StartEmptyClock(Room room)
	{
		var now = Context.Clock.GetUtcNow();
		room.EmptySince = now;
		room.ClosingWarned = false;
		return now + EmptyRoomTimeout;
	}

	public int CloseEmptyRooms()
	{
		var closed = 0;
		foreach (var room in _rooms.Select(pair => pair.Value).ToArray())
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

	internal bool TooManyRooms(UserRecord creator)
	{
		return _rooms.Count(pair => creator.Equals(pair.Value.Authority.Creator)) >= MaxRoomsPerCreator;
	}

	internal async Task<bool> IsRoomCreationLockedAsync(CancellationToken ct = default)
	{
		return (await Context.Settings.GetAsync(ct)).Locks.HasFlag(Locks.Rooms);
	}

	internal async Task<(Room? Room, RoomOutcome Outcome)> OpenRoomAsync(UserRecord creator, StableConnection? seat,
		bool seatsFromGame, string name, string password, bool isPrivate, MatchSettings? settings)
	{
		var now = Context.Clock.GetUtcNow();
		if (seatsFromGame && RoomOf(seat!) is not null) return (null, RoomOutcome.AlreadyInRoom);

		if (!MatchData.IsValidName(name)) return (null, RoomOutcome.InvalidSettings);

		// ponytail: two concurrent opens by one creator can both pass the room limit; count reservations per creator if that matters.
		if (Reserve() is not { } id) return (null, RoomOutcome.NoRoomId);

		var match = await CreateMatchAsync(id, name, creator, isPrivate, now);
		var room = Establish(id, match, settings, password, seat);

		var closesAt = !room.Slots.Any(slot => slot.Player is not null) ? StartEmptyClock(room) : (DateTimeOffset?)null;
		Context.Events.Publish(new LobbyRoomOpened(room, room.Authority.Host, closesAt));
		return (room, RoomOutcome.Ok);
	}

	private async Task<MatchRecord> CreateMatchAsync(int id, string name, UserRecord creator, bool isPrivate,
		DateTimeOffset now)
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
			});
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

	internal Room? OpenRoomOf(MatchRecord match)
	{
		return Rooms.Values.FirstOrDefault(room => !room.IsClosed && room.Match.Id == match.Id);
	}

	internal void Watch(StableConnection connection)
	{
		if (MutableWatchers.Add(connection)) Context.Events.Publish(new LobbyWatcherJoined(connection));
	}

	internal static void SeatCreator(Room room, StableConnection creator)
	{
		if (room.Seat(creator) is null) return;
		room.Authority.Host = creator;
	}

	public Task ReleaseAsync(Connection connection)
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

	public async Task<int> CloseUnfinishedAsync()
	{
		var allMatches = await Paging.ListAllAsync(page => Context.Matches.ListAsync(new MatchQuery(false), page));

		var now = Context.Clock.GetUtcNow();

		foreach (var match in allMatches)
		{
			var aborted = (await Context.Rounds.ListAsync(match)).Where(r => r.EndedAt is null)
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