using Basil.Domain.Bancho;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Scores;

/// <summary>Validates and records a score submission.</summary>
internal interface IScoreRecording
{
	/// <summary>Validates and records a score with its optional round and replay.</summary>
	/// <param name="player">The account that submitted the score.</param>
	/// <param name="loginClient">The osu! client the player logged in with.</param>
	/// <param name="submission">The parsed submission.</param>
	/// <param name="checkedBeatmap">The checksums of the beatmap the score is checked against.</param>
	/// <param name="client">The osu! client information sent with the submission.</param>
	/// <param name="round">The round the player played, or <see langword="null" />.</param>
	/// <param name="team">The team the score was set for, or <see langword="null" />.</param>
	/// <param name="replay">The replay bytes, or <see langword="null" /> when none is kept.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The rejection, or the stored score and updated statistics.</returns>
	Task<ScoreOutcome> ValidateAndRecordAsync(
		UserRecord player,
		BanchoClient loginClient,
		Submission submission,
		BeatmapChecksums checkedBeatmap,
		SubmittedClient client,
		Round? round,
		GameTeam? team,
		byte[]? replay,
		CancellationToken cancellationToken = default);
}

/// <summary>The result of validating and recording a score submission.</summary>
internal readonly record struct ScoreOutcome(
	ScoreRejection? Rejection,
	ScoreRecord? Score,
	RoundScore? RoundScore,
	UserStats? Stats);
