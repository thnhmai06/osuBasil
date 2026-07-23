using Basil.Application.Abstractions.Scores;
using Basil.Application.Services.Scores;

namespace Basil.Web.Routing;

/// <summary>
///     `/score` — new public read surface for individual score rows, plus a rename of the old bare
///     `GET /replays/{scoreId}` download. No POST/PUT/DELETE: scores only ever come from the existing
///     in-game score-submission pipeline, never through this API.
/// </summary>
internal static class ScoreRoutes
{
    public static void MapScoreRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/score/{id:long}", async (long id, IScoreRepository scores,
            CancellationToken cancellationToken) =>
        {
            var score = await scores.FetchByIdAsync(id, cancellationToken);
            return score is null ? Results.NotFound() : Results.Json(score);
        })
            .WithGroupName("basilapi")
            .WithSummary("Get one score, by score id.")
            .WithDescription("Returns the score's full row — mode-specific hit counts, combo, total score, " +
                "mods, and `isInvalidated`. An invalidated score is still returned (flagged, not hidden) — " +
                "invalidation marks a score whose beatmap changed or was removed after submission, it doesn't " +
                "erase the player's history. 404 if no score with this id exists. Public, no authentication.")
            .WithTags("Scores");

        group.MapGet("/score/{id:long}/replay", async (long id, ReplayService replayService,
            CancellationToken cancellationToken) =>
        {
            var result = await replayService.FetchReplayFileAsync(id, cancellationToken);
            return result.Code == ReplayFetchResultCode.NotFound
                ? Results.NotFound()
                : Results.Bytes(result.Data!, "application/x-osu-replay");
        })
            .WithGroupName("basilapi")
            .WithSummary("Download a replay file, by score id.")
            .WithDescription("Serves the `.osr` replay for the given score id. 404 if the score doesn't exist " +
                "or has no stored replay. Content-Type `application/x-osu-replay`. Public, no authentication — " +
                "unlike the osu! client's own `GET /web/osu-getreplay.php`, which requires client-style login " +
                "credentials.")
            .WithTags("Scores");
    }
}
