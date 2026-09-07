using System.Text.Json;
using Basil.Server.Shared;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http;
using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Spectating;

/// <summary>
///     Handles the Server-Sent Events (SSE) endpoint for a player's live status and spectator input.
/// </summary>
internal static class PlayerLiveRoutes
{
	/// <summary>
	///     Streams a player's live status alongside their spectator input.
	/// </summary>
	/// <remarks>
	///     Not match-scoped (a spectated player may not even be in a match), so this stream is not
	///     registered with any <see cref="SseSubscriberRegistry" /> — its lifetime is the client's own
	///     connection, same as before ADR-004. Combines a coalescing state sub-event (<c>status</c>:
	///     online/offline and current activity, present from the first event and on every change) with
	///     an event-oriented one (<c>input</c>: raw replay frames, only while the player is playing),
	///     the same multiplexed shape the match-live streams use for their own state+event pairs.
	/// </remarks>
	public static IResult HandleInput(HttpContext context, int playerId, IPlayerInputEvents inputEvents,
		IPlayerStatusEvents statusEvents, ISessionRegistry<GameSession> gameRegistry,
		CancellationToken cancellationToken)
	{
		SseEndpoints.SetSseHeaders(context);
		return TypedResults.ServerSentEvents(SseEndpoints.SubscribeMultiWithSnapshot(null,
			publish =>
			{
				void InputHandler(int id, byte[] payload)
				{
					if (id == playerId) publish("input", payload);
				}

				void StatusHandler(int id, byte[] payload)
				{
					if (id == playerId) publish("status", payload);
				}

				inputEvents.InputPublished += InputHandler;
				statusEvents.StatusPublished += StatusHandler;
				return () =>
				{
					inputEvents.InputPublished -= InputHandler;
					statusEvents.StatusPublished -= StatusHandler;
				};
			},
			"status",
			() => JsonSerializer.SerializeToUtf8Bytes(
				PlayerStatusView.Build(gameRegistry.GetByUserId(playerId)), BasilJsonOptions.Instance),
			cancellationToken));
	}
}
