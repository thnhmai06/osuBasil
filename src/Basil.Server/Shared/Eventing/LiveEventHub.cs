using System.Collections.Concurrent;

namespace Basil.Server.Shared.Eventing;

/// <inheritdoc cref="ILiveEventHub" />
/// <remarks>
///     SSE is a broadcast: every subscriber to a stream receives the same immutable event. This
///     type is explicitly not responsible for, and holds no reference to, a repository, match
///     business logic, snapshot construction or a feature DTO — a caller decides whether to build a
///     payload at all, using <see cref="HasSubscribers" />, and hands the hub only opaque bytes. It
///     carries deltas, never a snapshot: a fresh subscriber gets its starting state from whatever the
///     feature's own state store is (see <see cref="StateStream{T}" />), not from this hub.
/// </remarks>
public sealed class LiveEventHub : ILiveEventHub
{
	private readonly ConcurrentDictionary<StreamKey, StreamState> _streams = new();

	/// <inheritdoc />
	public bool HasSubscribers(StreamKey key)
	{
		if (!_streams.TryGetValue(key, out var state)) return false;
		lock (state.Sync) return state.Subscribers.Count > 0;
	}

	/// <inheritdoc />
	public void Publish(StreamKey key, long version, ReadOnlyMemory<byte> payload)
	{
		var state = GetOrCreateState(key);
		LiveSubscription[] subscribers;
		lock (state.Sync) subscribers = [.. state.Subscribers];

		foreach (var subscriber in subscribers) subscriber.OnPublish(version, payload);
	}

	/// <inheritdoc />
	public LiveSubscription Open(StreamKey key)
	{
		var state = GetOrCreateState(key);
		lock (state.Sync)
		{
			LiveSubscription? subscription = null;
			// ReSharper disable once AccessToModifiedClosure -- assigned before the unsubscribe
			// callback can ever run (only Dispose invokes it, after this constructor returns).
			subscription = new LiveSubscription(() => Unsubscribe(state, subscription!));
			state.Subscribers.Add(subscription);
			return subscription;
		}
	}

	private StreamState GetOrCreateState(StreamKey key)
	{
		return _streams.GetOrAdd(key, static _ => new StreamState());
	}

	/// <inheritdoc />
	public void Forget(string category, int id)
	{
		foreach (var key in _streams.Keys)
			if (key.Category == category && key.Id == id)
				_streams.TryRemove(key, out _);
	}

	private static void Unsubscribe(StreamState state, LiveSubscription subscription)
	{
		lock (state.Sync) state.Subscribers.Remove(subscription);
	}

	/// <summary>Per-stream state: the lock that <see cref="Open" /> and <see cref="Publish" /> share, and the subscriber list.</summary>
	private sealed class StreamState
	{
		public readonly Lock Sync = new();
		public readonly List<LiveSubscription> Subscribers = [];
	}
}