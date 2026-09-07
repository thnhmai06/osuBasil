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
	public static IResult HandleMain(HttpContext context, MatchSession match, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeMultiWithSnapshot(match.SseSubscribers,
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
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("settings", match.SseSubscribers,
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
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("hosts", match.SseSubscribers,
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
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("refs", match.SseSubscribers,
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
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("ban", match.SseSubscribers,
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
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("timer", match.SseSubscribers,
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
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeWithSnapshot("slots", match.SseSubscribers,
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
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeMultiWithSnapshot(match.SseSubscribers,
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
	public static IResult HandleChat(HttpContext context, MatchSession match, IMatchLiveEvents events,
		CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.Subscribe("chat", match.SseSubscribers,
			BufferedPublish(publish => events.SubscribeChat(match.DbId, publish).Dispose, ChatFlushInterval,
				cancellationToken),
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
