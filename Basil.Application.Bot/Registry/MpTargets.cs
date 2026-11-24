using System.Collections.Concurrent;
using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Application.Bot.Registry;

internal sealed class MpTargets
{
	private readonly ConcurrentDictionary<int, Room> _userIdToRoom = new();

	public void Set(int userId, Room room)
	{
		// A closed room can never be a target again, so a new target is the moment to forget the closed ones.
		foreach (var (id, target) in _userIdToRoom)
			if (target.IsClosed)
				_userIdToRoom.TryRemove(new KeyValuePair<int, Room>(id, target));

		_userIdToRoom[userId] = room;
	}

	public bool Clear(int userId)
	{
		return _userIdToRoom.TryRemove(userId, out _);
	}

	public Room? Get(int userId)
	{
		if (!_userIdToRoom.TryGetValue(userId, out var room)) return null;
		if (!room.IsClosed) return room;

		_userIdToRoom.TryRemove(new KeyValuePair<int, Room>(userId, room));
		return null;
	}
}