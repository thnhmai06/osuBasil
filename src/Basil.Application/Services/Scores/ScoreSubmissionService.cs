using System.Collections.Concurrent;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Services.Authentication;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Application.Services.Scores;

public enum ScoreSubmissionResultCode
{
    Success,
    BeatmapNotFound,
    PlayerNotFound,
    DuplicateSubmission,
    NotInMultiplayer
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

/// <summary>
///     A submitted score plus everything about it that isn't intrinsic to the score fact itself: its
///     persisted id (DB-generated, never carried on <see cref="ScoreSubmission" />), the resolved
///     beatmap and player name (looked up once, threaded through rather than re-queried), and the
///     rank reported back to the client.
/// </summary>
public sealed record SubmittedScoreResult(
    ScoreSubmission Score,
    long ScoreId,
    Beatmap Beatmap,
    string PlayerName,
    int? Rank);

public sealed record ScoreSubmissionOutcome(ScoreSubmissionResultCode Code, SubmittedScoreResult? Result = null);

/// <summary>
///     Ported from app/services/score_submission.py's ScoreSubmissionService.submit_score, collapsed
///     to Basil's no-pp scope (every `pp`-vs-`score` branch in the Python source always takes the
///     score branch here), its 100%-offline scope (no `.osu` file fetch), and its fixed-stats scope
///     (no per-user stats/rank update on submission — see docs/scope-decisions.md). If the submitting
///     player is currently in a multiplayer match, the score is linked to the match's current Round
///     (<see cref="MatchSession.CurrentRoundId" />) with the player's slot team — this is what lets
///     the TRT and Scores read paths reconstruct a match's results, without any gather/wait step:
///     submission and MatchComplete arrive on separate connections with no ordering guarantee, so the
///     link is written at submission time rather than collected later.
/// </summary>
public sealed class ScoreSubmissionService(
    IMapRepository maps,
    IScoreRepository scores,
    AuthenticationService authentication,
    IReplayStorage replayStorage)
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

        var score = ScoreSubmission.FromSubmission(request.ScoreDataFields.Skip(2).ToArray()) with
        {
            BeatmapMd5 = beatmap.Md5,
            UserId = player.Id,
            ServerTime = DateTimeOffset.UtcNow.UtcDateTime
        };

        try
        {
            if (player.OsuVersion is null) throw new ScoreSubmissionIntegrityException("missing login osu! version");

            score.ValidateSubmissionIntegrity(
                player.Client, player.OsuVersion.Date, player.Name, request.OsuVersion, request.ClientHash,
                request.UniqueIds, request.StoryboardMd5, beatmapMd5, request.UpdatedBeatmapHash);
        }
        catch (ScoreSubmissionIntegrityException)
        {
            // Non-fatal: bancho.py only logs + records a metric here — ported as-is.
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
        MatchTeam? team = match?.GetSlot(player.Id)?.Team is { } slotTeam and not MatchTeam.Neutral
            ? slotTeam
            : null;

        // Multiplayer-only scope: a score played outside a room, or inside a room with no round
        // currently in progress, is neither processed nor persisted (no lock, no dedupe check, no
        // DB row, no replay, no play-count increment).
        if (roundId is null) return new ScoreSubmissionOutcome(ScoreSubmissionResultCode.NotInMultiplayer);

        var checksumLock = ChecksumLocks.GetOrAdd(score.ClientChecksum, static _ => new SemaphoreSlim(1, 1));
        await checksumLock.WaitAsync(cancellationToken);
        try
        {
            if (await scores.ExistsByOnlineChecksumAsync(score.ClientChecksum, cancellationToken))
                return new ScoreSubmissionOutcome(ScoreSubmissionResultCode.DuplicateSubmission);

            var (updatedScore, rank) = CalculateSubmissionStatus(score, request.ScoreTime, request.FailTime);
            score = updatedScore;

            var replayData = score.IsPassed ? request.ReplayData : null;
            if (replayData is not null && replayData.Length < MinReplaySize)
                // No restriction/moderation system — only the replay is discarded; the score still counts.
                replayData = null;

            var scoreId = await scores.CreateAsync(BuildInsertRow(score, roundId, team), cancellationToken);

            if (!player.Restricted) await maps.IncrementPlayCountsAsync(beatmap.Id, score.IsPassed, cancellationToken);

            if (replayData is not null) await replayStorage.WriteAsync(scoreId, replayData, cancellationToken);

            return new ScoreSubmissionOutcome(
                ScoreSubmissionResultCode.Success,
                new SubmittedScoreResult(score, scoreId, beatmap, player.Name, rank));
        }
        finally
        {
            checksumLock.Release();
        }
    }

    /// <summary>
    ///     Every passed score is unconditionally the player's best (no comparison against prior
    ///     scores) and always reported at rank 1 — a deliberate product decision so the osu! client
    ///     always believes it achieved a top score and uploads its replay.
    /// </summary>
    private static (ScoreSubmission Score, int? Rank) CalculateSubmissionStatus(
        ScoreSubmission score, int scoreTime, int failTime)
    {
        var rank = score.IsPassed ? 1 : (int?)null;

        return (score with { TimeElapsed = TimeSpan.FromMilliseconds(score.IsPassed ? scoreTime : failTime) }, rank);
    }

    private static ScoreInsertRow BuildInsertRow(ScoreSubmission score, int? roundId, MatchTeam? team)
    {
        return new ScoreInsertRow(
            score.BeatmapMd5,
            score.Score,
            score.Accuracy,
            score.MaxCombo,
            score.Mods,
            score.HitCounts.x300,
            score.HitCounts.x100,
            score.HitCounts.x50,
            score.HitCounts.xMiss,
            score.HitCounts.xGeki,
            score.HitCounts.xKatu,
            score.Grade.ToString(),
            score.Mode,
            score.ServerTime,
            (int)score.TimeElapsed.TotalMilliseconds,
            score.ClientFlags,
            score.UserId,
            score.IsFullCombo,
            score.ClientChecksum,
            score.ServerTime,
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
