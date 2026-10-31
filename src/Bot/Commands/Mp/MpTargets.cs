using System.Collections.Concurrent;
using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Bot.Commands.Mp;

/// <summary>
///     Per-user memory of which room a <c>!mp</c> command should target.
/// </summary>
/// <remarks>
///     Set by <c>!mp in</c> from a DM to the bot, cleared by <c>!mp in</c> without an id. The remembered room is the
///     one the user named; a room id is reused after a room closes, so the room itself is remembered and checked
///     against the open rooms before a command targets it.
/// </remarks>
internal sealed class MpTargets
{
	private readonly ConcurrentDictionary<int, Room> _userIdToRoom = new();

	/// <summary>Records the room this user is targeting.</summary>
	/// <param name="userId">The user's id.</param>
	/// <param name="room">The room to target.</param>
	public void Set(int userId, Room room)
	{
		_userIdToRoom[userId] = room;
	}

	/// <summary>Clears the stored room for this user.</summary>
	/// <param name="userId">The user's id.</param>
	/// <returns><see langword="true" /> when a room was stored for the user; otherwise, <see langword="false" />.</returns>
	public bool Clear(int userId)
	{
		return _userIdToRoom.TryRemove(userId, out _);
	}

	/// <summary>Gets the room this user is targeting.</summary>
	/// <param name="userId">The user's id.</param>
	/// <returns>The room, or <see langword="null" /> when none is stored.</returns>
	public Room? Get(int userId)
	{
		return _userIdToRoom.TryGetValue(userId, out var room) ? room : null;
	}
}