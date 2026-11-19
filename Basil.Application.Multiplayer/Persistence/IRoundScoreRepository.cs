using Basil.Domain.Multiplayer.Report.Rounds;

namespace Basil.Application.Multiplayer.Persistence;

public interface IRoundScoreRepository
{
	Task<RoundScore> RecordAsync(RoundScore roundScore);

	Task<IReadOnlyList<RoundScore>> ListAsync(Round round, CancellationToken ct = default);
}