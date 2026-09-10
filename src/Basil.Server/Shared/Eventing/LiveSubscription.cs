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
///     A single subscriber's handle on a <see cref="LiveEventHub" /> stream: every event published on
///     it from the moment it was opened.
/// </summary>
/// <remarks>
///     Disposing the subscription (synchronously or asynchronously) unregisters it from the hub and
///     completes <see cref="Events" />. Carries no starting state of its own — a caller that needs
///     one reads it separately (typically from a <see cref="StateStream{T}" />) and uses
///     <see cref="EventsAfter" /> to reconcile the two.
/// </remarks>
public sealed class LiveSubscription : IDisposable, IAsyncDisposable
{
	private readonly Channel<LiveEvent> _channel = Channel.CreateUnbounded<LiveEvent>();
	private readonly Action _unsubscribe;

	internal LiveSubscription(Action unsubscribe)
	{
		_unsubscribe = unsubscribe;
	}

	/// <summary>Gets every event published on this stream since this subscription was opened.</summary>
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

	/// <summary>Delivers a publish to this subscription.</summary>
	internal void OnPublish(long version, ReadOnlyMemory<byte> payload)
	{
		_channel.Writer.TryWrite(new LiveEvent(version, payload));
	}
}