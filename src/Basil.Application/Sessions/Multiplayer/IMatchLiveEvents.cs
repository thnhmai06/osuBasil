namespace Basil.Application.Sessions.Multiplayer;

/// <summary>
///     Non-blocking pub/sub for the live event layer. Publishers, mostly packet handlers that
///     already hold <see cref="MatchSession.Lock" />, must never block on a slow or dead
///     subscriber: raising a plain C# event is itself non-blocking, and each subscriber writes what
///     it receives into its own bounded channel. The actual response write happens later, entirely
///     decoupled from whatever lock the publisher was holding.
/// </summary>
public interface IMatchLiveEvents
{
	/// <summary>
	///     Gets a value that indicates whether anything is currently subscribed to
	///     <see cref="PlayerScorePublished" />, letting a caller skip decoding and serializing a
	///     payload nobody will receive.
	/// </summary>
	bool HasPlayerScoreSubscribers { get; }

	/// <summary>
	///     Occurs when the match's general state channel should be updated. Raised with the match's
	///     database id and the payload bytes to broadcast.
	/// </summary>
	event Action<int, byte[]> MainPublished;

	/// <summary>
	///     Occurs when one player's live score channel should be updated. Raised with the match's
	///     database id, the player's name, and the payload bytes to broadcast.
	/// </summary>
	event Action<int, string, byte[]> PlayerScorePublished;

	/// <summary>
	///     Occurs when a match's settings channel should be updated. Raised with the match's
	///     database id and the payload bytes to broadcast.
	/// </summary>
	event Action<int, byte[]> SettingsPublished;

	/// <summary>
	///     Occurs when one slot's state channel (the "slot" sub-event) should be updated. Raised
	///     with the match's database id, the zero-based slot index, and the payload bytes to
	///     broadcast.
	/// </summary>
	event Action<int, int, byte[]> SlotPublished;

	/// <summary>
	///     Occurs when a match's host channel should be updated. Raised with the match's database id
	///     and the payload bytes to broadcast.
	/// </summary>
	event Action<int, byte[]> HostPublished;

	/// <summary>
	///     Occurs when a match's referee-list channel should be updated. Raised with the match's
	///     database id and the payload bytes to broadcast.
	/// </summary>
	event Action<int, byte[]> RefsPublished;

	/// <summary>
	///     Occurs when a match's ban-list channel should be updated. Raised with the match's
	///     database id and the payload bytes to broadcast.
	/// </summary>
	event Action<int, byte[]> BansPublished;

	/// <summary>
	///     Occurs when a match's countdown-timer channel should be updated. Raised with the match's
	///     database id and the payload bytes to broadcast.
	/// </summary>
	event Action<int, byte[]> TimerPublished;

	/// <summary>
	///     Occurs when a match's slots channel should be updated. Raised with the match's database
	///     id and the payload bytes to broadcast.
	/// </summary>
	event Action<int, byte[]> SlotsPublished;

	/// <summary>Raises <see cref="MainPublished" /> with the given match database id and payload.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishMain(int matchDbId, byte[] payload);

	/// <summary>Raises <see cref="PlayerScorePublished" /> with the given match database id, player name, and payload.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="playerName">The name of the player whose score channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishPlayer(int matchDbId, string playerName, byte[] payload);

	/// <summary>Raises <see cref="SettingsPublished" /> with the given match database id and payload.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishSettings(int matchDbId, byte[] payload);

	/// <summary>Raises <see cref="SlotPublished" /> with the given match database id, slot index, and payload.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="slotIndex">The zero-based index of the slot whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishSlot(int matchDbId, int slotIndex, byte[] payload);

	/// <summary>Raises <see cref="HostPublished" /> with the given match database id and payload.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishHost(int matchDbId, byte[] payload);

	/// <summary>Raises <see cref="RefsPublished" /> with the given match database id and payload.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishRefs(int matchDbId, byte[] payload);

	/// <summary>Raises <see cref="BansPublished" /> with the given match database id and payload.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishBans(int matchDbId, byte[] payload);

	/// <summary>Raises <see cref="TimerPublished" /> with the given match database id and payload.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishTimer(int matchDbId, byte[] payload);

	/// <summary>Raises <see cref="SlotsPublished" /> with the given match database id and payload.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishSlots(int matchDbId, byte[] payload);
}