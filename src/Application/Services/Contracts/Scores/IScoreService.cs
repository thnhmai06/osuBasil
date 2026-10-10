using Basil.Application.Services.Contracts.Events;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Scores;

namespace Basil.Application.Services.Contracts.Scores;

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
	/// <param name="client">The osu! client information sent with the submission.</param>
	/// <param name="replay">The replay bytes, or <see langword="null" /> for a failed play.</param>
	/// <param name="cancellationToken">A token that cancels the submission.</param>
	/// <returns><see langword="null" /> when the score was stored; otherwise, why it was rejected.</returns>
	/// <remarks>
	///     A score on a beatmap the server does not have is accepted only when it is the beatmap of the
	///     latest round in the player's room. A score played in that round carries the round, and
	///     <see cref="ScoreSubmitted" /> names the room so the round can record it. Every
	///     accepted play adds to the play count and the total score; passed plays also add to the ranked score.
	///     The submission is checked against the player's latest recorded login. A submission whose
	///     checksum was already stored is refused. Only a passed play keeps its replay, and only when
	///     the replay is at least 24 bytes long.
	/// </remarks>
	Task<ScoreRejection?> SubmitAsync(
		BanchoConnection connection,
		Submission submission,
		BeatmapChecksums? beatmap,
		SubmittedClient client,
		byte[]? replay,
		CancellationToken cancellationToken = default);
}