using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Basil.Server.Shared.Eventing;

/// <summary>An item delivered on a <see cref="LiveSubscription" />'s <see cref="LiveSubscription.Events" />.</summary>
/// <param name="Version">
///     This event's version. Versions on one stream are monotonically increasing but not
///     contiguous — a stream only emits when its own content changed, so gaps in the sequence are
///     normal and are not loss. Actual loss is signalled explicitly by a separate <c>gap</c> event,
///     never inferred from a version jump.
/// </param>
/// <param name="Payload">The opaque bytes published for this version.</param>
public readonly record struct LiveEvent(long Version, ReadOnlyMemory<byte> Payload);

/// <summary>
///     A single subscriber's handle on a <see cref="LiveEventHub" /> stream: the snapshot it opened
///     with, and every subsequent event that snapshot does not already contain.
/// </summary>
/// <remarks>
///     Disposing the subscription (synchronously or asynchronously) unregisters it from the hub and
///     completes <see cref="Events" />.
/// </remarks>
public sealed class LiveSubscription : IDisposable, IAsyncDisposable
{
	private readonly Channel<LiveEvent> _channel = Channel.CreateUnbounded<LiveEvent>();
	private readonly Lock _sync = new();
	private readonly Action _unsubscribe;
	private bool _hasSnapshot;
	private ReadOnlyMemory<byte>? _snapshot;
	private long _version;

	internal LiveSubscription(ReadOnlyMemory<byte>? snapshot, long version, bool isStale, Action unsubscribe)
	{
		_snapshot = snapshot;
		_version = version;
		IsStale = isStale;
		_hasSnapshot = !isStale;
		_unsubscribe = unsubscribe;
	}

	/// <summary>
	///     Gets the snapshot this subscription currently stands behind: the payload captured at
	///     <see cref="LiveEventHub.Open" />, replaced by whichever of a race between
	///     <see cref="SeedIfNotSuperseded" /> and a concurrent <see cref="LiveEventHub.Publish" />
	///     won while the subscription was still stale. <see langword="null" /> only when the stream
	///     has never been published to.
	/// </summary>
	public ReadOnlyMemory<byte>? Snapshot
	{
		get
		{
			lock (_sync) return _snapshot;
		}
	}

	/// <summary>Gets the version <see cref="Snapshot" /> reflects.</summary>
	public long Version
	{
		get
		{
			lock (_sync) return _version;
		}
	}

	/// <summary>
	///     Gets a value indicating whether the stream's state had moved on, via
	///     <see cref="LiveEventHub.MarkStale" />, past what <see cref="Snapshot" /> reflected when
	///     this subscription opened. A caller observing this should build a fresh snapshot and offer
	///     it through <see cref="SeedIfNotSuperseded" />.
	/// </summary>
	public bool IsStale { get; }

	/// <summary>
	///     Gets the events on this stream after <see cref="Snapshot" />, i.e. every publish carrying
	///     a version strictly greater than <see cref="Version" /> at the moment it arrived.
	/// </summary>
	public IAsyncEnumerable<LiveEvent> Events => _channel.Reader.ReadAllAsync();

	/// <summary>
	///     Gets this subscription's events strictly newer than <paramref name="fence" />, dropping
	///     anything at or below it.
	/// </summary>
	/// <remarks>
	///     A caller that opened this subscription before separately reading its own full state (e.g.
	///     from a <see cref="StateStream{T}" />) uses this to reconcile the two: subscribing first
	///     guarantees no publish in between is missed, and this filter guarantees a publish already
	///     reflected in that state is never delivered again as if it were new.
	/// </remarks>
	/// <param name="fence">The version already reflected elsewhere; only strictly greater versions are yielded.</param>
	/// <param name="cancellationToken">Stops enumeration when cancelled.</param>
	public async IAsyncEnumerable<LiveEvent> EventsAfter(long fence,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
			if (item.Version > fence)
				yield return item;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_unsubscribe();
		_channel.Writer.TryComplete();
	}

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}

	/// <summary>
	///     Offers a freshly built snapshot as this subscription's <see cref="Snapshot" />, unless a
	///     publish already landed past <paramref name="fence" /> while it was being built — in which
	///     case that publish is newer and authoritative, and this call is a no-op.
	/// </summary>
	/// <param name="snapshot">The freshly built snapshot, reflecting state as of <paramref name="fence" />.</param>
	/// <param name="fence">The version this subscription had captured before building <paramref name="snapshot" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="snapshot" /> was adopted; <see langword="false" />
	///     if a newer publish had already superseded it.
	/// </returns>
	public bool SeedIfNotSuperseded(ReadOnlyMemory<byte> snapshot, long fence)
	{
		lock (_sync)
		{
			if (_version > fence) return false;
			_snapshot = snapshot;
			_hasSnapshot = true;
			return true;
		}
	}

	/// <summary>
	///     Delivers a publish to this subscription. While no snapshot has been established yet (this
	///     subscription opened stale and has not been seeded), the publish itself becomes the
	///     snapshot instead of being queued as an event, since there is nothing meaningful to enqueue
	///     relative to a snapshot that does not exist yet. Once a snapshot exists, every later
	///     publish is queued to <see cref="Events" /> unconditionally — this stream's own ordering
	///     guarantee (never delivering an event the snapshot already contains) already holds because
	///     nothing is enqueued before a snapshot is established.
	/// </summary>
	internal void OnPublish(long version, ReadOnlyMemory<byte> payload)
	{
		lock (_sync)
		{
			if (!_hasSnapshot)
			{
				_snapshot = payload;
				_version = version;
				_hasSnapshot = true;
				return;
			}
		}

		_channel.Writer.TryWrite(new LiveEvent(version, payload));
	}
}