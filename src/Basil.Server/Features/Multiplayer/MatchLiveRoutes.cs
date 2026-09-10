using Basil.Server.Features.Spectating;
using Basil.Server.Shared.Sessions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http;

namespace Basil.Server.Features.Multiplayer;

/// <summary>
///     Handles the Server-Sent Events (SSE) endpoints for live match updates.
/// </summary>
/// <remarks>
///     These endpoints provide server-to-client event streams for real-time match updates. Some
///     streams begin with a snapshot of the current state, while others deliver only newly published
///     events.
/// </remarks>
internal static class MatchLiveRoutes
{
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
	public static IResult HandleMain(HttpContext context, MatchSession match, ILiveEventHub hub,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeMultiWithSnapshot(match.SseSubscribers,
			publish =>
			{
				var mainSubscription = hub.Open(MatchStreams.Main(match.DbId));
				// Every slot's score channel is subscribed unconditionally: which slots are actually
				// occupied changes over a match's lifetime, but the fixed 16-slot arrangement doesn't.
				var scoreSubscriptions = Enumerable.Range(0, 16)
					.Select(i => hub.Open(MatchStreams.Score(match.DbId, i))).ToArray();
				_ = ForwardAsync(mainSubscription, "main", publish);
				foreach (var scoreSubscription in scoreSubscriptions)
					_ = ForwardAsync(scoreSubscription, "gameplay", publish);
				return () =>
				{
					mainSubscription.Dispose();
					foreach (var scoreSubscription in scoreSubscriptions) scoreSubscription.Dispose();
				};
			},
			"main", readLatestSnapshot, cancellationToken));
	}

	/// <summary>Forwards every event on <paramref name="subscription" /> to <paramref name="publish" /> as <paramref name="eventType" />.</summary>
	private static async Task ForwardAsync(LiveSubscription subscription, string eventType,
		Action<string, byte[]> publish)
	{
		await foreach (var item in subscription.Events)
			publish(eventType, item.Payload.ToArray());
	}

	/// <summary>Forwards every event on <paramref name="subscription" /> to <paramref name="publish" />.</summary>
	private static async Task ForwardAsync(LiveSubscription subscription, Action<byte[]> publish)
	{
		await foreach (var item in subscription.Events)
			publish(item.Payload.ToArray());
	}

	/// <summary>
	///     Streams live match settings updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current settings first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleSettings(HttpContext context, MatchSession match, ILiveEventHub hub,
		CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("settings", match.SseSubscribers,
			hub, MatchStreams.Settings(match.DbId), match.SettingsSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live host updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current host list first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleHost(HttpContext context, MatchSession match, ILiveEventHub hub,
		CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("hosts", match.SseSubscribers,
			hub, MatchStreams.Host(match.DbId), match.HostSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live referee updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current referee list first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleRefs(HttpContext context, MatchSession match, ILiveEventHub hub,
		CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("refs", match.SseSubscribers,
			hub, MatchStreams.Refs(match.DbId), match.RefsSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live player restriction updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current restrictions first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleBans(HttpContext context, MatchSession match, ILiveEventHub hub,
		CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("ban", match.SseSubscribers,
			hub, MatchStreams.Bans(match.DbId), match.BansSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live match timer updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current timer first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleTimer(HttpContext context, MatchSession match, ILiveEventHub hub,
		CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("timer", match.SseSubscribers,
			hub, MatchStreams.Timer(match.DbId), match.TimerSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live slot updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current slot state first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleSlots(HttpContext context, MatchSession match, ILiveEventHub hub,
		CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("slots", match.SseSubscribers,
			hub, MatchStreams.Slots(match.DbId), match.SlotsSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams all live updates for a match slot.
	/// </summary>
	/// <remarks>
	///     The stream combines slot state, live gameplay, and player input events into a
	///     single Server-Sent Events stream.
	/// </remarks>
	public static IResult HandleLiveSlot(HttpContext context, MatchSession match, int slotIndex,
		ILiveEventHub hub, IPlayerInputEvents inputEvents, ISessionRegistry<GameSession> sessionRegistry,
		Func<byte[]?> readLatestSlotSnapshot, CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeMultiWithSnapshot(match.SseSubscribers,
			publish =>
			{
				var slotSubscription = hub.Open(MatchStreams.Slot(match.DbId, slotIndex));
				var scoreSubscription = hub.Open(MatchStreams.Score(match.DbId, slotIndex));
				_ = ForwardAsync(slotSubscription, "slot", publish);
				_ = ForwardAsync(scoreSubscription, "gameplay", publish);
				inputEvents.InputPublished += InputHandler;
				return () =>
				{
					slotSubscription.Dispose();
					scoreSubscription.Dispose();
					inputEvents.InputPublished -= InputHandler;
				};

				void InputHandler(int playerId, byte[] payload)
				{
					if (match.Slots[slotIndex].PlayerId == playerId) publish("input", payload);
				}
			},
			"slot", readLatestSlotSnapshot, cancellationToken));
	}

	/// <summary>
	///     How often buffered chat lines are flushed to each connection (Issue #4: "Buffer messages
	///     per connection and send them every second to reduce load").
	/// </summary>
	private static readonly TimeSpan ChatFlushInterval = TimeSpan.FromSeconds(1);

	/// <summary>
	///     Streams the chat said in a match's own channel.
	/// </summary>
	/// <remarks>
	///     Only lines said from the moment the stream opens are delivered; chat is not stored, so
	///     there is no earlier state to send first. Lines are buffered per connection and flushed as
	///     one combined <c>chat</c> event at most once per <see cref="ChatFlushInterval" /> -- every
	///     line is still delivered, in order, just batched to cut how many SSE writes a busy room's
	///     chat produces.
	/// </remarks>
	public static IResult HandleChat(HttpContext context, MatchSession match, ILiveEventHub hub,
		CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.Subscribe("chat", match.SseSubscribers,
			BufferedPublish(publish =>
			{
				var subscription = hub.Open(MatchStreams.Chat(match.DbId));
				_ = ForwardAsync(subscription, publish);
				return subscription.Dispose;
			}, ChatFlushInterval, cancellationToken),
			cancellationToken));
	}

	/// <summary>
	///     Wraps a subscribe function so that individual publishes are buffered and flushed as one
	///     combined JSON array at most once per <paramref name="flushInterval" />, instead of one SSE
	///     item per publish.
	/// </summary>
	/// <remarks>
	///     Every buffered payload is preserved in order -- nothing is dropped or coalesced to only the
	///     latest, unlike a state-oriented stream's snapshot; only the delivery cadence changes. A
	///     flush with nothing buffered emits nothing.
	/// </remarks>
	/// <param name="subscribe">The underlying event source to wrap.</param>
	/// <param name="flushInterval">How often to flush the buffer, if it holds anything.</param>
	/// <param name="cancellationToken">The connection's cancellation token; stops the flush loop when it fires.</param>
	private static Func<Action<byte[]>, Action> BufferedPublish(Func<Action<byte[]>, Action> subscribe,
		TimeSpan flushInterval, CancellationToken cancellationToken)
	{
		return publish =>
		{
			var buffer = new List<JsonNode?>();
			var bufferLock = new Lock();

			var unsubscribe = subscribe(payload =>
			{
				lock (bufferLock) buffer.Add(JsonNode.Parse(payload));
			});

			var timer = new PeriodicTimer(flushInterval);
			_ = Task.Run(async () =>
			{
				try
				{
					while (await timer.WaitForNextTickAsync(cancellationToken))
					{
						JsonNode?[] batch;
						lock (bufferLock)
						{
							if (buffer.Count == 0) continue;
							batch = [.. buffer];
							buffer.Clear();
						}

						publish(JsonSerializer.SerializeToUtf8Bytes(new JsonArray(batch), BasilJsonOptions.Instance));
					}
				}
				catch (OperationCanceledException)
				{
					// The connection closed; nothing left to flush to.
				}
			}, cancellationToken);

			return () =>
			{
				timer.Dispose();
				unsubscribe();
			};
		};
	}
}