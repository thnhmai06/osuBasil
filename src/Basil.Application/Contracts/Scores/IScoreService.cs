using Basil.Application.Events;
using Basil.Application.Scores;
using Basil.Application.Sessions;
using Basil.Domain.Client;
using Basil.Domain.Scores;
using Basil.Domain.Utilities;

namespace Basil.Application.Contracts.Scores;

/// <summary>Checks and stores submitted scores.</summary>
public interface IScoreService : IEventPublisher<ScoreEvent>
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
	///     The submission is checked against the player's latest recorded login. A submission whose
	///     checksum was already stored is refused. Only a passed play keeps its replay, and only when
	///     the replay is at least 24 bytes long. Anticheat flags sent with the submission are reported
	///     to the player's room.
	/// </remarks>
	Task<ScoreRejection?> SubmitAsync(
		BanchoConnection connection,
		Submission submission,
		(Md5 Hash, Md5? StoryboardHash)? beatmap,
		(string Hash, string Serial) clientFingerprint,
		string clientVersionDate,
		Md5 clientBeatmapHash,
		byte[]? replay,
		CancellationToken cancellationToken = default);
}