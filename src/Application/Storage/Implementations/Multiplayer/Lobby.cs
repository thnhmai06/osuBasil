using System.Collections.Concurrent;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Multiplayer.Room;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Utilities;

namespace Basil.Application.Storage.Implementations.Multiplayer;

/// <summary>The open multiplayer rooms and the osu! clients watching the multiplayer lobby.</summary>
internal sealed class Lobby : ILobby
{
	private const int MaxRoomId = ushort.MaxValue;
	private readonly ConcurrentDictionary<int, Room> _rooms = new();
	private readonly HashSet<int> _reserved = [];

	private readonly Lock _sync = new();
	private readonly ConcurrentSet<BanchoConnection> _watchers = [];

	/// <inheritdoc />
	public IEnumerable<Room> Rooms => _rooms.Values;

	/// <inheritdoc />
	public IReadOnlySet<BanchoConnection> Watchers => _watchers;

	/// <inheritdoc />
	bool ILobby.IsFull
	{
		get
		{
			using var scope = _sync.EnterScope();
			return _rooms.Count + _reserved.Count >= MaxRoomId;
		}
	}

	/// <inheritdoc />
	public Room? Find(int id)
	{
		return _rooms.GetValueOrDefault(id);
	}

	/// <inheritdoc />
	public Room? RoomOf(BanchoConnection player)
	{
		// ponytail: scans every room; add a player index if room counts grow large.
		return _rooms.Values.FirstOrDefault(room => room.Slots.Find(player) is not null);
	}

	/// <inheritdoc />
	int? ILobby.Reserve()
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

	/// <inheritdoc />
	void ILobby.Release(int id)
	{
		using var scope = _sync.EnterScope();
		_reserved.Remove(id);
	}

	/// <inheritdoc />
	void ILobby.Add(Room room)
	{
		using var scope = _sync.EnterScope();
		_rooms[room.Id] = room;
		_reserved.Remove(room.Id);
	}

	/// <inheritdoc />
	bool ILobby.Remove(Room room)
	{
		return _rooms.TryRemove(new KeyValuePair<int, Room>(room.Id, room));
	}

	/// <inheritdoc />
	bool ILobby.AddWatcher(BanchoConnection watcher)
	{
		return _watchers.Add(watcher);
	}

	/// <inheritdoc />
	bool ILobby.RemoveWatcher(BanchoConnection watcher)
	{
		return _watchers.Remove(watcher);
	}

	/// <inheritdoc />
	Lock.Scope ILobby.Enter()
	{
		return _sync.EnterScope();
	}
}