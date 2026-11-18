using System.Collections.Concurrent;
using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Application.Bot.Registry;

internal sealed class MpTargets
{
	private readonly ConcurrentDictionary<int, Room> _userIdToRoom = new();

	public void Set(int userId, Room room)
	{
		_userIdToRoom[userId] = room;
	}

	public bool Clear(int userId)
	{
		return _userIdToRoom.TryRemove(userId, out _);
	}

	public Room? Get(int userId)
	{
		return _userIdToRoom.TryGetValue(userId, out var room) ? room : null;
	}
}