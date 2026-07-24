using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Scores;
using Basil.Application.Sessions;
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
            IPlayerSessionRegistry sessionRegistry, IUserRepository users, IMapRepository maps,
            CancellationToken cancellationToken) =>
        {
            var (p, ps) = Pagination.Normalize(page, pageSize);
            var overqueried = await scores.FetchPageAsync((p - 1) * ps, ps + 1, cancellationToken);
            var trimmed = Pagination.Trim(overqueried, p, ps);
            var views = new List<ScoreDetailView>(trimmed.Items.Count);
            foreach (var row in trimmed.Items)
                views.Add(await BuildDetailView(row, sessionRegistry, users, maps, cancellationToken));
            return Results.Json(new PagedResult<ScoreDetailView>(trimmed.Page, trimmed.PageSize, trimmed.Count,
                trimmed.HasMore, views));
        })
            .WithGroupName("basilapi")
            .WithName("listScores")
            .WithSummary("List Scores")
            .WithDescription("Query params: `page` (default 1), `pageSize` (default 50). Response: " +
                "`{ page, pageSize, count, hasMore, items }`, each item the same shape as " +
                "`GET /scores/{scoreId}`. Public, no authentication.")
            .WithTags("Scores")
            .Produces<PagedResult<ScoreDetailView>>()
            .WithExample(StatusCodes.Status200OK, new PagedResult<ScoreDetailView>(1, 50, 1, false, [SampleScoreDetail()]));

        group.MapGet("/scores/{scoreId:long}", async (long scoreId, IScoreRepository scores,
            IPlayerSessionRegistry sessionRegistry, IUserRepository users, IMapRepository maps,
            CancellationToken cancellationToken) =>
        {
            var score = await scores.FetchByIdAsync(scoreId, cancellationToken);
            return score is null
                ? Results.NotFound()
                : Results.Json(await BuildDetailView(score, sessionRegistry, users, maps, cancellationToken));
        })
            .WithGroupName("basilapi")
            .WithName("getScore")
            .WithSummary("Get Score")
            .WithDescription("Returns the score's full row — mode-specific hit counts, combo, total score, " +
                "mods, the submitting `user` embed, and the played `beatmap` (null once its stored `mapMd5` " +
                "no longer resolves — content changed or the beatmap was removed since). 404 if no score with " +
                "this id exists. Public, no authentication.")
            .WithTags("Scores")
            .Produces<ScoreDetailView>()
            .WithExample(StatusCodes.Status200OK, SampleScoreDetail())
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

    private static async Task<ScoreDetailView> BuildDetailView(ScoreRow row, IPlayerSessionRegistry sessionRegistry,
        IUserRepository users, IMapRepository maps, CancellationToken cancellationToken)
    {
        var user = await MatchLiveSnapshotBuilder.ResolveOrPlaceholder(row.UserId, sessionRegistry, users, cancellationToken);
        var beatmap = await MatchLiveSnapshotBuilder.ResolveBeatmapAsync(row.MapMd5, maps, cancellationToken);

        return new ScoreDetailView(row.Id, row.RoundId, row.Team, row.MapMd5, beatmap, row.Score, row.Accuracy,
            row.MaxCombo, row.Mods, row.N300, row.N100, row.N50, row.NMiss, row.NGeki, row.NKatu, row.Grade,
            row.Mode, row.PlayTime, row.TimeElapsed, row.ClientFlags, user, row.Perfect, row.SubmittedAt);
    }

    /// <summary>
    ///     Wire shape for `GET /scores` and `GET /scores/{scoreId}` — reuses <see cref="ScoreRow" />'s
    ///     fields but replaces bare `userId` with the resolved `user` embed and adds `beatmap` (null once
    ///     `mapMd5` no longer resolves), replacing the old `isInvalidated` boolean with that same signal.
    /// </summary>
    public sealed record ScoreDetailView(
        long Id,
        int? RoundId,
        MatchTeam? Team,
        string MapMd5,
        Beatmap? Beatmap,
        long Score,
        double Accuracy,
        int MaxCombo,
        Mods Mods,
        int N300,
        int N100,
        int N50,
        int NMiss,
        int NGeki,
        int NKatu,
        string Grade,
        GameMode Mode,
        DateTime PlayTime,
        int TimeElapsed,
        ClientFlags ClientFlags,
        UserBrief User,
        bool Perfect,
        DateTime SubmittedAt);

    private static ScoreDetailView SampleScoreDetail()
    {
        var row = new ScoreRow(999, 3, MatchTeam.Red, "d41d8cd98f00b204e9800998ecf8427e", 4_850_213, 98.42, 1234,
            Mods.HardRock | Mods.Hidden, 720, 45, 3, 2, 12, 5, "A", GameMode.Standard,
            DateTime.Parse("2026-07-20T12:34:56Z"), 185, ClientFlags.Clean, 9, false,
            "3f2504e04f8964dfa8807de37b2c73e1", DateTime.Parse("2026-07-20T12:38:01Z"));

        return new ScoreDetailView(row.Id, row.RoundId, row.Team, row.MapMd5, null, row.Score, row.Accuracy,
            row.MaxCombo, row.Mods, row.N300, row.N100, row.N50, row.NMiss, row.NGeki, row.NKatu, row.Grade,
            row.Mode, row.PlayTime, row.TimeElapsed, row.ClientFlags, new UserBrief(9, "Carol", "us"), row.Perfect,
            row.SubmittedAt);
    }
}
