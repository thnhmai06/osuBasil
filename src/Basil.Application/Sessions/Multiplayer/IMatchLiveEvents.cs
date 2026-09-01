namespace Basil.Application.Sessions.Multiplayer;

/// <summary>
///     Non-blocking pub/sub for the live event layer, keyed per match. Publishers, mostly packet
///     handlers that already hold <see cref="MatchSession.Lock" />, must never block on a slow or
///     dead subscriber; each subscriber receives the payload and writes it out on its own schedule,
///     decoupled from the publisher.
/// </summary>
/// <remarks>
///     Subscriptions are scoped to one match: publishing to match A only reaches match A's own
///     subscribers, never invoking handlers registered for other matches (ADR-004) — a publish's
///     cost is proportional to that match's subscriber count, not the server's total. Subscribe
///     methods return an <see cref="IDisposable" /> that deregisters the handler when disposed
///     (typically on client disconnect); <see cref="Forget" /> drops a match's bookkeeping entirely
///     once its <c>TeardownMatch</c> has completed every subscriber through
///     <see cref="Basil.Application.Services.SseSubscriberRegistry" />, so a match's subscriptions
///     never linger in memory past its lifetime.
/// </remarks>
public interface IMatchLiveEvents
{
	/// <summary>
	///     Gets a value that indicates whether a match currently has anything subscribed to its
	///     score channel, letting a caller skip decoding and serializing a payload nobody will
	///     receive.
	/// </summary>
	/// <param name="matchDbId">The persistent database id of the match to check.</param>
	bool HasPlayerScoreSubscribers(int matchDbId);

	/// <summary>Subscribes to a match's general state channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match to subscribe to.</param>
	/// <param name="handler">Invoked with the payload bytes to broadcast on every publish.</param>
	/// <returns>A handle that deregisters <paramref name="handler" /> when disposed.</returns>
	IDisposable SubscribeMain(int matchDbId, Action<byte[]> handler);

	/// <summary>Publishes a payload to a match's general state channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishMain(int matchDbId, byte[] payload);

	/// <summary>Subscribes to one player's live score channel within a match.</summary>
	/// <param name="matchDbId">The persistent database id of the match to subscribe to.</param>
	/// <param name="handler">Invoked with the player's name and the payload bytes on every publish.</param>
	/// <returns>A handle that deregisters <paramref name="handler" /> when disposed.</returns>
	IDisposable SubscribePlayerScore(int matchDbId, Action<string, byte[]> handler);

	/// <summary>Publishes a payload to one player's live score channel within a match.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="playerName">The name of the player whose score channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishPlayer(int matchDbId, string playerName, byte[] payload);

	/// <summary>Subscribes to a match's settings channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match to subscribe to.</param>
	/// <param name="handler">Invoked with the payload bytes to broadcast on every publish.</param>
	/// <returns>A handle that deregisters <paramref name="handler" /> when disposed.</returns>
	IDisposable SubscribeSettings(int matchDbId, Action<byte[]> handler);

	/// <summary>Publishes a payload to a match's settings channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishSettings(int matchDbId, byte[] payload);

	/// <summary>Subscribes to a match's per-slot "slot" sub-event channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match to subscribe to.</param>
	/// <param name="handler">Invoked with the zero-based slot index and payload bytes on every publish.</param>
	/// <returns>A handle that deregisters <paramref name="handler" /> when disposed.</returns>
	IDisposable SubscribeSlot(int matchDbId, Action<int, byte[]> handler);

	/// <summary>Publishes a payload to one slot's state channel within a match.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="slotIndex">The zero-based index of the slot whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishSlot(int matchDbId, int slotIndex, byte[] payload);

	/// <summary>Subscribes to a match's host channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match to subscribe to.</param>
	/// <param name="handler">Invoked with the payload bytes to broadcast on every publish.</param>
	/// <returns>A handle that deregisters <paramref name="handler" /> when disposed.</returns>
	IDisposable SubscribeHost(int matchDbId, Action<byte[]> handler);

	/// <summary>Publishes a payload to a match's host channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishHost(int matchDbId, byte[] payload);

	/// <summary>Subscribes to a match's referee-list channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match to subscribe to.</param>
	/// <param name="handler">Invoked with the payload bytes to broadcast on every publish.</param>
	/// <returns>A handle that deregisters <paramref name="handler" /> when disposed.</returns>
	IDisposable SubscribeRefs(int matchDbId, Action<byte[]> handler);

	/// <summary>Publishes a payload to a match's referee-list channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishRefs(int matchDbId, byte[] payload);

	/// <summary>Subscribes to a match's banlist channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match to subscribe to.</param>
	/// <param name="handler">Invoked with the payload bytes to broadcast on every publish.</param>
	/// <returns>A handle that deregisters <paramref name="handler" /> when disposed.</returns>
	IDisposable SubscribeBans(int matchDbId, Action<byte[]> handler);

	/// <summary>Publishes a payload to a match's banlist channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishBans(int matchDbId, byte[] payload);

	/// <summary>Subscribes to a match's countdown-timer channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match to subscribe to.</param>
	/// <param name="handler">Invoked with the payload bytes to broadcast on every publish.</param>
	/// <returns>A handle that deregisters <paramref name="handler" /> when disposed.</returns>
	IDisposable SubscribeTimer(int matchDbId, Action<byte[]> handler);

	/// <summary>Publishes a payload to a match's countdown-timer channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishTimer(int matchDbId, byte[] payload);

	/// <summary>Subscribes to a match's whole-arrangement slots channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match to subscribe to.</param>
	/// <param name="handler">Invoked with the payload bytes to broadcast on every publish.</param>
	/// <returns>A handle that deregisters <paramref name="handler" /> when disposed.</returns>
	IDisposable SubscribeSlots(int matchDbId, Action<byte[]> handler);

	/// <summary>Publishes a payload to a match's whole-arrangement slots channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose channel is being updated.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishSlots(int matchDbId, byte[] payload);

	/// <summary>Subscribes to a match's own chat channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match to subscribe to.</param>
	/// <param name="handler">Invoked with the payload bytes to broadcast on every publish.</param>
	/// <returns>A handle that deregisters <paramref name="handler" /> when disposed.</returns>
	IDisposable SubscribeChat(int matchDbId, Action<byte[]> handler);

	/// <summary>Publishes a chat line to a match's own chat channel.</summary>
	/// <param name="matchDbId">The persistent database id of the match whose chat carried the line.</param>
	/// <param name="payload">The UTF-8 JSON bytes to broadcast to the channel's subscribers.</param>
	void PublishChat(int matchDbId, byte[] payload);

	/// <summary>
	///     Drops every stream's bookkeeping for a match, called once its subscribers have all been
	///     completed (see <see cref="Basil.Application.Services.SseSubscriberRegistry" />) so a
	///     torn-down match's subscriptions never linger. Safe to call even if the match never had any
	///     subscriber.
	/// </summary>
	/// <param name="matchDbId">The persistent database id of the match to forget.</param>
	void Forget(int matchDbId);
}