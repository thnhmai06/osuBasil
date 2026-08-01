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

/// <summary>
///     Identifies the outcome of a score submission attempt.
/// </summary>
public enum ScoreSubmissionResultCode
{
	/// <summary>The score was stored successfully.</summary>
	Success,

	/// <summary>The submitted beatmap MD5 does not match any stored beatmap.</summary>
	BeatmapNotFound,

	/// <summary>The submitting player could not be authenticated as an online player.</summary>
	PlayerNotFound,

	/// <summary>A score with the same online checksum was already stored.</summary>
	DuplicateSubmission
}

/// <summary>
///     Holds the raw, decrypted data of a single score submission.
/// </summary>
/// <param name="ScoreDataFields">
///     The full colon-delimited submission: the beatmap MD5, the username, then the sixteen
///     score fields.
/// </param>
/// <param name="PasswordMd5">The MD5 hash of the player's password.</param>
/// <param name="OsuVersion">The osu! version string sent with the submission.</param>
/// <param name="ClientHash">The client hash of the submitting client.</param>
/// <param name="UniqueIds">The pipe-delimited unique-id string sent by the client.</param>
/// <param name="StoryboardMd5">
///     The MD5 of the beatmap's storyboard file, or <see langword="null" /> if the beatmap has
///     none.
/// </param>
/// <param name="UpdatedBeatmapHash">
///     The beatmap hash the client claims, compared against the stored one during integrity
///     validation.
/// </param>
/// <param name="ScoreTime">The duration of a passed play, in milliseconds.</param>
/// <param name="FailTime">The duration of a failed play, in milliseconds.</param>
/// <param name="ReplayData">The raw replay bytes from the submission, or <see langword="null" /> for a failed play.</param>
/// <remarks>
///     Decryption happens at the HTTP endpoint, which owns the encrypted form fields this use case
///     never sees.
/// </remarks>
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
///     A stored score together with the context that surrounds it but is not part of the score
///     fact itself.
/// </summary>
/// <param name="Score">The stored submission.</param>
/// <param name="ScoreId">The database id assigned to the stored score.</param>
/// <param name="Beatmap">The beatmap the score was played on.</param>
/// <param name="PlayerName">The submitting player's name.</param>
/// <param name="Rank">The per-beatmap rank reported back to the client, or <see langword="null" /> for a failed play.</param>
/// <remarks>
///     <see cref="ScoreId" /> is database-generated and never carried on
///     <see cref="Submission" />. The beatmap and player name are looked up once during submission
///     and threaded through rather than re-queried by the response path.
/// </remarks>
public sealed record SubmittedScoreResult(
	Submission Score,
	long ScoreId,
	Beatmap Beatmap,
	string PlayerName,
	int? Rank);

/// <summary>
///     The outcome of a score submission attempt.
/// </summary>
/// <param name="Code">The result of the attempt.</param>
/// <param name="Result">
///     The stored score and its context when the submission succeeded, or <see langword="null" />
///     otherwise.
/// </param>
public sealed record ScoreSubmissionOutcome(ScoreSubmissionResultCode Code, SubmittedScoreResult? Result = null);

/// <summary>
///     Persists a score submitted by an osu! client and reports the outcome back.
/// </summary>
/// <remarks>
///     Runs in Basil's no-pp, fully-offline scope: every valid submission is stored
///     unconditionally, solo or in a room, with no pp calculation and no fetch of the beatmap's
///     <c>.osu</c> file. The only server-side rejections are an unknown beatmap and a duplicate
///     online checksum. When the submitting player is in a multiplayer match with an active round,
///     the score is opportunistically linked to that round
///     (<see cref="Basil.Application.Sessions.Multiplayer.MatchSession.CurrentRoundId" />) with the
///     player's slot team, which is what lets the match report and score read paths reconstruct a
///     match's results. A solo score, or one that arrives after its round already ended, is stored
///     with a null round id and team instead. Every stored submission, pass or fail, also bumps
///     <see cref="IStatsRepository.IncrementAsync" /> (TotalScore always, RankedScore only when
///     linked to a round) and refreshes the player's in-memory
///     <see cref="PlayerSession.ModeStats" /> cache, so the next user-stats packet reflects the
///     change without a re-login.
/// </remarks>
public sealed class ScoreSubmissionService(
	IBeatmapRepository beatmaps,
	IScoreRepository scores,
	IStatsRepository stats,
	AuthenticationService authentication,
	IReplayStorage replayStorage,
	ILogger<ScoreSubmissionService> logger)
{
	private const int MinReplaySize = 24;

	// One lock per in-flight online checksum, so two near-simultaneous submissions of the same
	// score cannot both pass the duplicate check.
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> ChecksumLocks = new();

	/// <summary>
	///     Submits a single score to the server.
	/// </summary>
	/// <param name="request">The decrypted score submission data.</param>
	/// <param name="cancellationToken">A token that cancels the submission.</param>
	/// <returns>A <see cref="ScoreSubmissionOutcome" /> describing the result.</returns>
	/// <remarks>
	///     Rejects the submission when the beatmap is unknown or the player cannot be authenticated
	///     as an online player. A valid, non-duplicate submission is stored unconditionally, its
	///     replay file written when present and large enough, and the result linked to the player's
	///     active match round when one exists.
	/// </remarks>
	public async Task<ScoreSubmissionOutcome> SubmitAsync(ScoreSubmissionRequest request,
		CancellationToken cancellationToken = default)
	{
		var beatmapMd5 = request.ScoreDataFields[0];
		var beatmap = await beatmaps.FetchOneAsync(md5: beatmapMd5, cancellationToken: cancellationToken);
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

		var score = Submission.FromSubmission([.. request.ScoreDataFields.Skip(2)]) with
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
			// Non-fatal: an integrity failure is only logged; the score is still processed.
			logger.LogInformation("Score submission integrity check failed: UserId={UserId} Reason={Reason}",
				player.Id, e.Message);
		}

		if (score.Mode != player.Status.Mode)
		{
			player.Status.Mods = score.Mods;
			player.Status.Mode = score.Mode;
		}

		// Captured before the checksum lock so a slot/team change racing with this submission
		// cannot matter: the round the player was actually playing is whatever their match and slot
		// said when gameplay ended.
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
				// No restriction or moderation system here, so only the replay is discarded; the
				// score still counts.
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
	///     Determines the per-beatmap rank to report for the submission.
	/// </summary>
	/// <param name="score">The parsed submission.</param>
	/// <param name="scoreTime">The duration of a passed play, in milliseconds.</param>
	/// <param name="failTime">The duration of a failed play, in milliseconds.</param>
	/// <returns>The submission with its elapsed time set and the rank to report.</returns>
	/// <remarks>
	///     Every passed score is unconditionally reported at rank 1, never compared against earlier
	///     scores, so the osu! client always believes it achieved a top score and uploads its
	///     replay. This rank is the per-beatmap chart rank shown on the results screen and is
	///     unrelated to the player's overall rank
	///     (<see cref="Basil.Application.Sessions.CachedPlayerStats.Rank" />), which holds the
	///     player's own user id instead (see
	///     <see cref="Basil.Application.Services.Authentication.LoginService" />).
	/// </remarks>
	private static (Submission Score, int? Rank) CalculateSubmissionStatus(
		Submission score, int scoreTime, int failTime)
	{
		var rank = score.IsPassed ? 1 : (int?)null;

		return (score with { TimeElapsed = TimeSpan.FromMilliseconds(score.IsPassed ? scoreTime : failTime) }, rank);
	}

	private static ScoreInsertRow BuildInsertRow(Submission score, int? roundId, MatchTeam? team)
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
	///     Removes the trailing space the client appends to the username of a supporter account.
	/// </summary>
	/// <param name="rawUsername">The username field as submitted by the client.</param>
	/// <returns>The username with one trailing space removed when present.</returns>
	private static string ExtractUsername(string rawUsername)
	{
		return rawUsername.EndsWith(' ') ? rawUsername[..^1] : rawUsername;
	}
}