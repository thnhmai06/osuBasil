using System.Net.ServerSentEvents;
using System.Threading.Channels;

namespace Basil.Application.Services;

/// <summary>
///     Writes into a bounded Server-Sent Events item channel, evicting the oldest queued items and
///     emitting a single gap marker when full instead of growing without bound or silently dropping
///     data.
/// </summary>
/// <remarks>
///     <see cref="BoundedChannelFullMode.DropOldest" /> gives no signal that a drop happened, and a
///     gap must be visible to the client, not silent. When full, this frees room for both the gap
///     marker and the payload (2 slots) before writing either — freeing only 1 slot then writing
///     both would silently drop the payload write (<see cref="ChannelWriter{T}.TryWrite" /> fails on
///     a full channel), and re-checking the channel's depth after each write never converges, since a
///     write immediately re-fills the slot just freed.
/// </remarks>
public static class BoundedSseChannel
{
	/// <summary>Writes one event, evicting older queued events (and emitting one gap marker) if the channel is full.</summary>
	/// <param name="writer">The channel to write into.</param>
	/// <param name="reader">The same channel's reader, used to measure and evict backlog.</param>
	/// <param name="capacity">The channel's bound.</param>
	/// <param name="eventType">The SSE event type of the payload being written.</param>
	/// <param name="payload">The event data being written.</param>
	/// <param name="eventId">
	///     The SSE <c>id:</c> field for the payload event, or <see langword="null" /> to omit it (the
	///     gap marker never carries one). Callers assign an id only for event-oriented sub-events
	///     (ADR-004) — one whose individual items are each meaningful on their own, as opposed to a
	///     coalescing state sub-event resumption can't meaningfully apply to.
	/// </param>
	/// <param name="reconnectionInterval">The SSE <c>retry:</c> field applied to both items written.</param>
	public static void WriteWithGapMarker(ChannelWriter<SseItem<string>> writer, ChannelReader<SseItem<string>> reader,
		int capacity, string eventType, string payload, string? eventId = null,
		TimeSpan? reconnectionInterval = null)
	{
		var full = reader.Count >= capacity;
		if (full)
			while (reader.Count > capacity - 2 && reader.TryRead(out _))
			{
			}

		if (full)
			writer.TryWrite(new SseItem<string>("", "gap") { ReconnectionInterval = reconnectionInterval });
		writer.TryWrite(new SseItem<string>(payload, eventType)
		{
			EventId = eventId,
			ReconnectionInterval = reconnectionInterval
		});
	}
}