using System.Collections.Concurrent;

namespace Basil.Bot.Application.Commands.Mp;

/// <summary>
///     Per-user memory of which room a <c>!mp</c> command should target.
/// </summary>
/// <remarks>
///     Set by <c>!mp in</c> from a DM to the bot, cleared by <c>!mp in</c> without an id. The
///     dispatcher resolves a command's room as: <c>!mp in &lt;id&gt;</c> target, then the room of
///     the channel when sent in <c>#mp_&lt;id&gt;</c>, then the sender's own room, then none.
/// </remarks>
internal sealed class MpScopes
{
	private readonly ConcurrentDictionary<int, int> _userIdToRoomId = new();

	/// <summary>Records the room id this user is targeting.</summary>
	/// <param name="userId">The user's id.</param>
	/// <param name="roomId">The room id.</param>
	public void Set(int userId, int roomId)
	{
		_userIdToRoomId[userId] = roomId;
	}

	/// <summary>Clears the stored room for this user.</summary>
	/// <param name="userId">The user's id.</param>
	/// <returns><see langword="true" /> when a room was stored for the user; otherwise, <see langword="false" />.</returns>
	public bool Clear(int userId)
	{
		return _userIdToRoomId.TryRemove(userId, out _);
	}

	/// <summary>Gets the room this user is targeting.</summary>
	/// <param name="userId">The user's id.</param>
	/// <returns>The room id, or <see langword="null" /> when none is stored.</returns>
	public int? Get(int userId)
	{
		return _userIdToRoomId.TryGetValue(userId, out var roomId) ? roomId : null;
	}
}