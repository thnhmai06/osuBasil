using Basil.Application.Shared;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Persistence;

/// <summary>Stores submitted scores.</summary>
public interface IScoreRepository
{
	/// <summary>Stores a new score and assigns its id.</summary>
	/// <param name="data">The data of the new score.</param>
	/// <returns>The stored score.</returns>
	/// <exception cref="AlreadyExistsException">A score with the same <see cref="ScoreData.Checksum" /> is already stored.</exception>
	Task<ScoreRecord> CreateAsync(ScoreData data);

	/// <summary>
	///     Stores a new score and adds its play to the statistics of its player, both stored or neither.
	/// </summary>
	/// <param name="data">The data of the new score; its player must be set.</param>
	/// <param name="rankedScore">The score the play adds to the ranked score, zero when it does not count.</param>
	/// <returns>The stored score and the statistics of its player in its mode including the play.</returns>
	/// <exception cref="AlreadyExistsException">A score with the same <see cref="ScoreData.Checksum" /> is already stored.</exception>
	Task<(ScoreRecord Score, UserStats Stats)> CreateWithPlayAsync(ScoreData data, long rankedScore);

	/// <summary>Gets a score by id.</summary>
	/// <param name="id">The score id.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The score, or <see langword="null" /> when no score has that id.</returns>
	ValueTask<ScoreRecord?> GetAsync(int id, CancellationToken ct = default);

	/// <summary>Gets the scores that have the given ids.</summary>
	/// <remarks>At most <see cref="ListLimits.Ceiling" /> scores are returned.</remarks>
	/// <param name="ids">The score ids; an id no score has is left out.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The stored scores among them, in ascending order of id.</returns>
	Task<IReadOnlyList<ScoreRecord>> GetManyAsync(IReadOnlyCollection<int> ids,
		CancellationToken ct = default);

	/// <summary>
	///     Lists matching scores. Beatmap or per-player-best listings are ordered by total score descending, then
	///     timestamp and id ascending; other listings are ordered by timestamp and id descending.
	/// </summary>
	/// <param name="query">Which scores to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>A page of scores.</returns>
	Task<Page<ScoreRecord>> ListAsync(ScoreQuery query, PageRequest page,
		CancellationToken ct = default);
}