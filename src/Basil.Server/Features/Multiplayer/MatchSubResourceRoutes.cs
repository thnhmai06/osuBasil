using Basil.Server.Shared.Eventing;
using System.Text.Json;
using Basil.Server.Shared.Http;
using Basil.Server.Features.Auth;
using Basil.Server.Shared.Http.Middleware;
using Basil.Server.Shared.Http.OpenApi;
using Basil.Server.Features.Multiplayer.Endpoints;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Server.Features.Multiplayer;

/// <summary>
///     Registers the REST endpoints for a match's hosts, referees, bans, slots, timer, abort, and
///     close actions.
/// </summary>
/// <remarks>
///     Each resource is publicly readable as plain JSON (404 if the match isn't currently live) or as
///     a server-sent-events stream on its `/live` sibling (409 if the match isn't currently live).
///     Every write requires administrator authorization.
/// </remarks>
internal static class MatchSubResourceRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/matches/{matchId}` host, referees, ban, slots, timer, abort, and close
	///     sub-routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchSubResourceRoutes(this RouteGroupBuilder group)
	{
		group.MapMatchHosts();
		group.MapMatchReferees();
		group.MapMatchBans();
		group.MapMatchSlots();
		MapTimer(group);
		MapAbort(group);
		group.MapMatchClose();
		group.MapMatchChat();
	}

	/// <summary>Registers the `/matches/{matchId}/timer` read, start, and abort routes.</summary>
	private static void MapTimer(RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:numericid}/timer", (int matchId, IMatchRegistry matchRegistry) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				return Results.Json(MatchLiveSnapshotBuilder.BuildTimer(match));
			})
			.WithGroupName("basilapi")
			.WithName("getMatchTimer")
			.WithSummary("Get match timer.")
			.WithDescription("""
			                 Returns the match's countdown timer as `{ running, secondsRemaining, autoStart }`.

			                 For a live stream of the same data, use `GET /matches/{matchId}/timer/live`.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """)
			.WithTags("Match Timer")
			.Produces<MatchTimerView>()
			.WithExample(StatusCodes.Status200OK,
				new MatchTimerView(true, 25, true, DateTimeOffset.Parse("2026-07-20T14:30:00Z"),
					DateTimeOffset.Parse("2026-07-20T14:30:30Z")))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/matches/{matchId:numericid}/timer/live", (int matchId, HttpContext context,
				IMatchRegistry matchRegistry,
				IMatchLiveEvents events, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return SseEndpoints.NotLive();

				return MatchLiveRoutes.HandleTimer(context, match, events,
					() => match.TimerSnapshot.Latest is { } snapshot
						? JsonSerializer.SerializeToUtf8Bytes(snapshot, BasilJsonOptions.Instance)
						: null,
					cancellationToken);
			})
			.WithGroupName("basilapi")
			.WithName("getMatchTimerLive")
			.WithSummary("Stream match timer.")
			.WithDescription("""
			                 Server-Sent Events stream of `GET /matches/{matchId}/timer`'s `running`, `autoStart`, `startedAt`, and `endsAt` fields. Unlike the REST response, this stream omits `secondsRemaining`; compute remaining time locally from `startedAt`/`endsAt` instead of polling a value that only goes stale between updates.

			                 A change is pushed at each announcement checkpoint `!mp timer`/`!mp start` uses, plus once more when the countdown finishes or is aborted.

			                 Returns `409 Conflict` if the match isn't currently live.
			                 """)
			.WithTags("Match Timer")
			.Produces<MatchTimerLiveView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK,
				new MatchTimerLiveView(true, true, DateTimeOffset.Parse("2026-07-20T14:30:00Z"),
					DateTimeOffset.Parse("2026-07-20T14:30:30Z")))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is not live"));

		group.MapPost("/matches/{matchId:numericid}/timer", async (int matchId, StartTimerRequest body,
				IMatchRegistry matchRegistry, MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					if (body.AutoStart)
					{
						var result = await matchControl.StartAsync(match, body.Seconds, mutation, cancellationToken);
						return result switch
						{
							MatchControlService.StartResult.AlreadyInProgress =>
								Results.Conflict(new ErrorResponse("Match is already in progress.")),
							MatchControlService.StartResult.BeatmapMissing =>
								Results.Conflict(new ErrorResponse(
									"Match cannot start because the beatmap does not exist on the server.")),
							_ => Results.Json(MatchLiveSnapshotBuilder.BuildTimer(match))
						};
					}

					matchControl.Timer(match, body.Seconds > 0 ? body.Seconds : 30, mutation);
					return Results.Json(MatchLiveSnapshotBuilder.BuildTimer(match));
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("startMatchTimer")
			.WithSummary("Start match timer.")
			.WithDescription("""
			                 Starts the match's countdown timer, from `{ seconds, autoStart }`.

			                 `autoStart: true` behaves like `!mp start [seconds]`: a positive `seconds` queues a countdown that starts the match when it finishes, while a non-positive value starts immediately. `autoStart: false` behaves like `!mp timer`: a countdown that never auto-starts (non-positive `seconds` defaults to 30).

			                 Returns `409 Conflict` if the match is already in progress or has no beatmap set, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Timer")
			.Produces<MatchTimerView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK,
				new MatchTimerView(true, 30, true, DateTimeOffset.Parse("2026-07-20T14:30:00Z"),
					DateTimeOffset.Parse("2026-07-20T14:30:30Z")))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is already in progress."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/matches/{matchId:numericid}/timer", async (int matchId, HttpContext context,
				IMatchRegistry matchRegistry, MatchControlService matchControl, CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					var result = matchControl.AbortTimer(match, mutation);
					if (result == MatchControlService.AbortTimerResult.NoTimerRunning)
						return Results.Conflict(new ErrorResponse("No countdown is running."));

					context.Items[EnvelopeMiddleware.EnvelopeMessageKey] = "Countdown aborted.";
					return Results.Json(MatchLiveSnapshotBuilder.BuildTimer(match));
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("abortMatchTimer")
			.WithSummary("Abort match timer.")
			.WithDescription("""
			                 Stops the running countdown.

			                 Returns `409 Conflict` if no countdown is running, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Timer")
			.Produces<MatchTimerView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK, new MatchTimerView(false, null, false))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("No countdown is running."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	/// <summary>Registers the `POST /matches/{matchId}/abort` route.</summary>
	private static void MapAbort(RouteGroupBuilder group)
	{
		group.MapPost("/matches/{matchId:numericid}/abort", async (int matchId, HttpContext context,
				IMatchRegistry matchRegistry, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					var abortedAt = DateTimeOffset.UtcNow;
					var result = await matchControl.AbortAsync(match, mutation, cancellationToken);
					if (result == MatchControlService.AbortResult.NotInProgress)
						return Results.Conflict(new ErrorResponse("Match is not in progress."));

					context.Items[EnvelopeMiddleware.EnvelopeMessageKey] = "Match aborted.";
					return Results.Json(new MatchAbortedView(matchId, abortedAt));
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("abortMatch")
			.WithSummary("Abort a match in progress.")
			.WithDescription("""
			                 Aborts the match's current round and returns a confirmation body with the abort time.

			                 Players in the match are notified over both the multiplayer protocol and the match's chat channel.

			                 Returns `409 Conflict` if the match is not in progress, or `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Abort")
			.Produces<MatchAbortedView>()
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status200OK,
				new MatchAbortedView(42, DateTimeOffset.Parse("2026-07-20T14:30:00Z")))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Match is not in progress."))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	/// <summary>Request body for `POST /matches/{matchId}/timer`.</summary>
	public sealed record StartTimerRequest(int Seconds, bool AutoStart);
}