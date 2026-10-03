using System.Collections.Concurrent;
using Basil.Application.Sessions;
using Basil.Domain.Utilities;

namespace Basil.Application.Multiplayer;

/// <summary>The open multiplayer rooms and the osu! clients watching the multiplayer lobby.</summary>
public sealed class Lobby
{
	private const int MaxRoomId = ushort.MaxValue;

	private readonly Lock _sync = new();
	private readonly ConcurrentDictionary<int, Room> _rooms = new();
	private readonly ConcurrentSet<BanchoConnection> _watchers = [];

	/// <summary>Gets every open room.</summary>
	public IEnumerable<Room> Rooms => _rooms.Values;

	/// <summary>Gets the osu! clients watching the multiplayer lobby.</summary>
	public IReadOnlySet<BanchoConnection> Watchers => _watchers;

	/// <summary>Gets a value that indicates whether every room id is taken, so no room can open.</summary>
	internal bool IsFull => _rooms.Count >= MaxRoomId;

	/// <summary>Finds an open room by id.</summary>
	public Room? Find(int id)
	{
		return _rooms.GetValueOrDefault(id);
	}

	/// <summary>Finds the room a player is seated in.</summary>
	public Room? RoomOf(BanchoConnection player)
	{
		// ponytail: scans every room; add a player index if room counts grow large.
		return _rooms.Values.FirstOrDefault(room => room.Slots.Find(player) is not null);
	}

	/// <summary>Creates a room under the lowest free room id and lists it as open.</summary>
	/// <param name="create">Creates the room for the assigned id.</param>
	/// <returns>The new room, or <see langword="null" /> when every room id is taken.</returns>
	/// <remarks>The caller holds the scope from <see cref="Enter" />.</remarks>
	internal Room? Add(Func<int, Room> create)
	{
		for (var id = 1; id <= MaxRoomId; id++)
		{
			if (_rooms.ContainsKey(id)) continue;

			var room = create(id);
			_rooms[id] = room;
			return room;
		}

		return null;
	}

	/// <summary>Removes a room from the open rooms.</summary>
	/// <param name="room">The room to remove.</param>
	/// <returns><see langword="true" /> if that room was open; <see langword="false" /> if it was not listed under its id.</returns>
	internal bool Remove(Room room)
	{
		return _rooms.TryRemove(new KeyValuePair<int, Room>(room.Id, room));
	}

	/// <summary>Adds an osu! client to the lobby watchers.</summary>
	/// <param name="watcher">The client that started watching.</param>
	/// <returns><see langword="true" /> if the client was added; <see langword="false" /> if it already was watching.</returns>
	internal bool AddWatcher(BanchoConnection watcher)
	{
		return _watchers.Add(watcher);
	}

	/// <summary>Removes an osu! client from the lobby watchers.</summary>
	/// <param name="watcher">The client that stopped watching.</param>
	/// <returns><see langword="true" /> if the client was removed; <see langword="false" /> if it was not watching.</returns>
	internal bool RemoveWatcher(BanchoConnection watcher)
	{
		return _watchers.Remove(watcher);
	}

	/// <summary>Enters the short scope in which a room id is assigned and a creator's open rooms are counted.</summary>
	/// <returns>The scope, to dispose when done.</returns>
	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}

	/// <summary>Enters a room's exclusive scope, in which one state transition of the room runs one at a time.</summary>
	/// <param name="room">The room to enter.</param>
	/// <param name="cancellationToken">A token that cancels the wait.</param>
	/// <returns>The scope, to dispose when done; or <see langword="null" /> when the room has closed.</returns>
	public async Task<IAsyncDisposable?> EnterAsync(Room room, CancellationToken cancellationToken = default)
	{
		await room.Gate.WaitAsync(cancellationToken);
		if (!room.IsClosed) return new Scope(room.Gate);
		room.Gate.Release();
		return null;
	}

	private sealed class Scope(SemaphoreSlim held) : IAsyncDisposable
	{
		private int _released;

		public ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0) held.Release();
			return ValueTask.CompletedTask;
		}
	}
}