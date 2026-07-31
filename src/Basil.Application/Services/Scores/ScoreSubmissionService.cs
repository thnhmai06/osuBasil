using System.Collections.Concurrent;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Authentication;
using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Scores;

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
///     score branch here) and its 100%-offline scope (no `.osu` file fetch). Every valid submission
///     is persisted unconditionally, solo or in a room, matching bancho.py's own unconditional
///     persistence — the only server-side rejections are an unknown beatmap and a duplicate
///     checksum. If the submitting player is currently in a multiplayer match with an active round,
///     the score is opportunistically linked to that round
///     (<see cref="Basil.Application.Sessions.Multiplayer.MatchSession.CurrentRoundId" />) with the
///     player's slot team, which is what lets the TRT and Scores read paths reconstruct a match's
///     results; a solo score (or one that arrives after its round already ended — see
///     <c>MatchCompleteHandler</c>'s doc comment) is simply persisted with a null RoundId/Team instead,
///     exactly like the schema's own `Scores` table comment describes. Every persisted submission
///     (pass or fail) also bumps <see cref="IStatsRepository.IncrementAsync" /> — TotalScore always,
///     RankedScore only when linked to a round — and the submitting player's own in-memory
///     <see cref="PlayerSession.ModeStats" /> cache, so their next `ChangeAction`-triggered UserStats
///     packet reflects it without needing to re-login.
/// </summary>
public sealed class ScoreSubmissionService(
	IMapRepository maps,
	IScoreRepository scores,
	IStatsRepository stats,
	AuthenticationService authentication,
	IReplayStorage replayStorage,
	ILogger<ScoreSubmissionService> logger)
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
		if (beatmap is null)
		{
			logger.LogInformation("Score submission rejected: BeatmapMd5={BeatmapMd5} not found", beatmapMd5);
			return new ScoreSubmissionOutcome(ScoreSubmissionResultCode.BeatmapNotFound);
		}

		var username = ExtractUsername(request.ScoreDataFields[1]);
		var player =
			await authentication.AuthenticateOnlinePlayerAsync(username, request.PasswordMd5, cancellationToken);
		if (player is null)
		{
			logger.LogInformation("Score submission rejected: Username={Username} not authenticated", username);
			return new ScoreSubmissionOutcome(ScoreSubmissionResultCode.PlayerNotFound);
		}

		var score = ScoreSubmission.FromSubmission([.. request.ScoreDataFields.Skip(2)]) with
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
		catch (ScoreSubmissionIntegrityException e)
		{
			// Non-fatal: bancho.py only logs + records a metric here — ported as-is.
			logger.LogInformation("Score submission integrity check failed: UserId={UserId} Reason={Reason}",
				player.Id, e.Message);
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

		var checksumLock = ChecksumLocks.GetOrAdd(score.ClientChecksum, static _ => new SemaphoreSlim(1, 1));
		await checksumLock.WaitAsync(cancellationToken);
		try
		{
			if (await scores.ExistsByOnlineChecksumAsync(score.ClientChecksum, cancellationToken))
			{
				logger.LogInformation("Score submission rejected: UserId={UserId} ClientChecksum={ClientChecksum} " +
				                      "Reason=Duplicate", player.Id, score.ClientChecksum);
				return new ScoreSubmissionOutcome(ScoreSubmissionResultCode.DuplicateSubmission);
			}

			var (updatedScore, rank) = CalculateSubmissionStatus(score, request.ScoreTime, request.FailTime);
			score = updatedScore;

			var replayData = score.IsPassed ? request.ReplayData : null;
			if (replayData is not null && replayData.Length < MinReplaySize)
			{
				// No restriction/moderation system — only the replay is discarded; the score still counts.
				logger.LogDebug("Replay discarded (under MinReplaySize): UserId={UserId}", player.Id);
				replayData = null;
			}

			var scoreId = await scores.CreateAsync(BuildInsertRow(score, roundId, team), cancellationToken);

			var rankedScoreDelta = roundId is not null ? score.Score : 0L;
			await stats.IncrementAsync(player.Id, score.Mode, score.Score, rankedScoreDelta, cancellationToken);

			var prevStats = player.ModeStats.GetValueOrDefault(score.Mode);
			player.ModeStats[score.Mode] = new CachedPlayerStats(
				(prevStats?.TotalScore ?? 0) + score.Score,
				(prevStats?.RankedScore ?? 0) + rankedScoreDelta,
				(prevStats?.Plays ?? 0) + 1,
				prevStats?.Rank ?? player.Id);

			using (logger.BeginScope(new Dictionary<string, object> { ["ScoreId"] = scoreId }))
			{
				if (replayData is not null) await replayStorage.WriteAsync(scoreId, replayData, cancellationToken);
				logger.LogInformation(
					"+ Score submitted: UserId={UserId} BeatmapMd5={BeatmapMd5} MatchId={MatchId} Score={Score}",
					player.Id, beatmap.Md5, match?.DbId, score.Score);
			}

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
	///     always believes it achieved a top score and uploads its replay. This is the per-beatmap
	///     chart rank shown on the results screen — unrelated to the player's overall/global rank
	///     (<see cref="Basil.Application.Sessions.CachedPlayerStats.Rank" />), which is the player's own
	///     user id instead (see <c>LoginService</c>).
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