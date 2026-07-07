using System.Collections.Concurrent;
using OpenOsuTournament.Bancho.Application.Abstractions;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Application.Abstractions.Scores;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.UseCases.Authentication;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Multiplayer;
using OpenOsuTournament.Bancho.Domain.Scores;

namespace OpenOsuTournament.Bancho.Application.UseCases.Scores;

public enum ScoreSubmissionResultCode
{
    Success,
    BeatmapNotFound,
    PlayerNotFound,
    DuplicateSubmission
}

/// <summary>
///     Ported from app/services/score_submission.py's ScoreSubmissionRequest. ScoreDataFields is the
///     full decrypted colon-delimited submission (beatmap_md5, username, then the 16 score fields) —
///     decryption happens at the HTTP endpoint, which owns the encrypted form fields this use case
///     doesn't need to see.
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

public sealed record SubmittedScoreResult(ScoreSubmission Score, long ScoreId);

public sealed record ScoreSubmissionOutcome(ScoreSubmissionResultCode Code, SubmittedScoreResult? Result = null);

/// <summary>
///     Ported from app/services/score_submission.py's ScoreSubmissionService.submit_score, collapsed
///     to OpenOsuTournament.Bancho's no-pp scope (every `pp`-vs-`score` branch in the Python source always takes the
///     score branch here), its 100%-offline scope (no `.osu` file fetch), and its fixed-stats scope
///     (no per-user stats/rank update on submission — see docs/scope-decisions.md). If the submitting
///     player is currently in a multiplayer match, the score is linked to the match's current Round
///     (<see cref="MatchSession.CurrentRoundId" />) with the player's slot team — this is what lets
///     the TRT and Scores read paths reconstruct a match's results, without any gather/wait step:
///     submission and MatchComplete arrive on separate connections with no ordering guarantee, so the
///     link is written at submission time rather than collected later.
/// </summary>
public sealed class ScoreSubmissionUseCase(
    IMapRepository maps,
    IScoreRepository scores,
    IScoreSubmissionPersistence scoreSubmissionPersistence,
    BanchoAuthenticationService authentication,
    IReplayStorage replayStorage,
    IClock clock)
{
    private const int MinReplaySize = 24;

    // Ported from ScoreSubmissionLocks — one lock per in-flight online_checksum, so duplicate
    // near-simultaneous submissions of the same score can't both pass the duplicate check.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ChecksumLocks = new();

    public async Task<ScoreSubmissionOutcome> SubmitAsync(ScoreSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var beatmapMd5 = request.ScoreDataFields[0];
        var beatmap = await maps.FetchOneAsync(md5: beatmapMd5, cancellationToken: cancellationToken);
        if (beatmap is null) return new ScoreSubmissionOutcome(ScoreSubmissionResultCode.BeatmapNotFound);

        var username = ExtractUsername(request.ScoreDataFields[1]);
        var player =
            await authentication.AuthenticateOnlinePlayerAsync(username, request.PasswordMd5, cancellationToken);
        if (player is null) return new ScoreSubmissionOutcome(ScoreSubmissionResultCode.PlayerNotFound);

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

        if (score.Mode != player.Status.Mode)
        {
            player.Status.Mods = score.Mods;
            player.Status.Mode = score.Mode;
        }

        // Ported from Player.match — captured before the checksum lock so a slot/team change
        // racing with this submission can't matter (the round the player was actually playing is
        // whatever their match/slot said at the moment gameplay ended).
        var match = player.Match;
        var roundId = match?.CurrentRoundId;
        var team = match?.GetSlot(player.Id)?.Team is { } slotTeam and not MatchTeams.Neutral ? (int)slotTeam : (int?)null;

        var checksumLock = ChecksumLocks.GetOrAdd(score.ClientChecksum, static _ => new SemaphoreSlim(1, 1));
        await checksumLock.WaitAsync(cancellationToken);
        try
        {
            if (await scores.ExistsByOnlineChecksumAsync(score.ClientChecksum, cancellationToken))
                return new ScoreSubmissionOutcome(ScoreSubmissionResultCode.DuplicateSubmission);

            await CalculateSubmissionStatusAsync(score, request.ScoreTime, request.FailTime, cancellationToken);

            var replayData = score.Passed ? request.ReplayData : null;
            if (replayData is not null && replayData.Length < MinReplaySize)
                // TODO(restriction-phase): bancho.py restricts + logs out the player here. No
                // restriction/moderation system exists yet — only the replay is discarded for now;
                // the score itself still counts.
                replayData = null;

            var scoreId = await scoreSubmissionPersistence.PersistScoreSubmissionAsync(
                score.Status == SubmissionStatus.Best,
                score.Bmap.Md5,
                player.Id,
                score.Mode,
                BuildInsertRow(score, roundId, team),
                cancellationToken);
            score.Id = scoreId;

            if (!player.Restricted) await maps.IncrementPlayCountsAsync(score.Bmap.Id, score.Passed, cancellationToken);

            if (replayData is not null) await replayStorage.WriteAsync(scoreId, replayData, cancellationToken);

            return new ScoreSubmissionOutcome(
                ScoreSubmissionResultCode.Success,
                new SubmittedScoreResult(score, scoreId));
        }
        finally
        {
            checksumLock.Release();
        }
    }

    /// <summary>
    ///     Ported from calculate_score_submission_status, with the `.osu`-file-availability gate
    ///     removed entirely (see class doc comment) — status/placement always run for a passed score.
    /// </summary>
    private async Task CalculateSubmissionStatusAsync(ScoreSubmission score, int scoreTime, int failTime,
        CancellationToken cancellationToken)
    {
        score.Acc = score.CalculateAccuracy();

        if (score.Passed)
        {
            await CalculateStatusAsync(score, cancellationToken);

            if (score.Bmap!.Status != RankedStatus.Pending)
                score.Rank = await scores.FetchPersonalBestLeaderboardRankAsync(score.Bmap.Md5, score.Mode, score.Score,
                    cancellationToken);
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
        var prevBestRow =
            await scores.FetchPersonalBestLeaderboardScoreAsync(score.Bmap!.Md5, score.Mode, score.PlayerId,
                cancellationToken);
        if (prevBestRow is null)
        {
            score.Status = SubmissionStatus.Best;
            return;
        }

        var prevBestRank =
            await scores.FetchPersonalBestLeaderboardRankAsync(score.Bmap.Md5, score.Mode, prevBestRow.Score,
                cancellationToken);
        score.PrevBest = new ScoreSubmission
        {
            Score = prevBestRow.Score,
            Grade = GradeExtensions.Parse(prevBestRow.Grade),
            MaxCombo = prevBestRow.MaxCombo,
            Acc = prevBestRow.Acc,
            Rank = prevBestRank
        };
        score.Status = score.Score > prevBestRow.Score ? SubmissionStatus.Best : SubmissionStatus.Submitted;
    }

    private static ScoreInsertRow BuildInsertRow(ScoreSubmission score, int? roundId, int? team)
    {
        return new ScoreInsertRow(
            score.Bmap!.Md5,
            score.Score,
            score.Acc,
            score.MaxCombo,
            (int)score.Mods,
            score.N300,
            score.N100,
            score.N50,
            score.NMiss,
            score.NGeki,
            score.NKatu,
            score.Grade.ToString(),
            (int)score.Status,
            (int)score.Mode,
            score.ServerTime,
            score.TimeElapsed,
            (int)score.ClientFlags,
            score.PlayerId,
            score.Perfect,
            score.ClientChecksum,
            roundId,
            team);
    }

    /// <summary>
    ///     Ported from score_submission_username — a supporter client appends a trailing space; a username ending in a
    ///     real space must be preserved.
    /// </summary>
    private static string ExtractUsername(string rawUsername)
    {
        return rawUsername.EndsWith(' ') ? rawUsername[..^1] : rawUsername;
    }
}
