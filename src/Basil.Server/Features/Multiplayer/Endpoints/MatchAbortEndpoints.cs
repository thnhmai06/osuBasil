using Basil.Server.Features.Auth;
using Basil.Server.Features.Multiplayer.Handlers.Lifecycle;
using Basil.Server.Shared.Http;
using Basil.Server.Shared.Http.Middleware;
using Basil.Server.Shared.Http.OpenApi;

namespace Basil.Server.Features.Multiplayer.Endpoints;

/// <summary>Registers the `POST /matches/{matchId}/abort` route.</summary>
internal static class MatchAbortEndpoints
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>Registers the match abort route on the `api.` host.</summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchAbort(this RouteGroupBuilder group)
	{
		group.MapPost("/matches/{matchId:numericid}/abort", async (int matchId, HttpContext context,
				IMatchRegistry matchRegistry, AbortHandler abortHandler,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				await using (var mutation = await match.BeginMutationAsync(cancellationToken))
				{
					var abortedAt = DateTimeOffset.UtcNow;
					var result = await abortHandler.AbortAsync(match, mutation, cancellationToken);
					if (result == AbortHandler.AbortResult.NotInProgress)
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
}