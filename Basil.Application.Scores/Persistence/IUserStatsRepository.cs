using Basil.Domain.Game;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Persistence;

public interface IUserStatsRepository
{
	ValueTask<UserStats> GetAsync(UserRecord user, GameMode mode, CancellationToken ct = default);

	/// <summary>Gets the statistics of each user in the paired mode, in the order of <paramref name="players"/>.</summary>
	Task<IReadOnlyList<UserStats>> ListAsync(IReadOnlyList<(UserRecord User, GameMode Mode)> players,
		CancellationToken ct = default);

	Task CreateOrUpdateAsync(UserStats stats);

	ValueTask<UserStats> AddPlayAsync(UserRecord user, GameMode mode, long totalScore, long rankedScore);
}