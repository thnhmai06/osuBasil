using System.Collections.Concurrent;
using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Authentication;
using Bancho.Domain;
using Bancho.Application.Abstractions.Beatmaps;
using Bancho.Application.Abstractions.Scores;
using Bancho.Domain.Beatmaps;
using Bancho.Domain.Login;
using Bancho.Domain.Scores;

namespace Bancho.Application.UseCases.Scores;

public enum ScoreSubmissionResultCode
{
    Success,
    BeatmapNotFound,
    PlayerNotFound,
    DuplicateSubmission,
}

/// <summary>
/// Ported from app/services/score_submission.py's ScoreSubmissionRequest. ScoreDataFields is the
/// full decrypted colon-delimited submission (beatmap_md5, username, then the 16 score fields) —
/// decryption happens at the HTTP endpoint, which owns the encrypted form fields this use case
/// doesn't need to see.
/// </summary>
public sealed record ScoreSubmissionRequest(
    IReadOnlyList<string> ScoreDataFields,
    string PasswordMd5,
    string OsuVersion,
    string ClientHash,
    string UniqueIds,
    string? StoryboardMd5,
    string UpdatedBeatmapHash,
    int ScoreTime,
    int FailTime,
    byte[]? ReplayData);

public sealed record SubmittedScoreResult(ScoreSubmission Score, long ScoreId, CachedPlayerStats PreviousStats, CachedPlayerStats CurrentStats);

public sealed record ScoreSubmissionOutcome(ScoreSubmissionResultCode Code, SubmittedScoreResult? Result = null);

/// <summary>
/// Ported from app/services/score_submission.py's ScoreSubmissionService.submit_score, collapsed
/// to bancho-net's no-pp scope (every `pp`-vs-`score` branch in the Python source always takes the
/// score branch here) and its 100%-offline scope (no `.osu` file fetch — see
/// docs/csharp-migration-plan.md Phase 6 notes: calculate_status/calculate_placement are pure DB
/// queries that were only ever gated behind the file-availability check for pp/sr's sake).
///
/// Deferred to later phases (none sit on the submitting client's response path — see note.md for
/// the full rationale): publish_user_stats broadcast, personal-best/first-place chat
/// announcements, and the actual privilege-mutation half of restrict-on-invalid-replay (no
/// restriction/moderation system exists yet). Achievements are dropped entirely, per scope.
/// </summary>
public sealed class ScoreSubmissionUseCase(
    IMapRepository maps,
    IScoreRepository scores,
    IScoreSubmissionPersistence scoreSubmissionPersistence,
    BanchoAuthenticationService authentication,
    IReplayStorage replayStorage,
    ILeaderboardStore leaderboardStore,
    IClock clock)
{
    private const int MinReplaySize = 24;

    // Ported from ScoreSubmissionLocks — one lock per in-flight online_checksum, so duplicate
    // near-simultaneous submissions of the same score can't both pass the duplicate check.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ChecksumLocks = new();

    public async Task<ScoreSubmissionOutcome> SubmitAsync(ScoreSubmissionRequest request, CancellationToken cancellationToken = default)
    {
        var beatmapMd5 = request.ScoreDataFields[0];
        var beatmap = await maps.FetchOneAsync(md5: beatmapMd5, cancellationToken: cancellationToken);
        if (beatmap is null)
        {
            return new ScoreSubmissionOutcome(ScoreSubmissionResultCode.BeatmapNotFound);
        }

        var username = ExtractUsername(request.ScoreDataFields[1]);
        var player = await authentication.AuthenticateOnlinePlayerAsync(username, request.PasswordMd5, cancellationToken);
        if (player is null)
        {
            return new ScoreSubmissionOutcome(ScoreSubmissionResultCode.PlayerNotFound);
        }

        var score = ScoreSubmission.FromSubmission(request.ScoreDataFields.Skip(2).ToArray());
        score.Bmap = beatmap;
        score.PlayerId = player.Id;
        score.PlayerName = player.Name;
        score.ServerTime = clock.UtcNow.UtcDateTime;

        try
        {
            ScoreSubmissionValidation.ValidateSubmissionIntegrity(
                player.Client, request.OsuVersion, request.ClientHash, request.UniqueIds, score,
                request.StoryboardMd5, beatmapMd5, request.UpdatedBeatmapHash);
        }
        catch (ScoreSubmissionIntegrityException)
        {
            // Non-fatal: bancho.py itself only logs + records a metric here right now (the
            // restriction branch is commented out pending a trial period) — ported as-is.
        }

        // Ported from update_submitter_status_mode's state mutation. The publish_user_stats
        // broadcast half is deferred — no consumer reads it before the presence-broadcast phase.
        if (score.Mode != player.Status.Mode)
        {
            player.Status.Mods = score.Mods;
            player.Status.Mode = score.Mode;
        }

        var checksumLock = ChecksumLocks.GetOrAdd(score.ClientChecksum, static _ => new SemaphoreSlim(1, 1));
        await checksumLock.WaitAsync(cancellationToken);
        try
        {
            if (await scores.ExistsByOnlineChecksumAsync(score.ClientChecksum, cancellationToken))
            {
                return new ScoreSubmissionOutcome(ScoreSubmissionResultCode.DuplicateSubmission);
            }

            await CalculateSubmissionStatusAsync(score, request.ScoreTime, request.FailTime, cancellationToken);

            var replayData = score.Passed ? request.ReplayData : null;
            if (replayData is not null && replayData.Length < MinReplaySize)
            {
                // TODO(restriction-phase): bancho.py restricts + logs out the player here. No
                // restriction/moderation system exists yet in bancho-net — only the replay is
                // discarded for now; the score itself still counts. See note.md.
                replayData = null;
            }

            var previousStats = player.ModeStats.GetValueOrDefault((int)score.Mode) ?? new CachedPlayerStats(0, 0, 0, 0, 0, 0, 0, 0);
            var updatedStats = ScoreStatsCalculator.ApplyScoreStats(score, previousStats);
            var shouldUpdateRank = score.Passed && score.Bmap.AwardsRankedScore && score.Status == SubmissionStatus.Best;

            // Ported from bancho.py's `async with self.database.transaction():` — the previous-best
            // demotion, score insert, and stats update commit atomically (see IScoreSubmissionPersistence's
            // doc comment for the bug this fixes over the original Phase 6 port).
            var scoreId = await scoreSubmissionPersistence.PersistScoreSubmissionAsync(
                markPreviousBestSubmitted: score.Status == SubmissionStatus.Best,
                mapMd5: score.Bmap.Md5,
                userId: player.Id,
                mode: score.Mode,
                scoreRow: BuildInsertRow(score),
                statsUpdate: new StatsUpdateRow(
                    updatedStats.Tscore, updatedStats.Rscore, updatedStats.Plays, updatedStats.Playtime,
                    updatedStats.Acc, updatedStats.MaxCombo, updatedStats.TotalHits, updatedStats.XhCount,
                    updatedStats.XCount, updatedStats.ShCount, updatedStats.SCount, updatedStats.ACount),
                cancellationToken: cancellationToken);
            score.Id = scoreId;

            if (!player.Restricted)
            {
                await maps.IncrementPlayCountsAsync(score.Bmap.Id, score.Passed, cancellationToken);
            }

            if (shouldUpdateRank)
            {
                await leaderboardStore.AddToGlobalLeaderboardAsync(player.Id, (int)score.Mode, updatedStats.Rscore, cancellationToken);
                await leaderboardStore.AddToCountryLeaderboardAsync(
                    player.Id, (int)score.Mode, player.Geoloc.CountryAcronym, updatedStats.Rscore, cancellationToken);
                var newRank = await leaderboardStore.FetchGlobalRankAsync(player.Id, (int)score.Mode, cancellationToken);
                if (newRank is not null)
                {
                    updatedStats = updatedStats with { Rank = newRank.Value };
                }
            }

            player.ModeStats[(int)score.Mode] = updatedStats;

            if (replayData is not null)
            {
                await replayStorage.WriteAsync(scoreId, replayData, cancellationToken);
            }

            return new ScoreSubmissionOutcome(
                ScoreSubmissionResultCode.Success,
                new SubmittedScoreResult(score, scoreId, previousStats, updatedStats));
        }
        finally
        {
            checksumLock.Release();
        }
    }

    /// <summary>
    /// Ported from calculate_score_submission_status, with the `.osu`-file-availability gate
    /// removed entirely (see class doc comment) — status/placement always run for a passed score.
    /// </summary>
    private async Task CalculateSubmissionStatusAsync(ScoreSubmission score, int scoreTime, int failTime, CancellationToken cancellationToken)
    {
        score.Acc = score.CalculateAccuracy();

        if (score.Passed)
        {
            await CalculateStatusAsync(score, cancellationToken);

            if (score.Bmap!.Status != RankedStatus.Pending)
            {
                score.Rank = await scores.FetchPersonalBestLeaderboardRankAsync(score.Bmap.Md5, score.Mode, score.Score, cancellationToken);
            }
        }
        else
        {
            score.Status = SubmissionStatus.Failed;
        }

        score.TimeElapsed = score.Passed ? scoreTime : failTime;
    }

    /// <summary>Ported from Score.calculate_status, collapsed to score-only comparison (was `self.pp > rec.pp`).</summary>
    private async Task CalculateStatusAsync(ScoreSubmission score, CancellationToken cancellationToken)
    {
        var prevBestRow = await scores.FetchPersonalBestLeaderboardScoreAsync(score.Bmap!.Md5, score.Mode, score.PlayerId, cancellationToken);
        if (prevBestRow is null)
        {
            score.Status = SubmissionStatus.Best;
            return;
        }

        var prevBestRank = await scores.FetchPersonalBestLeaderboardRankAsync(score.Bmap.Md5, score.Mode, prevBestRow.Score, cancellationToken);
        score.PrevBest = new ScoreSubmission
        {
            Score = prevBestRow.Score,
            Grade = GradeExtensions.Parse(prevBestRow.Grade),
            MaxCombo = prevBestRow.MaxCombo,
            Acc = prevBestRow.Acc,
            Rank = prevBestRank,
        };
        score.Status = score.Score > prevBestRow.Score ? SubmissionStatus.Best : SubmissionStatus.Submitted;
    }

    private static ScoreInsertRow BuildInsertRow(ScoreSubmission score) => new(
        MapMd5: score.Bmap!.Md5,
        Score: score.Score,
        Acc: score.Acc,
        MaxCombo: score.MaxCombo,
        Mods: (int)score.Mods,
        N300: score.N300,
        N100: score.N100,
        N50: score.N50,
        NMiss: score.NMiss,
        NGeki: score.NGeki,
        NKatu: score.NKatu,
        Grade: score.Grade.ToString(),
        Status: (int)score.Status,
        Mode: (int)score.Mode,
        PlayTime: score.ServerTime,
        TimeElapsed: score.TimeElapsed,
        ClientFlags: (int)score.ClientFlags,
        UserId: score.PlayerId,
        Perfect: score.Perfect,
        OnlineChecksum: score.ClientChecksum);

    /// <summary>Ported from score_submission_username — a supporter client appends a trailing space; a username ending in a real space must be preserved.</summary>
    private static string ExtractUsername(string rawUsername) => rawUsername.EndsWith(' ') ? rawUsername[..^1] : rawUsername;
}
