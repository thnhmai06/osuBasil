using Basil.Domain.Scores;

namespace Basil.Application.Scores;

/// <summary>Stores submitted scores.</summary>
public interface IScoreRepository
{
	/// <summary>Stores a new score and assigns its id.</summary>
	/// <param name="data">The data of the new score.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	/// <returns>The stored score.</returns>
	Task<Score> AddAsync(ScoreData data, CancellationToken cancellationToken = default);
}