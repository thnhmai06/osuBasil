using Basil.Server.Shared.Http;
using Basil.Server.Shared.Http.Middleware;
using Basil.Server.Shared.Http.OpenApi;
using Basil.Server.Features.Auth;

namespace Basil.Server.Features.Multiplayer.Endpoints;

/// <summary>Registers the `POST /matches/{matchId}/close` route.</summary>
internal static class MatchCloseEndpoints
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>Registers the match close route on the `api.` host.</summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchClose(this RouteGroupBuilder group)
	{
		group.MapPost("/matches/{matchId:numericid}/close", async (int matchId, HttpContext context,
				IMatchRegistry matchRegistry, MatchControlService matchControl,
				CancellationToken cancellationToken) =>
			{
				var match = matchRegistry.GetByDbId(matchId);
				if (match is null) return Results.NotFound(new ErrorResponse("Match not found."));

				await using (await match.BeginMutationAsync(cancellationToken))
				{
					var endedAt = DateTimeOffset.UtcNow;
					await matchControl.CloseAsync(null, null, match, cancellationToken);
					context.Items[EnvelopeMiddleware.EnvelopeMessageKey] = "Match closed.";
					return Results.Json(new MatchClosedView(matchId, endedAt));
				}
			})
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("closeMatch")
			.WithSummary("Close a match.")
			.WithDescription("""
			                 Closes the match and returns a confirmation body with its end time.

			                 Returns `404 Not Found` if the match isn't currently live.
			                 """ + AdminKeyNote)
			.WithTags("Match Close")
			.Produces<MatchClosedView>()
			.WithExample(StatusCodes.Status200OK, new MatchClosedView(42, DateTimeOffset.Parse("2026-07-20T14:30:00Z")))
			.ProducesProblem(StatusCodes.Status404NotFound);
	}
}
