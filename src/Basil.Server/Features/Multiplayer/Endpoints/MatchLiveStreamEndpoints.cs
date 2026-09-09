using System.Text.Json;
using Basil.Server.Features.Spectating;
using Basil.Server.Shared.Eventing;
using Basil.Server.Shared.Http;
using Basil.Server.Shared.Http.OpenApi;
using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Multiplayer.Endpoints;

/// <summary>Registers the `/matches/{matchId}/live` full-state and per-slot stream routes.</summary>
internal static class MatchLiveStreamEndpoints
{
	/// <summary>Registers the match live-stream routes on the `api.` host.</summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchLiveStreams(this RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:numericid}/live", HandleMainLiveStream)
			.WithGroupName("basilapi")
			.WithName("getMatchLive")
			.WithSummary("Stream full match state.")
			.WithDescription("""
			                 Server-Sent Events stream of the match's full live state: room config, host, referees, current beatmap, in-progress flag, and every slot, interleaved with every player's live gameplay updates during a round.

			                 Each event is one of two types, carried by the SSE `event` field:

			                 - `main`: the room's full state first, then only the fields that changed
			                 - `gameplay`: one player's live score frames during a round

			                 For per-slot player input frames, use `GET /matches/{matchId}/live/{slotIndex}`. For a one-shot snapshot including historical rounds and events, use `GET /matches/{matchId}`.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """)
			.WithTags("Match Live")
			.Produces<MatchLiveSnapshot>()
			.Produces<PlayerLiveScore>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithMainLiveExamples(MatchSampleFixtures.SampleLiveSnapshot())
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is not live"));

		group.MapGet("/matches/{matchId:numericid}/live/{slotIndex:int}", HandleLiveSlotStream)
			.WithGroupName("basilapi")
			.WithName("getMatchSlotLive")
			.WithSummary("Stream one match slot.")
			.WithDescription("""
			                 Server-Sent Events stream for a single slot, `{slotIndex}` in 1-16 (matching `!mp move`'s convention).

			                 Each event is one of three types, carried by the SSE `event` field:

			                 - `slot`: the slot's occupancy, status, team, and mods (full first, then deltas)
			                 - `score`: the current occupant's live score frames during a round
			                 - `input`: the current occupant's raw spectator-input frames, the same shape as `GET /users/{userId}/live`

			                 The stream follows whoever currently occupies the slot; if the occupant changes, later `score`/`input` events match the new occupant automatically.

			                 Returns `404 Not Found` if the match isn't currently live or `slotIndex` is out of range.
			                 """)
			.WithTags("Match Live")
			.Produces<PlayerLiveScore>()
			.WithSlotLiveExamples()
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static IResult HandleMainLiveStream(int matchId, HttpContext context, IMatchRegistry matchRegistry,
		IMatchLiveEvents events, CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null) return SseEndpoints.NotLive();

		return MatchLiveRoutes.HandleMain(context, match, events,
			() => match.MainSnapshot.Latest is { } snapshot
				? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
				: null,
			cancellationToken);
	}

	private static IResult HandleLiveSlotStream(int matchId, int slotIndex, HttpContext context,
		IMatchRegistry matchRegistry, IMatchLiveEvents matchEvents, IPlayerInputEvents inputEvents,
		ISessionRegistry<GameSession> gameRegistry, CancellationToken cancellationToken)
	{
		var match = matchRegistry.GetByDbId(matchId);
		if (match is null || slotIndex is < 1 or > 16)
			return SseEndpoints.SseError(StatusCodes.Status404NotFound,
				"Match is not currently live, or slotIndex is out of range.");

		var index = slotIndex - 1;
		return MatchLiveRoutes.HandleLiveSlot(context, match, index, matchEvents, inputEvents, gameRegistry,
			() => match.SlotSnapshots[index].Latest is { } snapshot
				? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
				: null,
			cancellationToken);
	}
}