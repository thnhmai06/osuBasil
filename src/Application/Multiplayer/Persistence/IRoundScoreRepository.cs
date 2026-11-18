using Basil.Application.Shared.Paging;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Persistence;

public interface IRoundScoreRepository
{
	Task<RoundScore> RecordAsync(RoundScore roundScore);

	Task<IReadOnlyList<RoundScore>> ListAsync(Round round, CancellationToken ct = default);
}