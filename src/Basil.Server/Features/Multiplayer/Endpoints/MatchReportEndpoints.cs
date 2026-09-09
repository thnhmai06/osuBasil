using Basil.Server.Shared.Http.OpenApi;

namespace Basil.Server.Features.Multiplayer.Endpoints;

/// <summary>Registers the `/matches/{matchId}` match report route.</summary>
internal static class MatchReportEndpoints
{
	/// <summary>Registers the match report route on the `api.` host.</summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapMatchReport(this RouteGroupBuilder group)
	{
		group.MapGet("/matches/{matchId:numericid}", async (int matchId, MatchReportService reportService,
				CancellationToken cancellationToken) =>
			{
				var report = await reportService.BuildAsync(matchId, cancellationToken);
				return report is null ? Results.NotFound(new ErrorResponse("Match not found.")) : Results.Json(report);
			})
			.WithGroupName("basilapi")
			.WithName("getMatchReport")
			.WithSummary("Retrieve a match report.")
			.WithDescription("""
			                 Returns a complete match report, including its events, rounds, and per-round scores.
			                 If the match is still live, the report also includes its current room state, such as the host, referees, slots, beatmap, win condition, team mode, mods, and match status.
			                 For real-time updates, use `GET /matches/{matchId}/live`.

			                 Returns `404 Not Found` if the match does not exist.
			                 """)
			.WithTags("Match Report")
			.Produces<MatchReport>()
			.WithExample(StatusCodes.Status200OK, MatchSampleFixtures.SampleMatchReport())
			.ProducesProblem(StatusCodes.Status404NotFound);
	}
}
