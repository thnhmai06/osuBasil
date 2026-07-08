using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Application.UseCases.Scores;

/// <summary>
///     Ported from app/services/score_submission.py's apply_score_base_stats/ranked_score_delta/
///     grade_count_deltas/apply_ranked_score_stats/apply_score_stats. Pure over
///     <see cref="CachedPlayerStats" /> + <see cref="ScoreSubmission" /> inputs — no I/O, no mutation of
///     either argument, matching the orchestration's compute-then-commit approach (see
///     docs/csharp-migration-plan.md Phase 6 notes on why Python's deep-copy snapshot/restore isn't
///     ported). Weighted pp/acc (fetch_weighted_best_performances/apply_weighted_performance_stats)
///     is dropped entirely — no-pp scope.
/// </summary>
public static class ScoreStatsCalculator
{
    /// <summary>Ported from apply_score_base_stats — applies to every submitted score, regardless of pass/fail or map status.</summary>
    public static CachedPlayerStats ApplyScoreBaseStats(ScoreSubmission score, CachedPlayerStats stats)
    {
        var totalHitsDelta = score.N300 + score.N100 + score.N50;
        var modeVanilla = score.Mode.AsVanilla();
        if (modeVanilla is 1 or 3)
            // Taiko uses geki/katu for big-note hits; mania uses them for rainbow 300/200.
            totalHitsDelta += score.NGeki + score.NKatu;

        return stats with
        {
            Playtime = stats.Playtime + score.TimeElapsed / 1000,
            Plays = stats.Plays + 1,
            Tscore = stats.Tscore + score.Score,
            TotalHits = stats.TotalHits + totalHitsDelta
        };
    }

    /// <summary>Ported from ranked_score_delta.</summary>
    public static long RankedScoreDelta(ScoreSubmission score)
    {
        return score.PrevBest is null ? score.Score : score.Score - score.PrevBest.Score;
    }

    /// <summary>Ported from grade_count_deltas. Only grades &gt;= A are tracked (matching the DB's xh/x/sh/s/a columns).</summary>
    public static IReadOnlyDictionary<Grade, int> GradeCountDeltas(ScoreSubmission score)
    {
        if (score.PrevBest is null)
            return score.Grade >= Grade.A
                ? new Dictionary<Grade, int> { [score.Grade] = 1 }
                : new Dictionary<Grade, int>();

        if (score.Grade == score.PrevBest.Grade) return new Dictionary<Grade, int>();

        var deltas = new Dictionary<Grade, int>();
        if (score.Grade >= Grade.A) deltas[score.Grade] = 1;

        if (score.PrevBest.Grade >= Grade.A)
            deltas[score.PrevBest.Grade] = deltas.GetValueOrDefault(score.PrevBest.Grade) - 1;

        return deltas;
    }

    /// <summary>Ported from apply_ranked_score_stats.</summary>
    public static CachedPlayerStats ApplyRankedScoreStats(ScoreSubmission score, CachedPlayerStats stats)
    {
        int xh = stats.XhCount, x = stats.XCount, sh = stats.ShCount, s = stats.SCount, a = stats.ACount;

        foreach (var (grade, delta) in GradeCountDeltas(score))
            switch (grade)
            {
                case Grade.Xh: xh += delta; break;
                case Grade.X: x += delta; break;
                case Grade.Sh: sh += delta; break;
                case Grade.S: s += delta; break;
                case Grade.A: a += delta; break;
                default: throw new ArgumentOutOfRangeException(nameof(score), grade, "Unexpected grade count update.");
            }

        return stats with
        {
            XhCount = xh, XCount = x, ShCount = sh, SCount = s, ACount = a,
            Rscore = stats.Rscore + RankedScoreDelta(score)
        };
    }

    /// <summary>
    ///     Ported from apply_score_stats. Uses <see cref="Beatmap.HasLeaderboardStrict" /> (not
    ///     <see cref="Beatmap.HasLeaderboard" />) for the max-combo gate, and
    ///     <see cref="Beatmap.AwardsRankedScore" /> for the ranked-score/grade-count gate — these are
    ///     deliberately different subsets of RankedStatus (see Beatmap's doc comments).
    /// </summary>
    public static CachedPlayerStats ApplyScoreStats(ScoreSubmission score, CachedPlayerStats stats)
    {
        var updated = ApplyScoreBaseStats(score, stats);

        if (!score.Passed || score.Bmap is null || !score.Bmap.HasLeaderboardStrict) return updated;

        if (score.MaxCombo > updated.MaxCombo) updated = updated with { MaxCombo = score.MaxCombo };

        if (score.Bmap.AwardsRankedScore && score.Status == SubmissionStatus.Best)
            updated = ApplyRankedScoreStats(score, updated);

        return updated;
    }
}