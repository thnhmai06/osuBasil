using Basil.Application.Shared.Paging;
using Basil.Domain.Game.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Persistence;

public interface IScoreRepository
{
	Task<ScoreRecord> CreateAsync(ScoreData data);

	Task<(ScoreRecord Score, UserStats Stats)> CreateWithPlayAsync(ScoreData data, long rankedScore);

	Task SetPpAsync(int id, double pp);

	Task<IReadOnlyList<ScoreRecord>> ListForPpAsync(int afterId, bool onlyMissing, int limit,
		CancellationToken ct = default);

	ValueTask<ScoreRecord?> GetAsync(int id, CancellationToken ct = default);

	Task<IReadOnlyList<ScoreRecord>> GetManyAsync(IReadOnlyCollection<int> ids,
		CancellationToken ct = default);

	Task<Page<ScoreRecord>> ListAsync(ScoreQuery query, PageRequest page,
		CancellationToken ct = default);
}