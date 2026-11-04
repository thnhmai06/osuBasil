using Basil.Application.Scores.Persistence;
using Basil.Domain.Client;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Ports;

/// <summary>Validates and stores a submitted score with its replay and the player's updated statistics.</summary>
internal interface IScoreRecorder
{
	/// <summary>Validates and records a score with its optional replay and updated statistics.</summary>
	/// <param name="player">The account that submitted the score.</param>
	/// <param name="loginClient">The osu! client the player logged in with.</param>
	/// <param name="submission">The parsed submission.</param>
	/// <param name="checkedBeatmap">The checksums of the beatmap the score is checked against.</param>
	/// <param name="client">The osu! client information sent with the submission.</param>
	/// <param name="replay">
	///     The seekable stream of the replay, positioned at its start and left open, or <see langword="null" /> when
	///     none is kept.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The rejection, or the stored score and updated statistics.</returns>
	Task<ScoreOutcome> ValidateAndRecordAsync(UserRecord player, OsuClient loginClient, Submission submission,
		BeatmapChecksums checkedBeatmap, SubmittedClient client, Stream? replay,
		CancellationToken cancellationToken = default);
}
