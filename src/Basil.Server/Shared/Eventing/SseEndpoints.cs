using Basil.Server.Shared.Http.OpenApi;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Net.ServerSentEvents;
using Basil.Server.Shared.Http;

namespace Basil.Server.Shared.Eventing;

/// <summary>
///     Cross-cutting helpers for building Server-Sent Events (SSE) endpoints, shared by every slice
///     that streams live updates.
/// </summary>
/// <remarks>
///     Every match-scoped stream built on <see cref="Subscribe" />, <see cref="SubscribeWithSnapshot" />
///     or <see cref="SubscribeMultiWithSnapshot" /> registers its channel with the match's
///     <see cref="SseSubscriberRegistry" /> (ADR-004) via <see cref="RegisterWithMatch" /> so
///     <c>TeardownMatch</c> can end it the moment the match closes, instead of leaving it to the
///     client's own disconnect. A <see langword="null" /> registry (a stream that is not
///     match-scoped) skips that registration entirely.
/// </remarks>
internal static class SseEndpoints
{
	/// <summary>
	///     The SSE <c>retry:</c> hint sent with every event on every stream, telling a disconnected
	///     client how long to wait before reconnecting.
	/// </summary>
	internal static readonly TimeSpan ReconnectionInterval = TimeSpan.FromSeconds(5);

	/// <summary>
	///     Event types (ADR-004's "event-oriented" classification) that get an SSE <c>id:</c> on
	///     <see cref="SubscribeMultiWithSnapshot" />'s multiplexed streams. The coalescing state
	///     sub-events sharing those same streams (<c>main</c>, <c>slot</c>) are deliberately excluded:
	///     an id implies resumption is meaningful, and a fresh snapshot always supersedes anything a
	///     coalescing stream could have resumed from.
	/// </summary>
	private static readonly HashSet<string> EventOrientedSubEventTypes = ["gameplay", "input"];

	/// <summary>
	///     Determines whether a route template represents a live SSE endpoint.
	/// </summary>
	/// <param name="routePattern">The route template to examine.</param>
	/// <returns>
	///     <see langword="true" /> if the template contains a literal <c>live</c>
	///     path segment; otherwise <see langword="false" />.
	/// </returns>
	internal static bool IsSseRoute(string? routePattern)
	{
		return routePattern is not null &&
		       routePattern.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("live");
	}

	/// <summary>
	///     Configures the response for Server-Sent Events.
	/// </summary>
	internal static void SetSseHeaders(HttpContext context)
	{
		context.Response.Headers.CacheControl = "no-cache";
		context.Response.Headers["X-Accel-Buffering"] = "no";
	}

	/// <summary>
	///     Returns a standard SSE error response indicating that no live stream is available.
	/// </summary>
	internal static IResult NotLive(string message = "Match is not live")
	{
		return SseError(StatusCodes.Status409Conflict, message);
	}

	/// <summary>
	///     Returns an error response for an SSE endpoint.
	/// </summary>
	internal static IResult SseError(int statusCode, string message)
	{
		var envelope = new Envelope<object?>(false, statusCode, message, null, null, null, DateTimeOffset.UtcNow);
		return Results.Json(envelope, BasilJsonOptions.Instance, statusCode: statusCode);
	}

	/// <summary>
	///     Registers a completion callback with a match's subscriber registry, and returns the
	///     combined teardown action that both deregisters from the registry and runs the caller's own
	///     unsubscribe. A <see langword="null" /> registry (a stream that is not match-scoped) skips
	///     registration entirely.
	/// </summary>
	internal static Action RegisterWithMatch(SseSubscriberRegistry? registry, Action complete, Action unsubscribe)
	{
		var registration = registry?.Subscribe(complete);
		return () =>
		{
			registration?.Dispose();
			unsubscribe();
		};
	}

	/// <summary>
	///     Converts an event source into an SSE stream.
	/// </summary>
	/// <remarks>
	///     Every stream built on this helper is entirely event-oriented (ADR-004) -- unlike the
	///     multiplexed streams, there is no coalescing sub-event sharing the channel -- so every item
	///     gets a monotonic per-connection SSE <c>id:</c>.
	/// </remarks>
	internal static IAsyncEnumerable<SseItem<string>> Subscribe(string eventType, SseSubscriberRegistry? registry,
		Func<Action<byte[]>, Action> subscribe, CancellationToken cancellationToken)
	{
		var channel = Channel.CreateBounded<SseItem<string>>(
			new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest });
		var streamTag = new KeyValuePair<string, object?>("stream", eventType);
		var nextEventId = 0L;
		var unsubscribe = subscribe(payload =>
		{
			channel.Writer.TryWrite(new SseItem<string>(Encoding.UTF8.GetString(payload), eventType)
			{
				EventId = Interlocked.Increment(ref nextEventId).ToString(),
				ReconnectionInterval = ReconnectionInterval
			});
			EventingMetrics.SseBacklogDepth.Record(channel.Reader.Count, streamTag);
		});
		var teardown = RegisterWithMatch(registry, () => channel.Writer.TryComplete(), unsubscribe);
		EventingMetrics.SseActiveSubscribers.Add(1, streamTag);
		cancellationToken.Register(() =>
		{
			teardown();
			EventingMetrics.SseActiveSubscribers.Add(-1, streamTag);
		});

		return channel.Reader.ReadAllAsync(cancellationToken);
	}

	/// <summary>
	///     Creates an SSE stream, backed directly by an <see cref="ILiveEventHub" /> stream, that
	///     begins with the state's own latest snapshot before forwarding the deltas that follow it.
	/// </summary>
	/// <remarks>
	///     Subscribes to the hub before reading <paramref name="stream" />'s latest state and version,
	///     so no publish landing in between is missed; the version read alongside that state then
	///     fences the hub's own events (<see cref="LiveSubscription.EventsAfter" />), so nothing the
	///     snapshot already reflects is delivered again as if it were new. Every stream built on this
	///     helper is state-oriented (ADR-004): items get an SSE <c>retry:</c> hint but never an
	///     <c>id:</c> -- a fresh snapshot always supersedes anything resumption from an id could offer.
	/// </remarks>
	internal static async IAsyncEnumerable<SseItem<string>> SubscribeWithSnapshot<T>(string eventType,
		SseSubscriberRegistry registry, ILiveEventHub hub, StreamKey key, StateStream<T> stream,
		[EnumeratorCancellation] CancellationToken cancellationToken) where T : class
	{
		var subscription = hub.Open(key);
		var teardown = RegisterWithMatch(registry, subscription.Dispose, subscription.Dispose);
		var streamTag = new KeyValuePair<string, object?>("stream", eventType);
		EventingMetrics.SseActiveSubscribers.Add(1, streamTag);
		cancellationToken.Register(() =>
		{
			teardown();
			EventingMetrics.SseActiveSubscribers.Add(-1, streamTag);
		});

		var (latest, fence) = stream.GetLatestAndVersion();
		if (latest is not null)
			yield return new SseItem<string>(
				Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(latest, BasilJsonOptions.Instance)),
				eventType) { ReconnectionInterval = ReconnectionInterval };

		await foreach (var item in subscription.EventsAfter(fence, cancellationToken))
			yield return new SseItem<string>(Encoding.UTF8.GetString(item.Payload.Span), eventType)
				{ ReconnectionInterval = ReconnectionInterval };
	}

	/// <summary>
	///     Creates an SSE stream backed by multiple event sources, beginning with an initial snapshot
	///     for one of them.
	/// </summary>
	/// <remarks>
	///     Bounded with an explicit <c>gap</c> event on eviction (ADR-004): this stream carries
	///     high-frequency sub-events alongside a lower-frequency state sub-event (<c>main</c> or
	///     <c>slot</c>), so it must never grow without bound the way the plain snapshot channels can
	///     stay unbounded. Because every sub-event shares this one physical queue, an eviction is
	///     flagged generically (one <c>gap</c> event per drop) rather than attributed to whichever
	///     sub-event happened to be evicted -- a caller that needs to know exactly which kind of
	///     update was lost cannot, from this stream alone. The event-oriented sub-events
	///     (<see cref="EventOrientedSubEventTypes" />) each get a monotonic per-connection SSE
	///     <c>id:</c>; the state sub-event never does, for the same reason
	///     <see cref="SubscribeWithSnapshot" /> omits one. A <see langword="null" />
	///     <paramref name="registry" /> skips <see cref="SseSubscriberRegistry" /> registration
	///     entirely, same as <see cref="Subscribe" />.
	/// </remarks>
	internal static async IAsyncEnumerable<SseItem<string>> SubscribeMultiWithSnapshot(
		SseSubscriberRegistry? registry, Func<Action<string, byte[]>, Action> subscribe, string snapshotEventType,
		Func<byte[]?> readLatestSnapshot, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		const int capacity = 64;
		var channel = Channel.CreateBounded<SseItem<string>>(capacity);
		var streamTag = new KeyValuePair<string, object?>("stream", snapshotEventType);
		var nextEventId = 0L;
		var unsubscribe = subscribe((eventType, payload) =>
		{
			var eventId = EventOrientedSubEventTypes.Contains(eventType)
				? Interlocked.Increment(ref nextEventId).ToString()
				: null;
			BoundedSseChannel.WriteWithGapMarker(channel.Writer, channel.Reader, capacity, eventType,
				Encoding.UTF8.GetString(payload), eventId, ReconnectionInterval);
			EventingMetrics.SseBacklogDepth.Record(channel.Reader.Count, streamTag);
		});
		var teardown = RegisterWithMatch(registry, () => channel.Writer.TryComplete(), unsubscribe);
		EventingMetrics.SseActiveSubscribers.Add(1, streamTag);
		cancellationToken.Register(() =>
		{
			teardown();
			EventingMetrics.SseActiveSubscribers.Add(-1, streamTag);
		});

		while (channel.Reader.TryRead(out _))
		{
			// discard: already reflected in the fresh snapshot read below
		}

		if (readLatestSnapshot() is { } snapshotBytes)
			yield return new SseItem<string>(Encoding.UTF8.GetString(snapshotBytes), snapshotEventType)
				{ ReconnectionInterval = ReconnectionInterval };

		await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
			yield return item;
	}
}