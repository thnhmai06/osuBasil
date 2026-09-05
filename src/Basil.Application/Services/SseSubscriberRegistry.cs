namespace Basil.Application.Services;

/// <summary>
///     Tracks a match's live SSE subscribers so <c>TeardownMatch</c> can end every one of them,
///     instead of leaving them to notice on their own (ADR-004).
/// </summary>
/// <remarks>
///     A bare concurrent collection is not enough here: a subscriber that registers concurrently
///     with teardown enumerating the collection can slip through unobserved, leaking exactly like
///     the defect this exists to fix. This type instead carries an explicit <c>Open</c>/<c>Closed</c>
///     lifecycle, with <see cref="Subscribe" /> and <see cref="CompleteAll" /> synchronized against
///     each other so the two can never interleave that way: a subscribe that loses the race to a
///     concurrent teardown observes <c>Closed</c> and completes immediately instead of registering.
/// </remarks>
public sealed class SseSubscriberRegistry
{
	private readonly Dictionary<Guid, Action> _subscribers = [];
	private readonly Lock _sync = new();
	private bool _closed;

	/// <summary>
	///     Registers a subscriber's completion callback, unless the match has already torn down.
	/// </summary>
	/// <remarks>
	///     If the registry is already closed, <paramref name="onComplete" /> runs immediately
	///     (synchronously, on the caller's thread) instead of being registered, so the caller
	///     observes end-of-stream right away rather than waiting on a match that is already gone.
	/// </remarks>
	/// <param name="onComplete">
	///     The callback that ends this subscriber's stream — typically a channel writer's
	///     <c>TryComplete()</c>.
	/// </param>
	/// <returns>
	///     A handle that deregisters the subscriber when disposed (e.g. on client disconnect), or
	///     <see langword="null" /> when the registry was already closed and nothing was registered.
	/// </returns>
	public IDisposable? Subscribe(Action onComplete)
	{
		lock (_sync)
		{
			if (_closed)
			{
				onComplete();
				return null;
			}

			var token = Guid.NewGuid();
			_subscribers[token] = onComplete;
			return new Subscription(this, token);
		}
	}

	/// <summary>
	///     Closes the registry and completes every currently-registered subscriber.
	/// </summary>
	/// <remarks>
	///     Transitions the registry to closed and atomically snapshots-then-clears the subscriber
	///     set under the same synchronization <see cref="Subscribe" /> uses, so no subscriber
	///     registered before this call is missed and no subscriber registered after it lingers.
	///     Each subscriber's completion runs after the synchronized section, not inside it, so this
	///     call's own cost stays proportional to an enqueue, not to subscriber count.
	/// </remarks>
	public void CompleteAll()
	{
		Action[] toComplete;
		lock (_sync)
		{
			_closed = true;
			toComplete = [.. _subscribers.Values];
			_subscribers.Clear();
		}

		foreach (var complete in toComplete) complete();
	}

	private void Unsubscribe(Guid token)
	{
		lock (_sync)
		{
			_subscribers.Remove(token);
		}
	}

	private sealed class Subscription(SseSubscriberRegistry registry, Guid token) : IDisposable
	{
		public void Dispose()
		{
			registry.Unsubscribe(token);
		}
	}
}
