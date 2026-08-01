using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Basil.Application.Json;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Web.OpenApi;

namespace Basil.Web.Routing;

/// <summary>
///     ASP.NET Core's native SSE support (<c>TypedResults.ServerSentEvents</c>) for the api. host's
///     live TRT channels. These are server-to-client push only: no client message is ever expected,
///     so each connection just subscribes a C# event (<see cref="IMatchLiveEvents" />/
///     <see cref="IPlayerInputEvents" />) into its own <see cref="Channel{T}" /> and hands that
///     straight to the framework; <see cref="Channel{T}" />'s reader is already an
///     <see cref="IAsyncEnumerable{T}" />, so no hand-written iterator is needed for the raw-frame
///     channels. Publishing is a non-blocking event raise plus a non-blocking
///     <c>ChannelWriter.TryWrite</c>, so a slow or dead subscriber can never stall the publisher,
///     which is often still holding <c>MatchSession.Lock</c>. <c>EventType</c> tags each stream
///     ("main"/"playerScore"/"input") so a client can <c>EventSource.addEventListener</c> per feed;
///     <c>EventId</c>/resumption is deliberately not used since these feeds have no backlog to
///     resume from after a reconnect: a fresh full snapshot (see <see cref="SubscribeWithSnapshot" />)
///     takes its place for the channels that carry one.
/// </summary>
internal static class LiveSseRoutes
{
	/// <summary>
	///     The "main" channel now carries deltas (see <see cref="SnapshotChannel{T}" />/
	///     <see cref="MatchMembershipService.EnqueueStateAsync" />) instead of a full re-snapshot on every
	///     change: a fresh connection reads <see cref="MatchSession.MainSnapshot" /> directly for its
	///     first event, then this subscription forwards every delta published after that.
	/// </summary>
	public static IResult HandleMain(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot(cancellationToken, "main",
			publish =>
			{
				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}

				events.MainPublished += Handler;
				return () => events.MainPublished -= Handler;
			},
			readLatestSnapshot));
	}

	/// <summary>Same full-then-delta convention as <see cref="HandleMain" />, scoped to the settings field set.</summary>
	public static IResult HandleSettings(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot(cancellationToken, "settings",
			publish =>
			{
				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}

				events.SettingsPublished += Handler;
				return () => events.SettingsPublished -= Handler;
			},
			readLatestSnapshot));
	}

	/// <summary>Same full-then-delta convention as <see cref="HandleMain" />, scoped to `GET /matches/{matchId}/hosts`.</summary>
	public static IResult HandleHost(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot(cancellationToken, "hosts",
			publish =>
			{
				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}

				events.HostPublished += Handler;
				return () => events.HostPublished -= Handler;
			},
			readLatestSnapshot));
	}

	/// <summary>Same full-then-delta convention as <see cref="HandleMain" />, scoped to `GET /matches/{matchId}/refs`.</summary>
	public static IResult HandleRefs(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot(cancellationToken, "refs",
			publish =>
			{
				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}

				events.RefsPublished += Handler;
				return () => events.RefsPublished -= Handler;
			},
			readLatestSnapshot));
	}

	/// <summary>Same full-then-delta convention as <see cref="HandleMain" />, scoped to `GET /matches/{matchId}/ban`.</summary>
	public static IResult HandleBans(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot(cancellationToken, "ban",
			publish =>
			{
				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}

				events.BansPublished += Handler;
				return () => events.BansPublished -= Handler;
			},
			readLatestSnapshot));
	}

	/// <summary>Same full-then-delta convention as <see cref="HandleMain" />, scoped to `GET /matches/{matchId}/timer`.</summary>
	public static IResult HandleTimer(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot(cancellationToken, "timer",
			publish =>
			{
				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}

				events.TimerPublished += Handler;
				return () => events.TimerPublished -= Handler;
			},
			readLatestSnapshot));
	}

	/// <summary>Same full-then-delta convention as <see cref="HandleMain" />, scoped to `GET /matches/{matchId}/slots`.</summary>
	public static IResult HandleSlots(HttpContext context, int matchId, IMatchLiveEvents events,
		Func<byte[]?> readLatestSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeWithSnapshot(cancellationToken, "slots",
			publish =>
			{
				void Handler(int id, byte[] payload)
				{
					if (id == matchId) publish(payload);
				}

				events.SlotsPublished += Handler;
				return () => events.SlotsPublished -= Handler;
			},
			readLatestSnapshot));
	}

	/// <summary>
	///     Merges three feeds that are separate elsewhere into one stream, tagged by event name: the
	///     slot's own state (full-then-delta, "slot"), the current occupant's live score frames
	///     ("score", forwarded as-is), and their raw spectator input frames ("input", forwarded as-is).
	///     Score/input are matched against whoever currently occupies the slot at the moment each frame
	///     arrives (read fresh off <paramref name="match" /> per event); if the occupant changes, later
	///     frames simply start matching the new occupant instead, with no separate re-subscribe step.
	/// </summary>
	public static IResult HandleLiveSlot(HttpContext context, MatchSession match, int slotIndex,
		IMatchLiveEvents matchEvents, IPlayerInputEvents inputEvents, IPlayerSessionRegistry sessionRegistry,
		Func<byte[]?> readLatestSlotSnapshot, CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SubscribeMultiWithSnapshot(cancellationToken,
			publish =>
			{
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

				matchEvents.SlotPublished += SlotHandler;
				matchEvents.PlayerScorePublished += ScoreHandler;
				inputEvents.InputPublished += InputHandler;
				return () =>
				{
					matchEvents.SlotPublished -= SlotHandler;
					matchEvents.PlayerScorePublished -= ScoreHandler;
					inputEvents.InputPublished -= InputHandler;
				};
			},
			"slot", readLatestSlotSnapshot));
	}

	public static IResult HandleInput(HttpContext context, int playerId, IPlayerInputEvents events,
		CancellationToken cancellationToken)
	{
		SetSseHeaders(context);
		return TypedResults.ServerSentEvents(Subscribe(cancellationToken, "input", publish =>
		{
			void Handler(int id, byte[] payload)
			{
				if (id == playerId) publish(payload);
			}

			events.InputPublished += Handler;
			return () => events.InputPublished -= Handler;
		}));
	}

	/// <summary>Defeats reverse-proxy response buffering (nginx's X-Accel-Buffering) and any caching of a live stream.</summary>
	private static void SetSseHeaders(HttpContext context)
	{
		context.Response.Headers.CacheControl = "no-cache";
		context.Response.Headers["X-Accel-Buffering"] = "no";
	}

	/// <summary>
	///     Every `.../live` route is <see cref="Middleware.SseEndpointMarker" />-tagged, so
	///     <see cref="Middleware.EnvelopeMiddleware" /> unconditionally skips buffering it (buffering
	///     would silently turn a real live stream into one that never delivers a single event), a
	///     genuine synchronous JSON error returned before any stream opens has to envelope itself by
	///     hand instead. Used when the requested match/resource isn't currently live, so there's nothing
	///     to stream.
	/// </summary>
	public static IResult NotLive(string message = "Match is not live")
	{
		return SseError(StatusCodes.Status409Conflict, message);
	}

	/// <summary>
	///     Same hand-envelope need as <see cref="NotLive" />, for the other pre-stream error statuses an SSE route can
	///     return (e.g. 404 out-of-range slot, 400 no-stream-to-expose).
	/// </summary>
	public static IResult SseError(int statusCode, string message)
	{
		var envelope = new Envelope<object?>(false, statusCode, message, null, null, null, DateTimeOffset.UtcNow);
		return Results.Json(envelope, BasilJsonOptions.Instance, statusCode: statusCode);
	}

	private static IAsyncEnumerable<SseItem<string>> Subscribe(
		CancellationToken cancellationToken, string eventType, Func<Action<byte[]>, Action> subscribe)
	{
		var channel = Channel.CreateBounded<SseItem<string>>(
			new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest });
		var unsubscribe = subscribe(payload =>
			channel.Writer.TryWrite(new SseItem<string>(Encoding.UTF8.GetString(payload), eventType)));
		cancellationToken.Register(unsubscribe);

		return channel.Reader.ReadAllAsync(cancellationToken);
	}

	/// <summary>
	///     Lock-free full-then-delta subscribe sequence: (1) subscribe first so no publish in the gap
	///     is missed, (2) drain-and-discard anything already queued (non-blocking); safe to discard
	///     because the publisher always writes its <see cref="SnapshotChannel{T}" /> before raising the
	///     event, so anything sitting in the channel here is already reflected in the fresh read that
	///     follows, (3) read the latest full snapshot and yield it, (4) resume the normal blocking
	///     drain loop, forwarding every subsequent publish as a delta. Uses an unbounded channel
	///     (unlike <see cref="Subscribe" />'s bounded/drop-oldest one) because a dropped delta
	///     permanently desyncs a client: there's no full re-snapshot fallback once deltas start.
	/// </summary>
	private static async IAsyncEnumerable<SseItem<string>> SubscribeWithSnapshot(
		[EnumeratorCancellation] CancellationToken cancellationToken,
		string eventType, Func<Action<byte[]>, Action> subscribe, Func<byte[]?> readLatestSnapshot)
	{
		var channel = Channel.CreateUnbounded<SseItem<string>>();
		var unsubscribe = subscribe(payload =>
			channel.Writer.TryWrite(new SseItem<string>(Encoding.UTF8.GetString(payload), eventType)));
		cancellationToken.Register(unsubscribe);

		while (channel.Reader.TryRead(out _))
		{
			// discard — already reflected in the fresh snapshot read below
		}

		if (readLatestSnapshot() is { } snapshotBytes)
			yield return new SseItem<string>(Encoding.UTF8.GetString(snapshotBytes), eventType);

		await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
			yield return item;
	}

	/// <summary>
	///     Same subscribe-drain-read-resume sequence as <see cref="SubscribeWithSnapshot" />, but for a
	///     stream fed by more than one event source at once (each publish carrying its own event-type
	///     tag); only <paramref name="snapshotEventType" />'s source gets the initial full-state read.
	/// </summary>
	private static async IAsyncEnumerable<SseItem<string>> SubscribeMultiWithSnapshot(
		[EnumeratorCancellation] CancellationToken cancellationToken,
		Func<Action<string, byte[]>, Action> subscribe, string snapshotEventType, Func<byte[]?> readLatestSnapshot)
	{
		var channel = Channel.CreateUnbounded<SseItem<string>>();
		var unsubscribe = subscribe((eventType, payload) =>
			channel.Writer.TryWrite(new SseItem<string>(Encoding.UTF8.GetString(payload), eventType)));
		cancellationToken.Register(unsubscribe);

		while (channel.Reader.TryRead(out _))
		{
			// discard — already reflected in the fresh snapshot read below
		}

		if (readLatestSnapshot() is { } snapshotBytes)
			yield return new SseItem<string>(Encoding.UTF8.GetString(snapshotBytes), snapshotEventType);

		await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
			yield return item;
	}
}