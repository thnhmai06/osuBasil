namespace Basil.Application.Sessions.Spectating;

/// <summary>
///     The status-scoped sibling of <see cref="IPlayerInputEvents" />, feeding a userSession's live
///     status channel: online/offline transitions and in-game activity changes. Keyed by userSession id,
///     same as <see cref="IPlayerInputEvents" />.
/// </summary>
public interface IPlayerStatusEvents
{
	/// <summary>
	///     Gets a value that indicates whether anything is currently subscribed to
	///     <see cref="StatusPublished" />, letting a caller skip building and serializing a payload
	///     nobody will receive.
	/// </summary>
	bool HasSubscribers { get; }

	/// <summary>
	///     Occurs whenever a userSession's status changes: login, logout, or an in-game activity update.
	///     Raised with the userSession's id and the payload bytes to broadcast.
	/// </summary>
	event Action<int, byte[]> StatusPublished;

	/// <summary>Raises <see cref="StatusPublished" /> with the given userSession id and payload.</summary>
	/// <param name="playerId">The id of the userSession whose status changed.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishStatus(int playerId, byte[] payload);
}
