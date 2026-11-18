using Basil.Application.Shared.Paging;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Persistence;

public interface IRoundRepository
{
	Task CreateOrUpdateAsync(Round round);

	Task<IReadOnlyList<Round>> ListAsync(MatchRecord match, CancellationToken ct = default);
}