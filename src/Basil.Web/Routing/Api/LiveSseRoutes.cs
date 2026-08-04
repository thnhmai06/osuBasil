using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Basil.Application.Formats;
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
///     events.
/// </remarks>
internal static class LiveSseRoutes
{
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
	///     Streams live match updates for the main match state.
	/// </summary>
	/// <remarks>
	///     Clients receive the current state first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleMain(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("main",
			publish =>
			{
				events.MainPublished += Handler;
				return () => events.MainPublished -= Handler;

				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}
			},
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live match settings updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current settings first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleSettings(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("settings",
			publish =>
			{
				events.SettingsPublished += Handler;
				return () => events.SettingsPublished -= Handler;

				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}
			},
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live host updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current host list first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleHost(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("hosts",
			publish =>
			{
				events.HostPublished += Handler;
				return () => events.HostPublished -= Handler;

				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}
			},
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live referee updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current referee list first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleRefs(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("refs",
			publish =>
			{
				events.RefsPublished += Handler;
				return () => events.RefsPublished -= Handler;

				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}
			},
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live player restriction updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current restrictions first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleBans(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("ban",
			publish =>
			{
				events.BansPublished += Handler;
				return () => events.BansPublished -= Handler;

				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}
			},
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live match timer updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current timer first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleTimer(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("timer",
			publish =>
			{
				events.TimerPublished += Handler;
				return () => events.TimerPublished -= Handler;

				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}
			},
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live slot updates.
	/// </summary>
	/// <remarks>
	///     Clients receive the current slot state first, followed by incremental updates.
	/// </remarks>
	public static IResult HandleSlots(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot("slots",
			publish =>
			{
				events.SlotsPublished += Handler;
				return () => events.SlotsPublished -= Handler;

				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}
			},
			readLatestSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams all live updates for a match slot.
	/// </summary>
	/// <remarks>
	///     The stream combines slot state, player score, and player input events into a
	///     single Server-Sent Events stream.
	/// </remarks>
	public static IResult HandleLiveSlot(HttpContext context, MatchSession match, int slotIndex,
		IMatchLiveEvents matchEvents, IPlayerInputEvents inputEvents, IUserSessionRegistry sessionRegistry,
		Func<byte[]?> readLatestSlotSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeMultiWithSnapshot(publish =>
			{
				matchEvents.SlotPublished += SlotHandler;
				matchEvents.PlayerScorePublished += ScoreHandler;
				inputEvents.InputPublished += InputHandler;
				return () =>
				{
					matchEvents.SlotPublished -= SlotHandler;
					matchEvents.PlayerScorePublished -= ScoreHandler;
					inputEvents.InputPublished -= InputHandler;
				};

				void SlotHandler(int id, int idx, byte[] payload)
				{
					if (id == match.DbId && idx == slotIndex) publish("slot", payload);
				}

				void ScoreHandler(int id, string playerName, byte[] payload)
				{
					if (id != match.DbId) return;
					var occupantName = match.Slots[slotIndex].PlayerId is { } occupantId
						? sessionRegistry.GetById(occupantId)?.Name
						: null;
					if (occupantName is not null && occupantName == playerName) publish("score", payload);
				}

				void InputHandler(int playerId, byte[] payload)
				{
					if (match.Slots[slotIndex].PlayerId == playerId) publish("input", payload);
				}
			},
			"slot", readLatestSlotSnapshot, cancellationToken));
	}

	/// <summary>
	///     Streams live spectator input for a player.
	/// </summary>
	public static IResult HandleInput(HttpContext context, int playerId, IPlayerInputEvents events,
		CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(Subscribe("input", publish =>
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
	///     Converts an event source into an SSE stream.
	/// </summary>
	private static IAsyncEnumerable<SseItem<string>> Subscribe(string eventType, Func<Action<byte[]>, Action> subscribe,
		CancellationToken cancellationToken)
	{
		var channel = Channel.CreateBounded<SseItem<string>>(
			new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest });
		var unsubscribe = subscribe(payload =>
			channel.Writer.TryWrite(new SseItem<string>(Encoding.UTF8.GetString(payload), eventType)));
		cancellationToken.Register(unsubscribe);

		return channel.Reader.ReadAllAsync(cancellationToken);
	}

	/// <summary>
	///     Creates an SSE stream that begins with the latest snapshot before forwarding
	///     the following updates.
	/// </summary>
	/// <remarks>
	///     The subscription is established before reading the snapshot so that no updates
	///     published during initialization are missed. Any queued updates older than the
	///     snapshot are discarded because they are already reflected in that snapshot.
	/// </remarks>
	private static async IAsyncEnumerable<SseItem<string>> SubscribeWithSnapshot(string eventType,
		Func<Action<byte[]>, Action> subscribe, Func<byte[]?> readLatestSnapshot,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var channel = Channel.CreateUnbounded<SseItem<string>>();
		var unsubscribe = subscribe(payload =>
			channel.Writer.TryWrite(new SseItem<string>(Encoding.UTF8.GetString(payload), eventType)));
		cancellationToken.Register(unsubscribe);

		while (channel.Reader.TryRead(out _))
		{
			// discard: already reflected in the fresh snapshot read below
		}

		if (readLatestSnapshot() is { } snapshotBytes)
			yield return new SseItem<string>(Encoding.UTF8.GetString(snapshotBytes), eventType);

		await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
			yield return item;
	}

	/// <summary>
	///     Creates an SSE stream backed by multiple event sources, beginning with an
	///     initial snapshot for one of them.
	/// </summary>
	private static async IAsyncEnumerable<SseItem<string>> SubscribeMultiWithSnapshot(
		Func<Action<string, byte[]>, Action> subscribe, string snapshotEventType, Func<byte[]?> readLatestSnapshot,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var channel = Channel.CreateUnbounded<SseItem<string>>();
		var unsubscribe = subscribe((eventType, payload) =>
			channel.Writer.TryWrite(new SseItem<string>(Encoding.UTF8.GetString(payload), eventType)));
		cancellationToken.Register(unsubscribe);

		while (channel.Reader.TryRead(out _))
		{
			// discard: already reflected in the fresh snapshot read below
		}

		if (readLatestSnapshot() is { } snapshotBytes)
			yield return new SseItem<string>(Encoding.UTF8.GetString(snapshotBytes), snapshotEventType);

		await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
			yield return item;
	}
}