using Basil.Domain.Multiplayer.Report.Matches;
using Basil.Domain.Multiplayer.Report.Rounds;

namespace Basil.Application.Multiplayer.Persistence;

public interface IRoundRepository
{
	Task CreateOrUpdateAsync(Round round);

	Task<IReadOnlyList<Round>> ListAsync(MatchRecord match, CancellationToken ct = default);
}