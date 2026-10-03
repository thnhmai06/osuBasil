using Basil.Application.Common;
using Basil.Domain.Scores;

namespace Basil.Application.Scores;

/// <summary>Stores submitted scores.</summary>
public interface IScoreRepository
{
	/// <summary>Stores a new score and assigns its id.</summary>
	/// <param name="data">The data of the new score.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored score, or <see langword="null" /> when a score with the same <see cref="ScoreData.Checksum" /> is already stored.</returns>
	Task<Score?> CreateAsync(ScoreData data, CancellationToken cancellationToken = default);

	/// <summary>Gets a score by id.</summary>
	/// <param name="id">The score id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The score, or <see langword="null" /> when no score has that id.</returns>
	ValueTask<Score?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Lists the scores a query includes, newest first.</summary>
	/// <param name="query">Which scores to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A page of scores.</returns>
	Task<Page<Score>> ListAsync(ScoreQuery query, PageRequest page, CancellationToken cancellationToken = default);
}