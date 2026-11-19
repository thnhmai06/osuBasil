using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Persistence;

public interface IUserStatsRepository
{
	ValueTask<UserStats> GetAsync(UserRecord user, GameMode mode, CancellationToken ct = default);

	Task CreateOrUpdateAsync(UserStats stats);

	ValueTask<UserStats> AddPlayAsync(UserRecord user, GameMode mode, long totalScore, long rankedScore);
}