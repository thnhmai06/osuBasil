using Basil.Application.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Scores;
using Basil.Domain.Utilities;

namespace Basil.Application.Scores;

/// <summary>Validates and stores a submitted score, its replay, and the submitter's updated statistics.</summary>
public sealed class ScoreSubmission(
	IScoreRepository scores,
	IReplayStorage replays,
	IUserStatsRepository stats,
	Lobby lobby)
{
	/// <summary>Validates and records a score submission.</summary>
	/// <param name="connection">The game client connection that submitted the score.</param>
	/// <param name="submission">The parsed submission.</param>
	/// <param name="beatmap">
	///     The beatmap the server knows for the submission, or <see langword="null" /> when the server does
	///     not have it.
	/// </param>
	/// <param name="clientFingerprint">The client hash and unique ids sent with the submission.</param>
	/// <param name="clientVersionDate">The client version date sent with the submission.</param>
	/// <param name="clientBeatmapHash">The beatmap MD5 the client claims to have played.</param>
	/// <param name="replay">The replay bytes, or <see langword="null" /> for a failed play.</param>
	/// <param name="cancellationToken">A token that cancels the submission.</param>
	/// <returns><see langword="null" /> when the score was stored; otherwise, why it was rejected.</returns>
	/// <remarks>
	///     A score on a beatmap the server does not have is accepted only when it is the beatmap of the
	///     latest round in the player's room. A score played in that round is recorded against it. Every
	///     accepted play counts toward the play count; passed plays also add to the scores.
	/// </remarks>
	public async Task<ScoreRejection?> SubmitAsync(
		BanchoConnection connection,
		Submission submission,
		(Md5 Hash, Md5? StoryboardHash)? beatmap,
		(string Hash, string Serial) clientFingerprint,
		string clientVersionDate,
		Md5 clientBeatmapHash,
		byte[]? replay,
		CancellationToken cancellationToken = default)
	{
		var room = lobby.RoomOf(connection);
		var round = room?.LastRound is { } last && last.BeatmapHash == clientBeatmapHash ? last : null;
		var checkedBeatmap = beatmap ?? (round is not null ? (clientBeatmapHash, null) : null);
		if (checkedBeatmap is not { } known) return ScoreRejection.UnknownBeatmap;

		if (submission.Validate(connection.Login.Client!, known, connection.User.Value.Name, clientFingerprint,
			    clientVersionDate, clientBeatmapHash) is { } rejection)
			return rejection;

		var score = await scores.CreateAsync(submission.Score with { UserId = connection.User.Id, Round = round },
			cancellationToken);
		if (score is null) return ScoreRejection.Duplicate;

		if (replay is not null)
		{
			await using var content = new MemoryStream(replay, false);
			await replays.SaveAsync(score, content, cancellationToken);
		}

		// ponytail: get-then-write per user; add an atomic increment to the contract if one user's submissions can overlap.
		var current = await stats.GetAsync(connection.User, submission.Score.Mode, cancellationToken);
		current.PlayCount++;
		if (submission.Score.IsPassed)
		{
			// Every beatmap reports as Approved (see Beatmapset.Status), so every passed score counts
			// toward ranked score too.
			current.TotalScore += submission.Score.TotalScore;
			current.RankedScore += submission.Score.TotalScore;
		}

		await stats.CreateOrUpdateAsync(current, cancellationToken);

		if (room is not null && round is not null)
		{
			await using var scope = await room.EnterAsync(cancellationToken);
			if (scope is not null) room.RecordScore(connection.User, score);
		}

		return null;
	}
}