using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Basil.Application.Diagnostics;
using Basil.Application.Formats;
using Basil.Application.Services;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Web.OpenApi;

namespace Basil.Web.Routing.Api;

/// <summary>
///     Registers the Server-Sent Events (SSE) endpoints for live multiplayer and spectating
///     updates.
/// </summary>
/// <remarks>
///     These endpoints provide server-to-client event streams for real-time updates. Some streams
///     begin with a snapshot of the current state, while others deliver only newly published
///     events. Every match-scoped stream registers its channel with the match's
///     <see cref="SseSubscriberRegistry" /> (ADR-004) so <c>TeardownMatch</c> can end it the moment
///     the match closes, instead of leaving it to the client's own disconnect. The per-player
///     <c>/spec/{playerId}/input</c> stream (<see cref="HandleInput" />) is not match-scoped and is
///     deliberately left out of that registry — its lifetime is the player's session, not any one
///     match's.
/// </remarks>
internal static class LiveSseRoutes
{
	/// <summary>
	///     The SSE <c>retry:</c> hint sent with every event on every stream, telling a disconnected
	///     client how long to wait before reconnecting.
	/// </summary>
	private static readonly TimeSpan ReconnectionInterval = TimeSpan.FromSeconds(5);

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
	///     Streams live match updates for the main match state, interleaved with every player's live
	///     gameplay updates during a round.
	/// </summary>
	/// <remarks>
	///     Clients receive the current state first (event <c>main</c>), followed by incremental state
	///     updates (also <c>main</c>) and, during a round, one <c>gameplay</c> event per player per
	///     score update. Bounded the same way the per-slot stream is (ADR-004): <c>gameplay</c> can be
	///     high-frequency, so the channel emits a <c>gap</c> marker on eviction rather than growing
	///     without bound.
	/// </remarks>
	public static IResult HandleMain(HttpContext context, MatchSession match, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeMultiWithSnapshot(match.SseSubscribers,
			publish =>
			{
				var unsubscribeMain = events.SubscribeMain(match.DbId, MainHandler);
				var unsubscribeGameplay = events.SubscribePlayerScore(match.DbId, GameplayHandler);
				return () =>
				{
					unsubscribeMain.Dispose();
					unsubscribeGameplay.Dispose();
				};

				void MainHandler(byte[] payload)
				{
					publish("main", payload);
				}

				void GameplayHandler(string playerName, byte[] payload)
				{
					publish("gameplay", payload);
				}
			},
			"main", readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live match settings updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current settings first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleSettings(HttpContext context, MatchSession match, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("settings", match.SseSubscribers,
			publish => events.SubscribeSettings(match.DbId, publish).Dispose,
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live host updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current host list first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleHost(HttpContext context, MatchSession match, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("hosts", match.SseSubscribers,
			publish => events.SubscribeHost(match.DbId, publish).Dispose,
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live referee updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current referee list first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleRefs(HttpContext context, MatchSession match, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("refs", match.SseSubscribers,
			publish => events.SubscribeRefs(match.DbId, publish).Dispose,
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live player restriction updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current restrictions first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleBans(HttpContext context, MatchSession match, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("ban", match.SseSubscribers,
			publish => events.SubscribeBans(match.DbId, publish).Dispose,
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live match timer updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current timer first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleTimer(HttpContext context, MatchSession match, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("timer", match.SseSubscribers,
			publish => events.SubscribeTimer(match.DbId, publish).Dispose,
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live slot updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current slot state first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleSlots(HttpContext context, MatchSession match, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("slots", match.SseSubscribers,
			publish => events.SubscribeSlots(match.DbId, publish).Dispose,
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams all live updates for a match slot.
	/// </summary>
	/// <remarks>
	///     The stream combines slot state, live gameplay, and player input events into a
	///     single Server-Sent Events stream.
	/// </remarks>
	public static IResult HandleLiveSlot(HttpContext context, MatchSession match, int slotIndex,
		IMatchLiveEvents matchEvents, IPlayerInputEvents inputEvents, ISessionRegistry<GameSession> sessionRegistry,
		Func<byte[]?> readLatestSlotSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeMultiWithSnapshot(match.SseSubscribers,
			publish =>
			{
				var unsubscribeSlot = matchEvents.SubscribeSlot(match.DbId, SlotHandler);
				var unsubscribeScore = matchEvents.SubscribePlayerScore(match.DbId, ScoreHandler);
				inputEvents.InputPublished += InputHandler;
				return () =>
				{
					unsubscribeSlot.Dispose();
					unsubscribeScore.Dispose();
					inputEvents.InputPublished -= InputHandler;
				};

				void SlotHandler(int idx, byte[] payload)
				{
					if (idx == slotIndex) publish("slot", payload);
				}

				void ScoreHandler(string playerName, byte[] payload)
				{
					var occupantName = match.Slots[slotIndex].PlayerId is { } occupantId
						? sessionRegistry.GetByUserId(occupantId)?.Name
						: null;
					if (occupantName is not null && occupantName == playerName) publish("gameplay", payload);
				}

				void InputHandler(int playerId, byte[] payload)
				{
					if (match.Slots[slotIndex].PlayerId == playerId) publish("input", payload);
				}
			},
			"slot", readLatestSlotSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams the chat said in a match's own channel.
	/// </summary>
	/// <remarks>
	///     Only lines said from the moment the stream opens are delivered; chat is not stored, so
	///     there is no earlier state to send first.
	/// </remarks>
	public static IResult HandleChat(HttpContext context, MatchSession match, IMatchLiveEvents events,
		CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(Subscribe("chat", match.SseSubscribers,
			publish => events.SubscribeChat(match.DbId, publish).Dispose, cancellationToken));
	}

	/// <summary>
	///     Streams live spectator input for a player.
	/// </summary>
	/// <remarks>
	///     Not match-scoped (a spectated player may not even be in a match), so this stream is not
	///     registered with any <see cref="SseSubscriberRegistry" /> — its lifetime is the client's own
	///     connection, same as before ADR-004.
	/// </remarks>
	public static IResult HandleInput(HttpContext context, int playerId, IPlayerInputEvents events,
		CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(Subscribe("input", registry: null, publish =>
		{
			events.InputPublished += Handler;
			return () => events.InputPublished -= Handler;

			void Handler(int id, byte[] payload)
			{
				if (id == playerId) publish(payload);
			}
		}, cancellationToken));
	}

	/// <summary>
	///     Configures the response for Server-Sent Events.
	/// </summary>
	private static void SetSseHeaders(HttpContext context)
	{
		context.Response.Headers.CacheControl = "no-cache";
		context.Response.Headers["X-Accel-Buffering"] = "no";
	}

	/// <summary>
	///     Returns a standard SSE error response indicating that no live stream is available.
	/// </summary>
	public static IResult NotLive(string message = "Match is not live")
	{
		return SseError(StatusCodes.Status409Conflict, message);
	}

	/// <summary>
	///     Returns an error response for an SSE endpoint.
	/// </summary>
	public static IResult SseError(int statusCode, string message)
	{
		var envelope = new Envelope<object?>(false, statusCode, message, null, null, null, DateTimeOffset.UtcNow);
		return Results.Json(envelope, BasilJsonOptions.Instance, statusCode: statusCode);
	}

	/// <summary>
	///     Registers a channel's completion with a match's subscriber registry, and returns the
	///     combined teardown action that both deregisters from the registry and runs the caller's own
	///     unsubscribe. A <see langword="null" /> registry (the per-player <c>input</c> stream) skips
	///     registration entirely.
	/// </summary>
	private static Action RegisterWithMatch(SseSubscriberRegistry? registry, ChannelWriter<SseItem<string>> writer,
		Action unsubscribe)
	{
		var registration = registry?.Subscribe(() => writer.TryComplete());
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
	///     Every stream built on this helper (<c>chat</c>, the standalone per-player <c>input</c>) is
	///     entirely event-oriented (ADR-004) -- unlike the multiplexed streams, there is no coalescing
	///     sub-event sharing the channel -- so every item gets a monotonic per-connection SSE
	///     <c>id:</c>.
	/// </remarks>
	private static IAsyncEnumerable<SseItem<string>> Subscribe(string eventType, SseSubscriberRegistry? registry,
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
			BasilMetrics.SseBacklogDepth.Record(channel.Reader.Count, streamTag);
		});
		var teardown = RegisterWithMatch(registry, channel.Writer, unsubscribe);
		BasilMetrics.SseActiveSubscribers.Add(1, streamTag);
		cancellationToken.Register(() =>
		{
			teardown();
			BasilMetrics.SseActiveSubscribers.Add(-1, streamTag);
		});

		return channel.Reader.ReadAllAsync(cancellationToken);
	}

	/// <summary>
	///     Creates an SSE stream that begins with the latest snapshot before forwarding
	///     the following updates.
	/// </summary>
	/// <remarks>
	///     The subscription is established before reading the snapshot so that no updates
	///     published during initialization are missed. Any queued updates older than the
	///     snapshot are discarded because they are already reflected in that snapshot. Every stream
	///     built on this helper is state-oriented (ADR-004): items get an SSE <c>retry:</c> hint but
	///     never an <c>id:</c> -- a fresh snapshot always supersedes anything resumption from an id
	///     could offer.
	/// </remarks>
	private static async IAsyncEnumerable<SseItem<string>> SubscribeWithSnapshot(string eventType,
		SseSubscriberRegistry registry, Func<Action<byte[]>, Action> subscribe, Func<byte[]?> readLatestSnapshot,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var channel = Channel.CreateUnbounded<SseItem<string>>();
		var streamTag = new KeyValuePair<string, object?>("stream", eventType);
		var unsubscribe = subscribe(payload =>
		{
			channel.Writer.TryWrite(new SseItem<string>(Encoding.UTF8.GetString(payload), eventType)
				{ ReconnectionInterval = ReconnectionInterval });
			BasilMetrics.SseBacklogDepth.Record(channel.Reader.Count, streamTag);
		});
		var teardown = RegisterWithMatch(registry, channel.Writer, unsubscribe);
		BasilMetrics.SseActiveSubscribers.Add(1, streamTag);
		cancellationToken.Register(() =>
		{
			teardown();
			BasilMetrics.SseActiveSubscribers.Add(-1, streamTag);
		});

		while (channel.Reader.TryRead(out _))
		{
			// discard: already reflected in the fresh snapshot read below
		}

		if (readLatestSnapshot() is { } snapshotBytes)
			yield return new SseItem<string>(Encoding.UTF8.GetString(snapshotBytes), eventType)
				{ ReconnectionInterval = ReconnectionInterval };

		await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
			yield return item;
	}

	/// <summary>
	///     Creates an SSE stream backed by multiple event sources, beginning with an initial snapshot
	///     for one of them.
	/// </summary>
	/// <remarks>
	///     Bounded with an explicit <c>gap</c> event on eviction (ADR-004): this stream carries the
	///     high-frequency <c>gameplay</c>/<c>input</c> sub-events alongside the lower-frequency
	///     state sub-event (<c>main</c> or <c>slot</c>), so it must never grow without bound the way
	///     the plain snapshot channels can stay unbounded. Because every sub-event shares this one
	///     physical queue, an eviction is flagged generically (one <c>gap</c> event per drop) rather
	///     than attributed to whichever sub-event happened to be evicted — a caller that needs to know
	///     exactly which kind of update was lost cannot, from this stream alone. The event-oriented
	///     sub-events (<see cref="EventOrientedSubEventTypes" />) each get a monotonic per-connection
	///     SSE <c>id:</c>; the state sub-event never does, for the same reason
	///     <see cref="SubscribeWithSnapshot" /> omits one.
	/// </remarks>
	private static async IAsyncEnumerable<SseItem<string>> SubscribeMultiWithSnapshot(
		SseSubscriberRegistry registry, Func<Action<string, byte[]>, Action> subscribe, string snapshotEventType,
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
			BasilMetrics.SseBacklogDepth.Record(channel.Reader.Count, streamTag);
		});
		var teardown = RegisterWithMatch(registry, channel.Writer, unsubscribe);
		BasilMetrics.SseActiveSubscribers.Add(1, streamTag);
		cancellationToken.Register(() =>
		{
			teardown();
			BasilMetrics.SseActiveSubscribers.Add(-1, streamTag);
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