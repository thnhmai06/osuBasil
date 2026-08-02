namespace Basil.Application.Sessions.Spectating;

/// <summary>
///     The userSession-scoped sibling of <see cref="Multiplayer.IMatchLiveEvents" />, feeding a userSession's
///     live spectating event channel. Keyed by userSession id rather than match id: unlike the match-scoped
///     channels, input frames are published for a userSession whether they are currently in a
///     multiplayer match (see SpectateFramesHandler).
/// </summary>
public interface IPlayerInputEvents
{
	/// <summary>
	///     Gets a value that indicates whether anything is currently subscribed to
	///     <see cref="InputPublished" />, letting a caller skip decoding and serializing a payload
	///     nobody will receive.
	/// </summary>
	bool HasSubscribers { get; }

	/// <summary>
	///     Occurs whenever a spectated userSession's input frame is relayed. Rose with the userSession's id
	///     and the payload bytes to broadcast.
	/// </summary>
	event Action<int, byte[]> InputPublished;

	/// <summary>Raises <see cref="InputPublished" /> with the given userSession id and payload.</summary>
	/// <param name="playerId">The id of the userSession whose input frame was relayed.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishInput(int playerId, byte[] payload);
}