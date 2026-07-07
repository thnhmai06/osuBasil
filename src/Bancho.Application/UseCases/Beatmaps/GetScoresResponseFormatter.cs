using Bancho.Domain.Beatmaps;

namespace Bancho.Application.UseCases.Beatmaps;

/// <summary>
///     Ported from app/api/domains/osu.py's format_scores_response/format_score_listing
///     (SCORE_LISTING_FMTSTR) for the GET /web/osu-osz2-getscores.php response body. No golden
///     fixture exists in the Python repo for this endpoint (0 hits in tests/unit or
///     tests/integration) — every expected string here is hand-built from the documented format
///     string, not captured from a running bancho.py. Byte-exactness against a real osu! client is
///     still unverified, same caveat as Phase 3/4's untested wire-format assumptions.
/// </summary>
public static class GetScoresResponseFormatter
{
    public const string NotSubmitted = "-1|false";

    public const string NeedsUpdate = "1|false";

    public static string NoLeaderboard(RankedStatus status)
    {
        return $"{(int)status}|false";
    }

    public static string Found(BeatmapLeaderboardResult result)
    {
        var scoreRows = result.ScoreRows!;
        var lines = new List<string>
        {
            $"{(int)result.RankedStatus!.Value}|false|{result.BeatmapId}|{result.BeatmapSetId}|{scoreRows.Count}|0|",
            $"0\n{result.BeatmapName}\n{result.BeatmapRating}"
        };

        if (scoreRows.Count == 0)
        {
            lines.Add("");
            lines.Add("");
            return string.Join("\n", lines);
        }

        lines.Add(result.PersonalBest is { } best
            ? FormatLine(best.Id, best.Name, best.Score, best.MaxCombo, best.N50, best.N100, best.N300,
                best.NMiss, best.NKatu, best.NGeki, best.Perfect, best.Mods, best.UserId, best.Rank, best.Time)
            : "");

        for (var i = 0; i < scoreRows.Count; i++)
        {
            var row = scoreRows[i];
            lines.Add(FormatLine(row.Id, row.Name, row.Score, row.MaxCombo, row.N50, row.N100, row.N300,
                row.NMiss, row.NKatu, row.NGeki, row.Perfect, row.Mods, row.UserId, i + 1, row.Time));
        }

        return string.Join("\n", lines);
    }

    private static string FormatLine(
        long id, string name, long score, int maxCombo, int n50, int n100, int n300,
        int nMiss, int nKatu, int nGeki, bool perfect, int mods, int userId, int rank, long time)
    {
        return
            $"{id}|{name}|{score}|{maxCombo}|{n50}|{n100}|{n300}|{nMiss}|{nKatu}|{nGeki}|{(perfect ? 1 : 0)}|{mods}|{userId}|{rank}|{time}|1";
    }
}