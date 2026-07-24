using Basil.Application.Abstractions.Scores;
using Basil.Application.Services.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc;

namespace Basil.Web.Routing;

/// <summary>
///     `/scores` — public read surface for individual score rows, plus a rename of the old bare
///     `GET /replays/{scoreId}` download. No POST/PUT/DELETE: scores only ever come from the existing
///     in-game score-submission pipeline, never through this API.
/// </summary>
internal static class ScoreRoutes
{
    public static void MapScoreRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/scores", async ([FromQuery] int? page, [FromQuery] int? pageSize, IScoreRepository scores,
            CancellationToken cancellationToken) =>
        {
            var (p, ps) = Pagination.Normalize(page, pageSize);
            var overqueried = await scores.FetchPageAsync((p - 1) * ps, ps + 1, cancellationToken);
            return Results.Json(Pagination.Trim(overqueried, p, ps));
        })
            .WithGroupName("basilapi")
            .WithName("listScores")
            .WithSummary("List Scores")
            .WithDescription("Query params: `page` (default 1), `pageSize` (default 50). Response: " +
                "`{ page, pageSize, count, hasMore, items }`, each item the same full row shape as " +
                "`GET /scores/{scoreId}`. Public, no authentication.")
            .WithTags("Scores")
            .Produces<PagedResult<ScoreRow>>()
            .WithExample(StatusCodes.Status200OK, new PagedResult<ScoreRow>(1, 50, 1, false, [SampleScore()]));

        group.MapGet("/scores/{scoreId:long}", async (long scoreId, IScoreRepository scores,
            CancellationToken cancellationToken) =>
        {
            var score = await scores.FetchByIdAsync(scoreId, cancellationToken);
            return score is null ? Results.NotFound() : Results.Json(score);
        })
            .WithGroupName("basilapi")
            .WithName("getScore")
            .WithSummary("Get Score")
            .WithDescription("Returns the score's full row — mode-specific hit counts, combo, total score, " +
                "mods, and `isInvalidated`. An invalidated score is still returned (flagged, not hidden) — " +
                "invalidation marks a score whose beatmap changed or was removed after submission, it doesn't " +
                "erase the player's history. 404 if no score with this id exists. Public, no authentication.")
            .WithTags("Scores")
            .Produces<ScoreRow>()
            .WithExample(StatusCodes.Status200OK, SampleScore())
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/scores/{scoreId:long}/replay", async (long scoreId, ReplayService replayService,
            CancellationToken cancellationToken) =>
        {
            var result = await replayService.FetchReplayFileAsync(scoreId, cancellationToken);
            return result.Code == ReplayFetchResultCode.NotFound
                ? Results.NotFound()
                : Results.Bytes(result.Data!, "application/x-osu-replay");
        })
            .WithGroupName("basilapi")
            .WithName("downloadScoreReplay")
            .WithSummary("Download Score Replay")
            .WithDescription("Serves the `.osr` replay for the given score id. 404 if the score doesn't exist " +
                "or has no stored replay. Content-Type `application/x-osu-replay`. Public, no authentication — " +
                "unlike the osu! client's own `GET /web/osu-getreplay.php`, which requires client-style login " +
                "credentials.")
            .WithTags("Scores")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static ScoreRow SampleScore()
    {
        return new ScoreRow(999, 3, MatchTeam.Red, "d41d8cd98f00b204e9800998ecf8427e", 4_850_213, 98.42, 1234,
            Mods.HardRock | Mods.Hidden, 720, 45, 3, 2, 12, 5, "A", GameMode.Standard,
            DateTime.Parse("2026-07-20T12:34:56Z"), 185, ClientFlags.Clean, 9, false,
            "3f2504e04f8964dfa8807de37b2c73e1", DateTime.Parse("2026-07-20T12:38:01Z"));
    }
}
